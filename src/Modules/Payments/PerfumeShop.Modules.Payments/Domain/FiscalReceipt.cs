using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Payments.Domain;

internal enum ReceiptKind
{
    Sale = 1,
    /// <summary>Cancels a sale receipt (refused parcel). Amounts are negative.</summary>
    Storno = 2,
}

internal enum ReceiptStatus
{
    /// <summary>Created, not sent to the fiscal provider yet (or the process stopped before).</summary>
    Pending = 1,
    Issued = 2,
    /// <summary>The provider refused or is not configured: retry when fixed.</summary>
    Failed = 3,
    /// <summary>A sale that was never issued and is no longer needed (the parcel came back first).</summary>
    Voided = 4,
}

/// <summary>Kosovo VAT, 18%: prices are gross, the VAT part is computed per line and rounded half away from zero.</summary>
internal static class ReceiptVat
{
    public const decimal Rate = 0.18m;

    /// <summary>Same rounding as Orders, so receipt VAT equals the order's VAT.</summary>
    public static decimal Of(decimal gross) => decimal.Round(gross * Rate / (1 + Rate), 2, MidpointRounding.AwayFromZero);

    public static IReadOnlyList<VatGroup> Breakdown(IEnumerable<ReceiptLine> lines) =>
        [.. lines.GroupBy(l => l.VatRate).OrderByDescending(g => g.Key).Select(g =>
        {
            var gross = g.Sum(l => l.LineTotal);
            var vat = g.Sum(l => l.VatAmount);
            return new VatGroup(g.Key, gross - vat, vat, gross);
        })];
}

/// <param name="VatRate">0.18 = 18%.</param>
/// <param name="Batches">Batch codes shipped (e.g. "B2391×1"): traceability on the receipt.</param>
internal sealed record ReceiptLine(string Description, string? Sku, decimal Quantity, decimal UnitPrice, decimal LineTotal, decimal VatRate, decimal VatAmount, string? Batches);

internal sealed record VatGroup(decimal Rate, decimal Net, decimal Vat, decimal Gross);

/// <summary>One call to the fiscal provider, kept for audit and re-issuing.</summary>
internal sealed class FiscalReceiptAttempt
{
    private FiscalReceiptAttempt() { }

    internal FiscalReceiptAttempt(Guid receiptId, string provider, bool succeeded, string requestJson, string? responseJson, string? error, DateTime atUtc)
    {
        ReceiptId = receiptId;
        Provider = provider;
        Succeeded = succeeded;
        RequestJson = requestJson;
        ResponseJson = responseJson;
        Error = error;
        AttemptedAtUtc = atUtc;
    }

    public Guid Id { get; private init; } = Guid.CreateVersion7();

    public Guid ReceiptId { get; private init; }

    public string Provider { get; private init; } = null!;

    public bool Succeeded { get; private init; }

    public string RequestJson { get; private init; } = null!;

    public string? ResponseJson { get; private init; }

    public string? Error { get; private init; }

    public DateTime AttemptedAtUtc { get; private init; }
}

/// <summary>
/// A receipt for an order: a Sale when it ships, a Storno (negative, linked to the sale) when the refused parcel is back.
/// The content is frozen when created; the provider only adds its fiscal number.
/// </summary>
internal sealed class FiscalReceipt : AggregateRoot<Guid>, IAuditable
{
    public const string Currency = Money.Eur;

    private readonly List<FiscalReceiptAttempt> _attempts = [];

    private FiscalReceipt() { }

    private FiscalReceipt(Guid id) : base(id) { }

    /// <summary>Our own number, e.g. "PS-R-2026-000042" (not the fiscal number).</summary>
    public string InternalNumber { get; private set; } = null!;

    public Guid OrderId { get; private set; }

    public string OrderNumber { get; private set; } = null!;

    public ReceiptKind Kind { get; private set; }

    public Guid? OriginalReceiptId { get; private set; }

    public string? OriginalInternalNumber { get; private set; }

    public ReceiptStatus Status { get; private set; }

    public IReadOnlyList<ReceiptLine> Lines { get; private set; } = [];

    public IReadOnlyList<VatGroup> VatBreakdown { get; private set; } = [];

    public decimal Total { get; private set; }

    public decimal VatTotal { get; private set; }

    public string PaymentMethod { get; private set; } = null!;

