using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Orders.Contracts;

/// <summary>Read access to orders for other modules (Shipping, Payments, Notifications).</summary>
public interface IOrdersApi
{
    Task<OrderSummaryDto?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <param name="orderNumber">e.g. "PS-2026-000123".</param>
    Task<OrderSummaryDto?> GetOrderByNumberAsync(string orderNumber, CancellationToken cancellationToken = default);

    /// <summary>Order numbers for ids (e.g. Inventory's batch trace); unknown ids are left out.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetOrderNumbersAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default);

    /// <summary>Order and refused-parcel counts per customer (customers without orders are left out).</summary>
    Task<IReadOnlyDictionary<Guid, CustomerOrderStatsDto>> GetCustomerOrderStatsAsync(IReadOnlyCollection<Guid> customerIds, CancellationToken cancellationToken = default);

    /// <summary>Customers with at least one refused parcel (admin "has refusals" filter).</summary>
    Task<IReadOnlyList<Guid>> GetCustomerIdsWithRefusalsAsync(CancellationToken cancellationToken = default);

    // Status changes driven by Shipping. All are idempotent: repeating a call for an order that is
    // already in that state succeeds without changing anything. Actors are staff user ids.

    /// <summary>Confirmed → Shipped: stock leaves the warehouse (oldest batches first).</summary>
    Task<Result<OrderSummaryDto>> MarkShippedAsync(Guid orderId, string courierName, string trackingNumber, string actorId, CancellationToken cancellationToken = default);

    /// <summary>Shipped → Delivered.</summary>
    Task<Result<OrderSummaryDto>> MarkDeliveredAsync(Guid orderId, string actorId, CancellationToken cancellationToken = default);

    /// <summary>Shipped → Refused: the parcel is back and its units return to stock.</summary>
    Task<Result<OrderSummaryDto>> MarkRefusedAsync(Guid orderId, string? reason, string actorId, CancellationToken cancellationToken = default);

    /// <summary>Delivered → CashCollected. An amount different from the total needs a note.</summary>
    Task<Result<OrderSummaryDto>> MarkCashCollectedAsync(Guid orderId, decimal amount, string? note, string actorId, CancellationToken cancellationToken = default);
}

/// <param name="Status">Pending, Confirmed, Shipped, Delivered, CashCollected, Cancelled, Refused, Expired.</param>
/// <param name="Total">EUR, VAT included; the Cash on Delivery amount.</param>
/// <param name="VatAmount">VAT contained in <paramref name="Total"/> (18%).</param>
public sealed record OrderSummaryDto(
    Guid OrderId,
    string OrderNumber,
    string Status,
    Guid CustomerId,
    string CustomerName,
    string CustomerPhone,
    OrderAddressDto DeliveryAddress,
    decimal Subtotal,
    decimal DeliveryFee,
    decimal Total,
    decimal VatAmount,
    string Currency,
    string PaymentMethod,
    string? CourierName,
    string? TrackingNumber,
    DateTime PlacedAtUtc,
    IReadOnlyList<OrderLineSummaryDto> Lines);

public sealed record OrderAddressDto(string CountryCode, string City, string Street, string? Notes);

/// <param name="Batches">Batch codes shipped for this line (after the order shipped).</param>
public sealed record OrderLineSummaryDto(
    Guid OrderLineId,
    Guid VariantId,
    string Sku,
    string BrandName,
    string FragranceName,
    string VariantType,
    decimal? SizeValue,
    string? SizeUnit,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal,
    decimal VatAmount,
    IReadOnlyList<string> Batches);

public sealed record CustomerOrderStatsDto(int Orders, int Refused);
