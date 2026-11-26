using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using PerfumeShop.Modules.Notifications.Domain;
using PerfumeShop.Modules.Orders.Contracts;
using PerfumeShop.Modules.Payments.Contracts;
using PerfumeShop.Modules.Shipping.Contracts;
using PerfumeShop.Shared.Infrastructure.Events;
using PerfumeShop.Shared.Infrastructure.Jobs;
using PerfumeShop.Shared.Infrastructure.Outbox;

namespace PerfumeShop.Modules.Notifications.Infrastructure;

/// <summary>
/// Customer SMS and staff alerts from Orders, Shipping and Payments events. Each event is handled once
/// (inbox, same transaction as the queued messages). Refused parcels deliberately get no SMS.
/// </summary>
internal sealed class NotificationEventHandlers(
    NotificationsDbContext db,
    NotificationQueue queue,
    IOrdersApi orders,
    IOptions<NotificationOptions> options,
    TimeProvider timeProvider,
    ILogger<NotificationEventHandlers> logger) :
    IIntegrationEventHandler<OrderPlacedIntegrationEvent>,
    IIntegrationEventHandler<OrderConfirmedIntegrationEvent>,
    IIntegrationEventHandler<OrderCancelledIntegrationEvent>,
    IIntegrationEventHandler<OrderExpiredIntegrationEvent>,
    IIntegrationEventHandler<ShipmentHandedOverIntegrationEvent>,
    IIntegrationEventHandler<ShipmentDeliveredIntegrationEvent>,
    IIntegrationEventHandler<FiscalReceiptFailedIntegrationEvent>,
    IJobFailureListener,
    IDeadLetterListener
{
    public const string Consumer = "notifications";

    public Task HandleAsync(OrderPlacedIntegrationEvent e, CancellationToken ct = default) =>
        OnceAsync(e.Id, ct, async () =>
        {
            var source = e.Id.ToString();
            await queue.CustomerSmsAsync(TemplateKeys.OrderPlaced, e.CustomerPhone,
                new Dictionary<string, string> { ["orderNumber"] = e.OrderNumber, ["total"] = Format.Amount(e.Total) }, source, e.OrderNumber, ct);

            var order = await orders.GetOrderAsync(e.OrderId, ct);
            await queue.StaffAlertAsync(TemplateKeys.StaffNewOrder, new Dictionary<string, string>
            {
                ["orderNumber"] = e.OrderNumber,
                ["total"] = Format.Amount(e.Total),
                ["placedAt"] = Format.LocalDateTime(e.OccurredOnUtc, options.Value.TimeZone),
                ["customerName"] = order?.CustomerName ?? string.Empty,
                ["customerPhone"] = e.CustomerPhone,
                ["city"] = order?.DeliveryAddress.City ?? string.Empty,
                ["items"] = order is null ? string.Empty : string.Join("\n", order.Lines.Select(l =>
                    $"{l.Quantity} × {l.BrandName} {l.FragranceName}{Format.Size(l.SizeValue, l.SizeUnit)} – {Format.Amount(l.LineTotal)} EUR")),
            }, source, e.OrderNumber, ct);
        });

    public Task HandleAsync(OrderConfirmedIntegrationEvent e, CancellationToken ct = default) =>
        CustomerAsync(e.Id, e.OrderId, e.OrderNumber, TemplateKeys.OrderConfirmed, _ => [], ct);

    public Task HandleAsync(OrderCancelledIntegrationEvent e, CancellationToken ct = default) =>
        CustomerAsync(e.Id, e.OrderId, e.OrderNumber, TemplateKeys.OrderCancelled, _ => [], ct);

    public Task HandleAsync(OrderExpiredIntegrationEvent e, CancellationToken ct = default) =>
        CustomerAsync(e.Id, e.OrderId, e.OrderNumber, TemplateKeys.OrderExpired, _ => [], ct);

    /// <summary>Sent when the courier picks the parcel up, not when the shipment is booked.</summary>
    public Task HandleAsync(ShipmentHandedOverIntegrationEvent e, CancellationToken ct = default) =>
        CustomerAsync(e.Id, e.OrderId, e.OrderNumber, TemplateKeys.ShipmentHandedOver, _ => new Dictionary<string, string>
        {
            ["courier"] = e.CourierName,
            ["trackingNumber"] = e.TrackingNumber,
            ["codAmount"] = Format.Amount(e.CodAmount),
        }, ct);

    public Task HandleAsync(ShipmentDeliveredIntegrationEvent e, CancellationToken ct = default) =>
        CustomerAsync(e.Id, e.OrderId, e.OrderNumber, TemplateKeys.ShipmentDelivered, _ => [], ct);

    public Task HandleAsync(FiscalReceiptFailedIntegrationEvent e, CancellationToken ct = default) =>
        OnceAsync(e.Id, ct, () => queue.StaffAlertAsync(TemplateKeys.StaffReceiptFailed, new Dictionary<string, string>
        {
            ["receiptNumber"] = e.InternalNumber,
            ["kind"] = e.Kind,
            ["orderNumber"] = e.OrderNumber,
            ["attempts"] = e.Attempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["error"] = e.Error,
            ["receiptId"] = e.ReceiptId.ToString(),
        }, e.Id.ToString(), e.OrderNumber, ct));

    /// <summary>Hangfire job ended Failed: one alert per job id.</summary>
    public async Task OnJobFailedAsync(JobFailure failure, CancellationToken cancellationToken)
    {
        await queue.StaffAlertAsync(TemplateKeys.StaffJobFailed, new Dictionary<string, string>
        {
            ["job"] = failure.JobName,
            ["recurringJob"] = failure.RecurringJobId ?? "-",
            ["jobId"] = failure.JobId,
            ["failedAt"] = Format.LocalDateTime(failure.FailedAtUtc, options.Value.TimeZone),
            ["error"] = failure.Reason,
        }, $"job-{failure.JobId}", null, cancellationToken);
        await SaveIgnoringDuplicateAsync(cancellationToken);
    }

    private async Task SaveIgnoringDuplicateAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear(); // the same failure was reported twice
        }
    }

    /// <summary>An event failed for the last time in some module's outbox: one alert per message.</summary>
    public async Task OnDeadLetterAsync(DeadLetter deadLetter, CancellationToken cancellationToken)
    {
        var zone = options.Value.TimeZone;
        await queue.StaffAlertAsync(TemplateKeys.StaffDeadLetter, new Dictionary<string, string>
        {
            ["eventType"] = deadLetter.EventType,
            ["module"] = deadLetter.Schema,
            ["messageId"] = deadLetter.MessageId.ToString(),
            ["attempts"] = deadLetter.Attempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["occurredAt"] = Format.LocalDateTime(deadLetter.OccurredOnUtc, zone),
            ["failedAt"] = Format.LocalDateTime(deadLetter.FailedAtUtc, zone),
            ["error"] = deadLetter.Error.Length > 1500 ? deadLetter.Error[..1500] : deadLetter.Error,
        }, $"dead-letter-{deadLetter.MessageId}", null, cancellationToken);
        await SaveIgnoringDuplicateAsync(cancellationToken);
    }

    private Task CustomerAsync(Guid eventId, Guid orderId, string orderNumber, string templateKey,
        Func<OrderSummaryDto, Dictionary<string, string>> values, CancellationToken ct) =>
        OnceAsync(eventId, ct, async () =>
        {
            var order = await orders.GetOrderAsync(orderId, ct);
            if (order is null)
            {
                logger.LogWarning("Order {Order} not found for notification {Template}", orderNumber, templateKey);
                return;
            }

            var all = values(order);
            all["orderNumber"] = orderNumber;
            await queue.CustomerSmsAsync(templateKey, order.CustomerPhone, all, eventId.ToString(), orderNumber, ct);
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
            // Delivered twice at the same moment: the other delivery won (inbox or message dedup key).
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            if (!await db.HasProcessedAsync(eventId, Consumer, ct))
            {
                throw;
            }
        }
    }
}
