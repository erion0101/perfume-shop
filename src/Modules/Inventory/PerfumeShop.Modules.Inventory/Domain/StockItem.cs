using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Inventory.Domain;

/// <summary>
/// Stock of one Catalog variant (Id = the variant id; no foreign key across modules).
/// Invariants: on hand = sum of batch remainders; 0 ≤ reserved ≤ on hand, so available is never negative.
/// Every change records a <see cref="StockMovement"/>.
/// </summary>
internal sealed class StockItem : AggregateRoot<Guid>, IAuditable
{
    public const int MaxQuantity = 100_000;
    public const int MaxThreshold = 1_000;
    public const int ReasonMaxLength = 500;

    private readonly List<StockBatch> _batches = [];
    private readonly List<StockMovement> _pendingMovements = [];

    private StockItem() { }

    private StockItem(Guid variantId, int lowStockThreshold) : base(variantId) => LowStockThreshold = lowStockThreshold;

    public int OnHand { get; private set; }

    public int Reserved { get; private set; }

    public int Available => OnHand - Reserved;

    /// <summary>"Low stock" when available ≤ this. Per variant; new items get Inventory:DefaultLowStockThreshold.</summary>
    public int LowStockThreshold { get; private set; }

    public bool IsLowStock => Available <= LowStockThreshold;

    public IReadOnlyList<StockBatch> Batches => _batches.AsReadOnly();

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Movements created since loading; the DbContext inserts them on save.</summary>
    internal IReadOnlyList<StockMovement> PendingMovements => _pendingMovements;

    internal void ClearPendingMovements() => _pendingMovements.Clear();

    public static Result<StockItem> Create(Guid variantId, int lowStockThreshold)
    {
        if (variantId == Guid.Empty)
        {
            return Error.Validation("Inventory.Item.VariantRequired", "Variant id is required.");
        }

        return lowStockThreshold is < 0 or > MaxThreshold ? InventoryErrors.ThresholdInvalid : new StockItem(variantId, lowStockThreshold);
    }

    public Result SetLowStockThreshold(int threshold)
    {
        if (threshold is < 0 or > MaxThreshold)
        {
            return InventoryErrors.ThresholdInvalid;
        }

        LowStockThreshold = threshold;
        return Result.Success();
    }

    // ---------- goods in ----------

    public Result<StockBatch> Receive(GoodsReceipt receipt, string? batchCode, int quantity, Money unitCost, string? actor, DateTime nowUtc)
    {
        var sequence = _batches.Count == 0 ? 1 : _batches.Max(b => b.Sequence) + 1;
        var batch = StockBatch.Create(Id, sequence, receipt, batchCode, quantity, unitCost, nowUtc);
        if (batch.IsFailure)
        {
            return batch;
        }

        if (OnHand + quantity > MaxQuantity)
        {
            return InventoryErrors.QuantityInvalid;
        }

        _batches.Add(batch.Value);
        OnHand += quantity;
        Record(StockMovementType.Receipt, quantity, +quantity, 0, nowUtc, actor,
            batchId: batch.Value.Id, batchCodes: batch.Value.BatchCode, reason: $"Invoice {receipt.SupplierInvoiceNumber}");
        return batch;
    }

    /// <summary>
    /// Stocktake corrections. Removing stock (breakage, loss) takes from the named batch or FIFO and can
    /// never touch reserved units. Adding stock must name the batch it belongs to.
    /// </summary>
    public Result Adjust(int delta, string? reason, Guid? batchId, string? actor, DateTime nowUtc)
    {
        reason = reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > ReasonMaxLength)
        {
            return InventoryErrors.ReasonRequired;
        }

        if (delta == 0)
        {
            return InventoryErrors.AdjustmentZero;
        }

        if (Math.Abs(delta) > MaxQuantity)
        {
            return InventoryErrors.QuantityInvalid;
        }

        StockBatch? named = null;
        if (batchId is { } id)
        {
            named = _batches.Find(b => b.Id == id);
            if (named is null)
            {
                return InventoryErrors.BatchNotFound;
            }
        }

        if (delta > 0)
        {
            if (named is null)
            {
                return InventoryErrors.PositiveAdjustmentNeedsBatch;
            }

            if (named.QuantityRemaining + delta > named.QuantityReceived)
            {
                return InventoryErrors.BatchOverfilled;
            }

            named.PutBack(delta);
            OnHand += delta;
            Record(StockMovementType.Adjustment, delta, +delta, 0, nowUtc, actor, batchId: named.Id, batchCodes: named.BatchCode, reason: reason);
            return Result.Success();
        }

        var remove = -delta;
        if (remove > Available)
        {
            return InventoryErrors.Insufficient(Available);
        }

        List<(StockBatch Batch, int Quantity)> taken;
        if (named is not null)
        {
            if (named.QuantityRemaining < remove)
            {
                return InventoryErrors.BatchInsufficient;
            }

            taken = [(named, remove)];
        }
        else
        {
            var fifo = TakeFifo(remove);
            if (fifo.IsFailure)
            {
                return fifo.Error;
            }

            taken = fifo.Value;
        }

