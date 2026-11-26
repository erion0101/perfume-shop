using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using PerfumeShop.Modules.Orders.Contracts;
using PerfumeShop.Modules.Payments.Domain;
using PerfumeShop.Modules.Payments.Infrastructure.Providers;
using PerfumeShop.Modules.Shipping.Contracts;
using PerfumeShop.Shared.Infrastructure.Events;
using PerfumeShop.Shared.Infrastructure.Outbox;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Payments.Infrastructure;

/// <summary>
/// Keeps payments and receipts in step with Orders and Shipping. Every event is applied at most once per consumer
/// (inbox row in the same save); out-of-order or missing history is tolerated (the payment is created on demand).
/// Receipts are stored first and sent to the fiscal provider after the save, so a provider failure never loses the event.
/// </summary>
internal sealed class PaymentEventHandlers(
    PaymentsDbContext db,
    IOrdersApi orders,
    ReceiptNumbers receiptNumbers,
    ReceiptIssuer issuer,
    IPaymentProvider paymentProvider,
    TimeProvider timeProvider,
    ILogger<PaymentEventHandlers> logger) :
    IIntegrationEventHandler<OrderPlacedIntegrationEvent>,
    IIntegrationEventHandler<OrderShippedIntegrationEvent>,
    IIntegrationEventHandler<OrderCashCollectedIntegrationEvent>,
    IIntegrationEventHandler<OrderCancelledIntegrationEvent>,
    IIntegrationEventHandler<OrderExpiredIntegrationEvent>,
    IIntegrationEventHandler<OrderRefusedIntegrationEvent>,
    IIntegrationEventHandler<CodSettledIntegrationEvent>
{
    public const string Consumer = "payments";

    public Task HandleAsync(OrderPlacedIntegrationEvent e, CancellationToken ct = default) =>
        OnceAsync(e.Id, ct, async () => { await EnsurePaymentAsync(e.OrderId, ct); });

    /// <summary>The parcel leaves: the sale receipt is created (and travels in the parcel).</summary>
    public async Task HandleAsync(OrderShippedIntegrationEvent e, CancellationToken ct = default)
    {
        await OnceAsync(e.Id, ct, async () =>
        {
            var order = await orders.GetOrderAsync(e.OrderId, ct);
            var payment = await EnsurePaymentAsync(e.OrderId, ct, order);
            payment?.RecordShipment(e.CourierName, e.TrackingNumber, e.OccurredOnUtc);

            if (order is null || await db.Receipts.AnyAsync(r => r.OrderId == e.OrderId && r.Kind == ReceiptKind.Sale, ct))
            {
                return;
            }

            var number = await receiptNumbers.NextAsync(timeProvider.GetUtcNow().UtcDateTime.Year, ct);
            var sale = FiscalReceipt.Sale(number, order.OrderId, order.OrderNumber, order.PaymentMethod, ReceiptContent.FromOrder(order));
            if (sale.IsFailure)
            {
                logger.LogError("Sale receipt for {Order} could not be built: {Error}", order.OrderNumber, sale.Error.Message);
                return;
            }

            if (sale.Value.VatTotal != order.VatAmount)
            {
                logger.LogWarning("Receipt VAT {ReceiptVat} differs from order VAT {OrderVat} for {Order}", sale.Value.VatTotal, order.VatAmount, order.OrderNumber);
            }

            db.Receipts.Add(sale.Value);
        });

        await IssuePendingAsync(e.OrderId, ReceiptKind.Sale, ct);
    }

    public Task HandleAsync(OrderCashCollectedIntegrationEvent e, CancellationToken ct = default) =>
        OnceAsync(e.Id, ct, async () =>
        {
            var payment = await EnsurePaymentAsync(e.OrderId, ct);
            Report(payment?.MarkCollected(e.AmountCollected, e.OccurredOnUtc), e.OrderNumber);
        });

    public Task HandleAsync(OrderCancelledIntegrationEvent e, CancellationToken ct = default) => CancelAsync(e.Id, e.OrderId, e.OrderNumber, ct);

    public Task HandleAsync(OrderExpiredIntegrationEvent e, CancellationToken ct = default) => CancelAsync(e.Id, e.OrderId, e.OrderNumber, ct);

    /// <summary>The refused parcel is back: no money, and the sale receipt is cancelled with a storno.</summary>
    public async Task HandleAsync(OrderRefusedIntegrationEvent e, CancellationToken ct = default)
    {
        await OnceAsync(e.Id, ct, async () =>
        {
            var payment = await EnsurePaymentAsync(e.OrderId, ct);
            Report(payment?.MarkFailed(e.OccurredOnUtc), e.OrderNumber);

            var sale = await db.Receipts.FirstOrDefaultAsync(r => r.OrderId == e.OrderId && r.Kind == ReceiptKind.Sale, ct);
            if (sale is null || await db.Receipts.AnyAsync(r => r.OrderId == e.OrderId && r.Kind == ReceiptKind.Storno, ct))
            {
                return;
            }

            if (sale.Status != ReceiptStatus.Issued)
            {
                sale.Void("The parcel was refused before the sale receipt was issued.");
                return;
            }

            var number = await receiptNumbers.NextAsync(timeProvider.GetUtcNow().UtcDateTime.Year, ct);
            db.Receipts.Add(FiscalReceipt.Storno(number, sale).Value);
        });

        await IssuePendingAsync(e.OrderId, ReceiptKind.Storno, ct);
    }

    /// <summary>Courier statement: fee and net per order, linked to the Shipping settlement.</summary>
    public Task HandleAsync(CodSettledIntegrationEvent e, CancellationToken ct = default) =>
        OnceAsync(e.Id, ct, async () =>
        {
            foreach (var line in e.Lines)
            {
                var payment = await EnsurePaymentAsync(line.OrderId, ct);
                Report(payment?.RecordSettlement(e.SettlementId, e.Reference, line.CollectedAmount, line.Fee, e.OccurredOnUtc), line.OrderNumber);
            }
        });

    private Task CancelAsync(Guid eventId, Guid orderId, string orderNumber, CancellationToken ct) =>
        OnceAsync(eventId, ct, async () =>
        {
            var payment = await EnsurePaymentAsync(orderId, ct);
            Report(payment?.Cancel(timeProvider.GetUtcNow().UtcDateTime), orderNumber);
        });

    private async Task OnceAsync(Guid eventId, CancellationToken ct, Func<Task> apply)
    {
        if (await db.HasProcessedAsync(eventId, Consumer, ct))
        {
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await apply();
        db.MarkProcessed(eventId, Consumer, timeProvider.GetUtcNow().UtcDateTime);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Delivered twice at the same moment: the other delivery won.
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            if (!await db.HasProcessedAsync(eventId, Consumer, ct))
            {
                throw;
            }
        }
    }

    /// <summary>The payment of an order, created from Orders if its OrderPlaced event was missed.</summary>
    private async Task<Payment?> EnsurePaymentAsync(Guid orderId, CancellationToken ct, OrderSummaryDto? order = null)
    {
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.OrderId == orderId, ct)
            ?? db.Payments.Local.FirstOrDefault(p => p.OrderId == orderId);
        if (payment is not null)
        {
            return payment;
        }

        order ??= await orders.GetOrderAsync(orderId, ct);
        if (order is null)
        {
            logger.LogWarning("Order {OrderId} not found: no payment created", orderId);
            return null;
        }

        payment = Payment.Expect(order.OrderId, order.OrderNumber, order.CustomerId, order.Total, paymentProvider.Method.ToString(), order.PlacedAtUtc);
        db.Payments.Add(payment);
        return payment;
    }

    private async Task IssuePendingAsync(Guid orderId, ReceiptKind kind, CancellationToken ct)
    {
        var pending = await db.Receipts.AsNoTracking()
            .Where(r => r.OrderId == orderId && r.Kind == kind && r.Status == ReceiptStatus.Pending)
            .Select(r => r.Id)
            .FirstOrDefaultAsync(ct);
        if (pending != Guid.Empty)
        {
            db.ChangeTracker.Clear();
            await issuer.IssueAsync(pending, ct);
        }
    }

    private void Report(Result<PaymentChange>? change, string orderNumber)
    {
        if (change is { IsFailure: true })
        {
            logger.LogWarning("Payment event for {Order} ignored: {Error}", orderNumber, change.Error.Message);
        }
    }
}