    public string? Provider { get; private set; }

    /// <summary>False for the development provider: the receipt has no legal value.</summary>
    public bool IsFiscal { get; private set; }

    public string? FiscalNumber { get; private set; }

    public DateTime? IssuedAtUtc { get; private set; }

    public string? LastError { get; private set; }

    public string? VoidReason { get; private set; }

    public IReadOnlyList<FiscalReceiptAttempt> Attempts => _attempts.AsReadOnly();

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static Result<FiscalReceipt> Sale(string internalNumber, Guid orderId, string orderNumber, string paymentMethod, IReadOnlyList<ReceiptLine> lines)
    {
        if (lines.Count == 0 || lines.Any(l => l.Quantity <= 0 || l.LineTotal < 0))
        {
            return Error.Validation("Payments.Receipt.LinesInvalid", "A sale receipt needs lines with positive quantities.");
        }

        var receipt = new FiscalReceipt(Guid.CreateVersion7())
        {
            InternalNumber = internalNumber,
            OrderId = orderId,
            OrderNumber = orderNumber,
            Kind = ReceiptKind.Sale,
            Status = ReceiptStatus.Pending,
            PaymentMethod = paymentMethod,
        };
        receipt.SetLines(lines);
        return receipt;
    }

    /// <summary>Negative copy of an issued sale, linked to it.</summary>
    public static Result<FiscalReceipt> Storno(string internalNumber, FiscalReceipt sale)
    {
        if (sale.Kind != ReceiptKind.Sale || sale.Status != ReceiptStatus.Issued)
        {
            return Error.Conflict("Payments.Receipt.StornoNeedsIssuedSale", "Only an issued sale receipt can be cancelled with a storno.");
        }

        var storno = new FiscalReceipt(Guid.CreateVersion7())
        {
            InternalNumber = internalNumber,
            OrderId = sale.OrderId,
            OrderNumber = sale.OrderNumber,
            Kind = ReceiptKind.Storno,
            OriginalReceiptId = sale.Id,
            OriginalInternalNumber = sale.InternalNumber,
            Status = ReceiptStatus.Pending,
            PaymentMethod = sale.PaymentMethod,
        };
        storno.SetLines([.. sale.Lines.Select(l => l with { Quantity = -l.Quantity, LineTotal = -l.LineTotal, VatAmount = -l.VatAmount })]);
        return storno;
    }

    public void RecordAttempt(string provider, bool succeeded, string requestJson, string? responseJson, string? error, DateTime atUtc) =>
        _attempts.Add(new FiscalReceiptAttempt(Id, provider, succeeded, requestJson, responseJson, error, atUtc));

    public Result MarkIssued(string provider, string fiscalNumber, bool isFiscal, DateTime atUtc)
    {
        if (Status is ReceiptStatus.Issued or ReceiptStatus.Voided)
        {
            return Error.Conflict("Payments.Receipt.NotIssuable", $"Receipt {InternalNumber} is {Status}.");
        }

        Status = ReceiptStatus.Issued;
        Provider = provider;
        FiscalNumber = fiscalNumber;
        IsFiscal = isFiscal;
        IssuedAtUtc = atUtc;
        LastError = null;
        return Result.Success();
    }

    public void MarkFailed(string provider, string error)
    {
        if (Status is ReceiptStatus.Issued or ReceiptStatus.Voided)
        {
            return;
        }

        Status = ReceiptStatus.Failed;
        Provider = provider;
        LastError = error.Length > 1000 ? error[..1000] : error;
    }

    /// <summary>A sale that never got issued is not needed anymore (the order was refused first).</summary>
    public Result Void(string reason)
    {
        if (Status == ReceiptStatus.Issued)
        {
            return Error.Conflict("Payments.Receipt.AlreadyIssued", "An issued receipt is cancelled with a storno, not voided.");
        }

        Status = ReceiptStatus.Voided;
        VoidReason = reason;
        return Result.Success();
    }

    public bool CanBeIssued => Status is ReceiptStatus.Pending or ReceiptStatus.Failed;

    private void SetLines(IReadOnlyList<ReceiptLine> lines)
    {
        Lines = lines;
        VatBreakdown = ReceiptVat.Breakdown(lines);
        Total = lines.Sum(l => l.LineTotal);
        VatTotal = lines.Sum(l => l.VatAmount);
    }
}
