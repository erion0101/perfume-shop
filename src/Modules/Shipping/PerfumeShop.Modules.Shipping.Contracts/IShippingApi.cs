using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Shipping.Contracts;

/// <summary>Shipment information for other modules (e.g. Notifications sending the tracking number).</summary>
public interface IShippingApi
{
    Task<ShipmentSummaryDto?> GetShipmentForOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    Task<ShipmentSummaryDto?> GetShipmentByTrackingNumberAsync(string trackingNumber, CancellationToken cancellationToken = default);

    /// <summary>Delivered parcels whose cash the courier has not paid within its remittance days (oldest first).</summary>
    Task<IReadOnlyList<OverdueCodDto>> GetOverdueCodAsync(CancellationToken cancellationToken = default);
}

public sealed record OverdueCodDto(
    Guid ShipmentId, string OrderNumber, string CourierName, string TrackingNumber, decimal CodAmount, DateTime DeliveredAtUtc, int DaysSinceDelivery, int RemittanceDays);

/// <param name="Status">Created, HandedOver, InTransit, Delivered, Refused, ReturnedToWarehouse.</param>
public sealed record ShipmentSummaryDto(
    Guid ShipmentId,
    Guid OrderId,
    string OrderNumber,
    Guid CourierId,
    string CourierName,
    string TrackingNumber,
    string Status,
    decimal CodAmount,
    DateTime CreatedAtUtc,
    DateTime? DeliveredAtUtc,
    DateTime? SettledAtUtc);

// Published through the Shipping outbox (at-least-once; handlers must be idempotent).

public sealed record ShipmentCreatedIntegrationEvent(
    Guid Id, DateTime OccurredOnUtc, Guid ShipmentId, Guid OrderId, string OrderNumber, string CourierName, string TrackingNumber, decimal CodAmount)
    : IntegrationEvent(Id, OccurredOnUtc);

/// <summary>The courier picked up the parcel (customer SMS with courier, tracking number and COD amount).</summary>
public sealed record ShipmentHandedOverIntegrationEvent(
    Guid Id, DateTime OccurredOnUtc, Guid ShipmentId, Guid OrderId, string OrderNumber, string CourierName, string TrackingNumber, decimal CodAmount)
    : IntegrationEvent(Id, OccurredOnUtc);

public sealed record ShipmentDeliveredIntegrationEvent(
    Guid Id, DateTime OccurredOnUtc, Guid ShipmentId, Guid OrderId, string OrderNumber, string TrackingNumber)
    : IntegrationEvent(Id, OccurredOnUtc);

/// <summary>The courier reports the parcel was refused; it is on its way back (stock returns on ReturnedToWarehouse).</summary>
public sealed record ShipmentRefusedIntegrationEvent(
    Guid Id, DateTime OccurredOnUtc, Guid ShipmentId, Guid OrderId, string OrderNumber, string TrackingNumber, string? Reason)
    : IntegrationEvent(Id, OccurredOnUtc);

/// <summary>A courier remittance statement was imported; <paramref name="Lines"/> are the parcels booked as paid.</summary>
public sealed record CodSettledIntegrationEvent(
    Guid Id, DateTime OccurredOnUtc, Guid SettlementId, Guid CourierId, string CourierName, string Reference,
    int MatchedCount, decimal MatchedAmount, decimal TotalFees, decimal NetPaid, int MismatchCount, IReadOnlyList<CodSettledLine> Lines)
    : IntegrationEvent(Id, OccurredOnUtc);

/// <param name="Fee">COD commission the courier kept for this parcel.</param>
public sealed record CodSettledLine(Guid ShipmentId, Guid OrderId, string OrderNumber, string TrackingNumber, decimal CollectedAmount, decimal Fee);