        taken.ForEach(t => t.Batch.Take(t.Quantity));
        OnHand -= remove;
        Record(StockMovementType.Adjustment, remove, -remove, 0, nowUtc, actor,
            batchId: taken.Count == 1 ? taken[0].Batch.Id : null, batchCodes: Describe(taken), reason: reason);
        return Result.Success();
    }

    // ---------- orders ----------

    public Result<StockReservation> Reserve(Guid orderId, Guid orderLineId, int quantity, DateTime? expiresAtUtc, string? actor, DateTime nowUtc)
    {
        if (quantity is < 1 or > MaxQuantity)
        {
            return InventoryErrors.QuantityInvalid;
        }

        if (quantity > Available)
        {
            return InventoryErrors.Insufficient(Available);
        }

        var reservation = new StockReservation(Id, orderId, orderLineId, quantity, expiresAtUtc, nowUtc);
        Reserved += quantity;
        Record(StockMovementType.Reservation, quantity, 0, +quantity, nowUtc, actor, orderId, orderLineId);
        return reservation;
    }

    /// <summary>
    /// Cancelled/refused/expired. A reserved line gives its units back to "available"; a line that
    /// already shipped is a return: the units go back into the batches they left. Idempotent.
    /// </summary>
    public Result Release(StockReservation reservation, string? reason, string? actor, DateTime nowUtc)
    {
        if (reservation.VariantId != Id)
        {
            return InventoryErrors.WrongVariant;
        }

        switch (reservation.Status)
        {
            case ReservationStatus.Reserved:
                Reserved -= reservation.Quantity;
                reservation.MarkReleased(nowUtc);
                Record(StockMovementType.Release, reservation.Quantity, 0, -reservation.Quantity, nowUtc, actor,
                    reservation.OrderId, reservation.OrderLineId, reason: reason);
                return Result.Success();

            case ReservationStatus.Committed:
                foreach (var allocation in reservation.Allocations)
                {
                    var batch = _batches.Find(b => b.Id == allocation.BatchId);
                    if (batch is null)
                    {
                        return InventoryErrors.BatchNotFound;
                    }

                    batch.PutBack(allocation.Quantity);
                }

                OnHand += reservation.Quantity;
                reservation.MarkReturned(nowUtc);
                Record(StockMovementType.Return, reservation.Quantity, +reservation.Quantity, 0, nowUtc, actor,
                    reservation.OrderId, reservation.OrderLineId,
                    batchCodes: Describe(reservation.Allocations.Select(a => (a.BatchCode, a.Quantity))), reason: reason);
                return Result.Success();

            default:
                return Result.Success(); // already released or returned
        }
    }

    /// <summary>Shipped: takes the units out of the oldest batches first and records which ones. Idempotent.</summary>
    public Result Commit(StockReservation reservation, string? actor, DateTime nowUtc)
    {
        if (reservation.VariantId != Id)
        {
            return InventoryErrors.WrongVariant;
        }

        switch (reservation.Status)
        {
            case ReservationStatus.Committed:
                return Result.Success();
            case ReservationStatus.Released or ReservationStatus.Returned:
                return InventoryErrors.ReservationReleased;
        }

        var taken = TakeFifo(reservation.Quantity);
        if (taken.IsFailure)
        {
            return taken.Error;
        }

        taken.Value.ForEach(t => t.Batch.Take(t.Quantity));
        OnHand -= reservation.Quantity;
        Reserved -= reservation.Quantity;
        reservation.MarkCommitted(taken.Value.Select(t => new StockAllocation(reservation.Id, t.Batch, t.Quantity)), nowUtc);
        Record(StockMovementType.Commit, reservation.Quantity, -reservation.Quantity, -reservation.Quantity, nowUtc, actor,
            reservation.OrderId, reservation.OrderLineId, batchCodes: Describe(taken.Value));
        return Result.Success();
    }

    private Result<List<(StockBatch Batch, int Quantity)>> TakeFifo(int quantity)
    {
        List<(StockBatch, int)> taken = [];
        var left = quantity;
        foreach (var batch in _batches.Where(b => b.QuantityRemaining > 0).OrderBy(b => b.ReceivedOn).ThenBy(b => b.Sequence))
        {
            var take = Math.Min(left, batch.QuantityRemaining);
            taken.Add((batch, take));
            left -= take;
            if (left == 0)
            {
                return taken;
            }
        }

        return InventoryErrors.Inconsistent;
    }

    private void Record(StockMovementType type, int quantity, int onHandDelta, int reservedDelta, DateTime nowUtc, string? actor,
        Guid? orderId = null, Guid? orderLineId = null, Guid? batchId = null, string? batchCodes = null, string? reason = null) =>
        _pendingMovements.Add(StockMovement.Record(this, type, quantity, onHandDelta, reservedDelta, nowUtc, actor, orderId, orderLineId, batchId, batchCodes, reason));

    private static string Describe(IEnumerable<(StockBatch Batch, int Quantity)> taken) =>
        Describe(taken.Select(t => (t.Batch.BatchCode, t.Quantity)));

    private static string Describe(IEnumerable<(string Code, int Quantity)> parts) =>
        string.Join(", ", parts.Select(p => $"{p.Code}×{p.Quantity}"));
}
