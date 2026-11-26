using PerfumeShop.Modules.Inventory.Contracts;
using PerfumeShop.Modules.Inventory.Domain;
using static PerfumeShop.Modules.Inventory.Tests.TestHelpers;

namespace PerfumeShop.Modules.Inventory.Tests.Domain;

public sealed class StockItemTests
{
    private static StockReservation Reserve(StockItem item, int quantity, DateTime? expires = null) =>
        item.Reserve(Guid.NewGuid(), Guid.NewGuid(), quantity, expires, "orders", Now).Ok();

    // ---------- receiving ----------

    [Fact]
    public void Receiving_adds_a_batch_and_on_hand_stock()
    {
        var item = Item();

        var batch = item.ReceiveBatch("B2391", 10);

        Assert.Equal(10, item.OnHand);
        Assert.Equal(10, item.Available);
        Assert.Equal(10, batch.QuantityRemaining);
        Assert.Equal("INV-2026-001", batch.SupplierInvoiceNumber);
        var movement = Assert.Single(item.PendingMovements);
        Assert.Equal(StockMovementType.Receipt, movement.Type);
        Assert.Equal((10, 10, 0), (movement.OnHandDelta, movement.OnHandAfter, movement.ReservedAfter));
    }

    [Theory]
    [InlineData("", "INV-1", "Lattafa", "Inventory.Batch.CodeRequired")]
    [InlineData("B1", " ", "Lattafa", "Inventory.Batch.InvoiceNumberRequired")]
    [InlineData("B1", "INV-1", "", "Inventory.Batch.SupplierRequired")]
    public void Batch_code_invoice_and_supplier_are_required(string code, string invoice, string supplier, string error)
    {
        var receipt = new GoodsReceipt(Guid.NewGuid(), supplier, invoice, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 3));

