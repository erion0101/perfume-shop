namespace PerfumeShop.Modules.Inventory.Domain;

internal enum ReservationStatus
{
    /// <summary>Held for an order line that has not shipped.</summary>
    Reserved = 1,

    /// <summary>Shipped: units left the warehouse; batches recorded in <see cref="StockReservation.Allocations"/>.</summary>
    Committed = 2,

    /// <summary>Cancelled before shipping, or expired.</summary>
    Released = 3,

    /// <summary>Shipped, then refused/returned: units went back into the same batches.</summary>
    Returned = 4,
}

/// <summary>Stock held for one order line. Created and changed only through <see cref="StockItem"/>.</summary>
internal sealed class StockReservation
{
    private readonly List<StockAllocation> _allocations = [];

    private StockReservation() { }

    internal StockReservation(Guid variantId, Guid orderId, Guid orderLineId, int quantity, DateTime? expiresAtUtc, DateTime createdAtUtc)
    {
        VariantId = variantId;
        OrderId = orderId;
        OrderLineId = orderLineId;
        Quantity = quantity;
        ExpiresAtUtc = expiresAtUtc;
        CreatedAtUtc = createdAtUtc;
        Status = ReservationStatus.Reserved;
    }

    public Guid Id { get; private init; } = Guid.CreateVersion7();

    public Guid VariantId { get; private init; }

    public Guid OrderId { get; private init; }

    /// <summary>Unique: the idempotency key for every operation.</summary>
    public Guid OrderLineId { get; private init; }

    public int Quantity { get; private init; }

    public ReservationStatus Status { get; private set; }

    /// <summary>Unconfirmed orders hold stock only until this time; null once the order is confirmed.</summary>
    public DateTime? ExpiresAtUtc { get; private set; }

    public DateTime CreatedAtUtc { get; private init; }

    public DateTime? CommittedAtUtc { get; private set; }

    public DateTime? ReleasedAtUtc { get; private set; }

    public DateTime? ReturnedAtUtc { get; private set; }

    public IReadOnlyList<StockAllocation> Allocations => _allocations.AsReadOnly();

    public bool IsExpired(DateTime nowUtc) => Status == ReservationStatus.Reserved && ExpiresAtUtc <= nowUtc;

    internal void Confirm() => ExpiresAtUtc = null;

    internal void MarkReleased(DateTime nowUtc)
    {
        Status = ReservationStatus.Released;
        ReleasedAtUtc = nowUtc;
    }

    internal void MarkCommitted(IEnumerable<StockAllocation> allocations, DateTime nowUtc)
    {
        _allocations.AddRange(allocations);
        Status = ReservationStatus.Committed;
        CommittedAtUtc = nowUtc;
        ExpiresAtUtc = null;
    }

    internal void MarkReturned(DateTime nowUtc)
    {
        Status = ReservationStatus.Returned;
        ReturnedAtUtc = nowUtc;
    }
}

/// <summary>How many units of which batch went out for a committed reservation.</summary>
internal sealed class StockAllocation
{
    private StockAllocation() { }

    internal StockAllocation(Guid reservationId, StockBatch batch, int quantity)
    {
        ReservationId = reservationId;
        BatchId = batch.Id;
        BatchCode = batch.BatchCode;
        Quantity = quantity;
    }

    public Guid Id { get; private init; } = Guid.CreateVersion7();

    public Guid ReservationId { get; private init; }

    public Guid BatchId { get; private init; }

    public string BatchCode { get; private init; } = null!;

    public int Quantity { get; private init; }
}
