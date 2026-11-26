using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PerfumeShop.Modules.Orders.Contracts;
using PerfumeShop.Modules.Payments.Contracts;
using PerfumeShop.Modules.Payments.Domain;
using PerfumeShop.Modules.Payments.Infrastructure.Fiscal;
using PerfumeShop.Shared.Infrastructure.Outbox;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Payments.Infrastructure;

internal static class ReceiptContent
{
    /// <summary>Receipt lines from the order snapshot (plus delivery), with 18% VAT per line.</summary>
    public static IReadOnlyList<ReceiptLine> FromOrder(OrderSummaryDto order)
    {
        List<ReceiptLine> lines = [.. order.Lines.Select(l =>
        {
            var size = l.SizeValue is { } v
                ? FormattableString.Invariant($" {v:0.#} {(l.SizeUnit switch { "Ml" => "ml", "Gram" => "g", _ => "copë" })}")
                : string.Empty;
            return new ReceiptLine($"{l.BrandName} {l.FragranceName}{size}", l.Sku, l.Quantity, l.UnitPrice, l.LineTotal,
                ReceiptVat.Rate, ReceiptVat.Of(l.LineTotal), l.Batches.Count > 0 ? string.Join(", ", l.Batches) : null);
        })];

        if (order.DeliveryFee > 0)
        {
            lines.Add(new ReceiptLine("Transporti", null, 1, order.DeliveryFee, order.DeliveryFee, ReceiptVat.Rate, ReceiptVat.Of(order.DeliveryFee), null));
        }

        return lines;
    }
}

/// <summary>PS-R-{year}-{000000}, allocated in the caller's transaction (no gaps).</summary>
internal sealed class ReceiptNumbers(PaymentsDbContext db)
{
    public async Task<string> NextAsync(int year, CancellationToken cancellationToken)
    {
        var next = await db.Database.SqlQuery<int>($"""
            INSERT INTO payments.receipt_number_counters (year, last_number) VALUES ({year}, 1)
            ON CONFLICT (year) DO UPDATE SET last_number = receipt_number_counters.last_number + 1
            RETURNING last_number AS "Value"
            """).ToListAsync(cancellationToken);
        return string.Create(CultureInfo.InvariantCulture, $"PS-R-{year}-{next.Single():D6}");
    }
}

/// <summary>Sends a stored receipt to the fiscal provider and records the request/response. Safe to call again.</summary>
internal sealed class ReceiptIssuer(
    PaymentsDbContext db,
    IFiscalReceiptService provider,
    BusinessDetails business,
    TimeProvider timeProvider,
    ILogger<ReceiptIssuer> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Result<FiscalReceipt>> IssueAsync(Guid receiptId, CancellationToken cancellationToken)
    {
        var receipt = await db.Receipts.Include(r => r.Attempts).FirstOrDefaultAsync(r => r.Id == receiptId, cancellationToken);
        if (receipt is null)
        {
            return Error.NotFound("Payments.Receipt.NotFound", "Receipt not found.");
        }

        if (!receipt.CanBeIssued)
        {
            return receipt; // already issued or voided: nothing to do
        }

        string? originalFiscalNumber = null;
        if (receipt.OriginalReceiptId is { } originalId)
        {
            originalFiscalNumber = await db.Receipts.Where(r => r.Id == originalId).Select(r => r.FiscalNumber).FirstOrDefaultAsync(cancellationToken);
        }

        var info = await business.GetAsync(cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var request = new FiscalReceiptRequest(
            receipt.Id, receipt.InternalNumber, receipt.Kind.ToString(), receipt.OrderNumber, originalFiscalNumber,
            info.Name, info.TaxNumber,
            [.. receipt.Lines.Select(l => new FiscalLineRequest(l.Description, l.Sku, l.Quantity, l.UnitPrice, l.LineTotal, l.VatRate, l.VatAmount))],
            [.. receipt.VatBreakdown.Select(v => new FiscalVatRequest(v.Rate, v.Net, v.Vat, v.Gross))],
            receipt.Total, receipt.VatTotal, FiscalReceipt.Currency, receipt.PaymentMethod, now);
        var requestJson = JsonSerializer.Serialize(request, Json);

        FiscalIssueResult result;
        try
        {
            result = await provider.IssueAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result = FiscalIssueResult.Failed($"{provider.Name} threw: {ex.Message}");
        }

        receipt.RecordAttempt(provider.Name, result.Succeeded, requestJson, result.ResponseJson, result.Error, now);
        if (result.Succeeded && result.FiscalNumber is not null)
        {
            receipt.MarkIssued(provider.Name, result.FiscalNumber, result.IsFiscal, now);
        }
        else
        {
            receipt.MarkFailed(provider.Name, result.Error ?? "The provider returned no fiscal number.");
            logger.LogError("Receipt {Number} ({Kind}) for {Order} failed: {Error}", receipt.InternalNumber, receipt.Kind, receipt.OrderNumber, receipt.LastError);
            db.AddIntegrationEvent(new FiscalReceiptFailedIntegrationEvent(
                Guid.CreateVersion7(), now, receipt.Id, receipt.InternalNumber, receipt.Kind.ToString(), receipt.OrderId, receipt.OrderNumber,
                receipt.LastError!, receipt.Attempts.Count));
        }

        await db.SaveChangesAsync(cancellationToken);
        return receipt;
    }
}