        Item().Receive(receipt, code, 5, Eur(10), "staff", Now).Fails(error);
    }

    [Fact]
    public void Future_dates_are_rejected()
    {
        var receipt = new GoodsReceipt(Guid.NewGuid(), "Lattafa", "INV-1", new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 24));

        Item().Receive(receipt, "B1", 5, Eur(10), "staff", Now).Fails("Inventory.Batch.DatesInvalid");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Received_quantity_must_be_positive(int quantity) =>
        Item().Receive(Receipt(), "B1", quantity, Eur(10), "staff", Now).Fails("Inventory.Quantity.Invalid");

    // ---------- reserving: available never goes negative ----------

    [Fact]
    public void Reserving_lowers_available_but_not_on_hand()
    {
        var item = Item();
        item.ReceiveBatch("B1", 5);

        Reserve(item, 2);

        Assert.Equal((5, 2, 3), (item.OnHand, item.Reserved, item.Available));
    }

    [Fact]
    public void Cannot_reserve_more_than_available()
    {
        var item = Item();
        item.ReceiveBatch("B1", 2);
        Reserve(item, 2);

        var result = item.Reserve(Guid.NewGuid(), Guid.NewGuid(), 1, null, "orders", Now);

        result.Fails(InventoryErrorCodes.InsufficientStock);
        Assert.Equal(0, item.Available);
    }

    [Fact]
    public void Releasing_a_reservation_frees_the_units_and_is_idempotent()
    {
        var item = Item();
        item.ReceiveBatch("B1", 5);
        var reservation = Reserve(item, 3);

        item.Release(reservation, "cancelled", "orders", Now).Ok();
        item.Release(reservation, "cancelled", "orders", Now).Ok();

        Assert.Equal(ReservationStatus.Released, reservation.Status);
        Assert.Equal((5, 0), (item.OnHand, item.Reserved));
        Assert.Single(item.PendingMovements, m => m.Type == StockMovementType.Release);
    }

    // ---------- committing: FIFO ----------

    [Fact]
    public void Commit_takes_the_oldest_batch_first_and_records_batch_codes()
    {
        var item = Item();
        var newer = item.ReceiveBatch("NEW-02", 5, new DateOnly(2026, 11, 20));
        var older = item.ReceiveBatch("OLD-01", 2, new DateOnly(2026, 11, 2));
        var reservation = Reserve(item, 3);

        item.Commit(reservation, "orders", Now).Ok();

        Assert.Equal(ReservationStatus.Committed, reservation.Status);
        Assert.Equal([("OLD-01", 2), ("NEW-02", 1)], reservation.Allocations.Select(a => (a.BatchCode, a.Quantity)));
        Assert.Equal(0, older.QuantityRemaining);
        Assert.Equal(4, newer.QuantityRemaining);
        Assert.Equal((4, 0, 4), (item.OnHand, item.Reserved, item.Available));
        var movement = item.PendingMovements.Last();
        Assert.Equal(StockMovementType.Commit, movement.Type);
        Assert.Equal("OLD-01×2, NEW-02×1", movement.BatchCodes);
    }

    [Fact]
    public void Same_day_batches_are_consumed_in_receiving_order()
    {
        var item = Item();
        var day = new DateOnly(2026, 11, 3);
        item.ReceiveBatch("FIRST", 1, day);
        item.ReceiveBatch("SECOND", 1, day);
        var reservation = Reserve(item, 1);

        item.Commit(reservation, "orders", Now).Ok();

        Assert.Equal("FIRST", reservation.Allocations.Single().BatchCode);
    }

    [Fact]
    public void Commit_is_idempotent_and_released_stock_cannot_ship()
    {
        var item = Item();
        item.ReceiveBatch("B1", 5);
        var shipped = Reserve(item, 2);
        var cancelled = Reserve(item, 1);
        item.Release(cancelled, "cancelled", "orders", Now).Ok();

        item.Commit(shipped, "orders", Now).Ok();
        item.Commit(shipped, "orders", Now).Ok();

        Assert.Equal(3, item.OnHand);
        item.Commit(cancelled, "orders", Now).Fails(InventoryErrorCodes.ReservationNotActive);
    }

    [Fact]
    public void Returning_a_shipped_line_puts_the_units_back_into_the_same_batches()
    {
        var item = Item();
        var older = item.ReceiveBatch("OLD-01", 1, new DateOnly(2026, 11, 2));
        var newer = item.ReceiveBatch("NEW-02", 5, new DateOnly(2026, 11, 20));
        var reservation = Reserve(item, 2);
        item.Commit(reservation, "orders", Now).Ok();

        item.Release(reservation, "Parcel refused", "orders", Now).Ok();
        item.Release(reservation, "Parcel refused", "orders", Now).Ok();

        Assert.Equal(ReservationStatus.Returned, reservation.Status);
        Assert.Equal((1, 5), (older.QuantityRemaining, newer.QuantityRemaining));
        Assert.Equal((6, 0), (item.OnHand, item.Reserved));
        var movement = Assert.Single(item.PendingMovements, m => m.Type == StockMovementType.Return);
        Assert.Equal("Parcel refused", movement.Reason);
    }

    // ---------- adjustments ----------

    [Fact]
    public void Adjustment_needs_a_reason()
    {
        var item = Item();
        item.ReceiveBatch("B1", 5);

        item.Adjust(-1, "  ", null, "staff", Now).Fails("Inventory.Adjustment.ReasonRequired");
        item.Adjust(0, "count", null, "staff", Now).Fails("Inventory.Adjustment.Zero");
    }

    [Fact]
    public void Removing_stock_never_touches_reserved_units()
    {
        var item = Item();
        item.ReceiveBatch("B1", 5);
        Reserve(item, 4);

        item.Adjust(-2, "Broken bottle", null, "staff", Now).Fails(InventoryErrorCodes.InsufficientStock);
        item.Adjust(-1, "Broken bottle", null, "staff", Now).Ok();

        Assert.Equal((4, 4, 0), (item.OnHand, item.Reserved, item.Available));
    }

    [Fact]
    public void Removing_stock_without_a_batch_takes_the_oldest_batch()
    {
        var item = Item();
        var older = item.ReceiveBatch("OLD", 2, new DateOnly(2026, 11, 1));
        var newer = item.ReceiveBatch("NEW", 2, new DateOnly(2026, 11, 10));

        item.Adjust(-3, "Stocktake", null, "staff", Now).Ok();

        Assert.Equal((0, 1), (older.QuantityRemaining, newer.QuantityRemaining));
        Assert.Equal("OLD×2, NEW×1", item.PendingMovements.Last().BatchCodes);
    }

    [Fact]
    public void Adding_stock_must_name_a_batch_and_cannot_exceed_what_the_invoice_says()
    {
        var item = Item();
        var batch = item.ReceiveBatch("B1", 3);
        item.Adjust(-2, "Stocktake", batch.Id, "staff", Now).Ok();

        item.Adjust(+1, "Found in storage", null, "staff", Now).Fails("Inventory.Adjustment.BatchRequired");
        item.Adjust(+3, "Found in storage", batch.Id, "staff", Now).Fails("Inventory.Adjustment.BatchOverfilled");
        item.Adjust(+2, "Found in storage", batch.Id, "staff", Now).Ok();

        Assert.Equal(3, item.OnHand);
    }

    [Fact]
    public void Every_change_is_in_the_ledger_with_running_totals()
    {
        var item = Item();
        item.ReceiveBatch("B1", 5);
        var reservation = Reserve(item, 2);
        item.Commit(reservation, "orders", Now).Ok();
        item.Adjust(-1, "Broken", null, "staff", Now).Ok();

        Assert.Equal(
            [
                (StockMovementType.Receipt, 5, 0),
                (StockMovementType.Reservation, 5, 2),
                (StockMovementType.Commit, 3, 0),
                (StockMovementType.Adjustment, 2, 0),
            ],
            item.PendingMovements.Select(m => (m.Type, m.OnHandAfter, m.ReservedAfter)));
        Assert.Equal(item.OnHand, item.Batches.Sum(b => b.QuantityRemaining));
    }

    [Fact]
    public void Low_stock_uses_the_variant_threshold()
    {
        var item = Item(threshold: 2);
        item.ReceiveBatch("B1", 3);
        Assert.False(item.IsLowStock);

        Reserve(item, 1);

        Assert.True(item.IsLowStock);
        item.SetLowStockThreshold(-1).Fails("Inventory.Threshold.Invalid");
    }

    [Fact]
    public void Reservation_expires_unless_confirmed()
    {
        var item = Item();
        item.ReceiveBatch("B1", 5);
        var reservation = Reserve(item, 1, expires: Now.AddMinutes(30));

        Assert.False(reservation.IsExpired(Now.AddMinutes(29)));
        Assert.True(reservation.IsExpired(Now.AddMinutes(30)));
        reservation.Confirm();
        Assert.False(reservation.IsExpired(Now.AddDays(30)));
    }
}
