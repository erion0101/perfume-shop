namespace PerfumeShop.Modules.Inventory.Domain;

internal enum StockMovementType
{
    Receipt = 1,
    Reservation = 2,
    Release = 3,
    Commit = 4,
    Adjustment = 5,
    Return = 6,
}

/// <summary>
/// One line of the append-only stock ledger. Rows are only ever inserted (a database trigger
/// rejects UPDATE and DELETE), so the history explains every on-hand and reserved number.
/// </summary>
internal sealed class StockMovement
{
    private StockMovement() { }

    public Guid Id { get; private init; }

    public Guid VariantId { get; private init; }

    public StockMovementType Type { get; private init; }

    /// <summary>Always positive; the deltas carry the direction.</summary>
    public int Quantity { get; private init; }

    public int OnHandDelta { get; private init; }

    public int ReservedDelta { get; private init; }

    public int OnHandAfter { get; private init; }

    public int ReservedAfter { get; private init; }

    public Guid? OrderId { get; private init; }

    public Guid? OrderLineId { get; private init; }

    public Guid? BatchId { get; private init; }

    /// <summary>Batch code(s) involved, e.g. "B2391" or "B2391×2, C1180×1" for a commit.</summary>
    public string? BatchCodes { get; private init; }

    public string? Reason { get; private init; }

    /// <summary>Staff user id, or the module that caused it (e.g. "orders").</summary>
    public string? Actor { get; private init; }

    public DateTime OccurredAtUtc { get; private init; }

    internal static StockMovement Record(
        StockItem item,
        StockMovementType type,
        int quantity,
        int onHandDelta,
        int reservedDelta,
        DateTime occurredAtUtc,
        string? actor,
        Guid? orderId = null,
        Guid? orderLineId = null,
        Guid? batchId = null,
        string? batchCodes = null,
        string? reason = null) => new()
    {
        Id = Guid.CreateVersion7(),
        VariantId = item.Id,
        Type = type,
        Quantity = quantity,
        OnHandDelta = onHandDelta,
        ReservedDelta = reservedDelta,
        OnHandAfter = item.OnHand,
        ReservedAfter = item.Reserved,
        OrderId = orderId,
        OrderLineId = orderLineId,
        BatchId = batchId,
        BatchCodes = batchCodes,
        Reason = reason,
        Actor = actor,
        OccurredAtUtc = occurredAtUtc,
    };
}
