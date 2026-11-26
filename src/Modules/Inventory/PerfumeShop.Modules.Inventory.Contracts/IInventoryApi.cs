using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Inventory.Contracts;

/// <summary>
/// Stock operations for other modules. Every write is idempotent per order line id, so callers
/// (Orders, retried jobs) can safely repeat a call.
/// </summary>
public interface IInventoryApi
{
    /// <summary>Availability of one variant; a variant never received has 0 available.</summary>
    Task<StockAvailabilityDto> GetAvailabilityAsync(Guid variantId, CancellationToken cancellationToken = default);

    /// <summary>Availability of many variants at once (one query), keyed by variant id.</summary>
    Task<IReadOnlyDictionary<Guid, StockAvailabilityDto>> GetAvailabilityAsync(IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken = default);

    /// <summary>Variants with at least one unit available (admin "in stock" filter).</summary>
    Task<IReadOnlyList<Guid>> GetVariantIdsInStockAsync(CancellationToken cancellationToken = default);

    /// <summary>Every variant whose available stock is at or below its low-stock threshold (lowest first).</summary>
    Task<IReadOnlyList<LowStockItemDto>> GetLowStockAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reserves all lines or none (fails with <see cref="InventoryErrorCodes.InsufficientStock"/>).
    /// A line already reserved with the same variant and quantity is skipped.
    /// </summary>
    /// <param name="expiresAtUtc">Unconfirmed orders: when the hold lapses. Null = until released or committed.</param>
    Task<Result> ReserveAsync(Guid orderId, IReadOnlyList<StockReservationLine> lines, DateTime? expiresAtUtc, CancellationToken cancellationToken = default);

    /// <summary>Order confirmed: the reservations no longer expire.</summary>
    Task<Result> ConfirmAsync(IReadOnlyCollection<Guid> orderLineIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Shipped: takes units out of stock, oldest batches first, and returns the batch codes per line.
    /// Already committed lines return their recorded batches.
    /// </summary>
    Task<Result<IReadOnlyList<CommittedLineDto>>> CommitAsync(IReadOnlyCollection<Guid> orderLineIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancelled before shipping: frees the reservation. Refused/returned after shipping: puts the
    /// units back into the batches they left. Unknown or already released lines are ignored.
    /// </summary>
    Task<Result> ReleaseAsync(IReadOnlyCollection<Guid> orderLineIds, string reason, CancellationToken cancellationToken = default);
}

public sealed record LowStockItemDto(Guid VariantId, int Available, int Threshold);

public sealed record StockReservationLine(Guid OrderLineId, Guid VariantId, int Quantity);

/// <param name="InStock">Available &gt; 0.</param>
/// <param name="IsLowStock">Available at or below the variant's low-stock threshold.</param>
public sealed record StockAvailabilityDto(Guid VariantId, int Available, bool InStock, bool IsLowStock);

public sealed record CommittedLineDto(Guid OrderLineId, Guid VariantId, int Quantity, IReadOnlyList<BatchAllocationDto> Batches);

public sealed record BatchAllocationDto(string BatchCode, int Quantity);

/// <summary>Error codes callers may want to react to.</summary>
public static class InventoryErrorCodes
{
    public const string InsufficientStock = "Inventory.Stock.Insufficient";
    public const string ReservationMismatch = "Inventory.Reservation.Mismatch";
    public const string ReservationNotActive = "Inventory.Reservation.NotActive";
    public const string ReservationNotFound = "Inventory.Reservation.NotFound";
}
