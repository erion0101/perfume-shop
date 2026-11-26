using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Payments.Contracts;

/// <summary>Payment and receipt information for other modules (e.g. Notifications, an admin dashboard).</summary>
public interface IPaymentsApi
{
    Task<PaymentSummaryDto?> GetPaymentForOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReceiptSummaryDto>> GetReceiptsForOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>The sale receipt of each order that has one (Shipping checks them before handing parcels to the courier).</summary>
    Task<IReadOnlyDictionary<Guid, ReceiptSummaryDto>> GetSaleReceiptsAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default);
}

/// <param name="Status">Expected, Collected, Failed, Cancelled.</param>
/// <param name="NetReceived">Collected minus the courier's COD fee (known after the courier statement).</param>
public sealed record PaymentSummaryDto(
    Guid PaymentId,
    Guid OrderId,
    string OrderNumber,
    string Method,
    string Status,
    decimal ExpectedAmount,
    decimal? CollectedAmount,
    decimal? CourierFee,
    decimal? NetReceived,
    Guid? SettlementId);

/// <param name="Kind">Sale or Storno.</param>
/// <param name="Status">Pending, Issued, Failed, Voided.</param>
/// <param name="IsFiscal">False for development receipts (no legal value).</param>
public sealed record ReceiptSummaryDto(
    Guid ReceiptId,
    string Kind,
    string Status,
    string InternalNumber,
    string? FiscalNumber,
    bool IsFiscal,
    decimal Total,
    DateTime? IssuedAtUtc);

/// <summary>A receipt could not be issued (published once per failed attempt, through the Payments outbox).</summary>
/// <param name="Kind">Sale or Storno.</param>
public sealed record FiscalReceiptFailedIntegrationEvent(
    Guid Id, DateTime OccurredOnUtc, Guid ReceiptId, string InternalNumber, string Kind, Guid OrderId, string OrderNumber, string Error, int Attempts)
    : IntegrationEvent(Id, OccurredOnUtc);
