using PerfumeShop.Modules.Orders.Domain;
using static PerfumeShop.Modules.Orders.Tests.TestHelpers;

namespace PerfumeShop.Modules.Orders.Tests.Domain;

public sealed class OrderTests
{
    private const string InvalidTransition = "Orders.Order.InvalidTransition";
    private static readonly IReadOnlyDictionary<Guid, IReadOnlyList<LineBatch>> NoBatches = new Dictionary<Guid, IReadOnlyList<LineBatch>>();

    // ---------- placing: snapshot, totals, VAT ----------

    [Fact]
    public void Placed_order_is_pending_with_totals_and_vat()
    {
        var order = Place([Line(39.90m, 2), Line(12.50m, 1)], deliveryFee: 2.00m).Ok();

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(92.30m, order.Subtotal);
        Assert.Equal(94.30m, order.Total);
        // 18% VAT is included: 79.80 → 12.17, 12.50 → 1.91, delivery 2.00 → 0.31
        Assert.Equal([12.17m, 1.91m], order.Lines.Select(l => l.VatAmount));
        Assert.Equal(14.39m, order.VatAmount);
        Assert.Equal(Now.AddHours(48), order.ExpiresAtUtc);
    }

    [Fact]
    public void Lines_keep_the_catalog_snapshot()
    {
        var line = Line(44.00m, 3);
        var order = Place([line]).Ok();

        var stored = order.Lines.Single();
        Assert.Equal((line.LineId, "LAT-KHA-100", "Lattafa", "Khamrah", 100m, "Ml", 44.00m, 3, 132.00m),
            (stored.Id, stored.Sku, stored.BrandName, stored.FragranceName, stored.SizeValue, stored.SizeUnit, stored.UnitPrice, stored.Quantity, stored.LineTotal));
        Assert.Equal("Lipjan", order.Address.City);
        Assert.Equal("+38344123456", order.Customer.Phone);
    }

    [Theory]
    [InlineData(0, "Orders.Line.QuantityInvalid")]
    [InlineData(11, "Orders.Line.QuantityInvalid")]
    public void Quantity_per_line_is_limited(int quantity, string code) =>
        Place([Line(quantity: quantity)]).Fails(code);

    [Fact]
    public void Same_variant_twice_is_rejected()
    {
        var variant = Guid.NewGuid();

        Place([Line(variantId: variant), Line(variantId: variant)]).Fails("Orders.Line.DuplicateVariant");
    }

    [Fact]
    public void Full_name_is_required() => Place(fullName: " ").Fails("Orders.Order.FullNameRequired");

    [Fact]
    public void Free_delivery_means_total_equals_subtotal() => Assert.Equal(39.90m, Place(deliveryFee: 0).Ok().Total);

    // ---------- the happy path ----------

    [Fact]
    public void Full_lifecycle_records_every_step_with_its_actor()
    {
        var order = Pending();
        order.Confirm(Staff, Now.AddHours(1)).Ok();
        order.Ship(Staff, "Posta e Kosovës", "PK123", NoBatches, Now.AddHours(2)).Ok();
        order.MarkDelivered(Staff, Now.AddDays(1)).Ok();
        order.MarkCashCollected(Staff, order.Total, null, Now.AddDays(2)).Ok();

        Assert.Equal(OrderStatus.CashCollected, order.Status);
        Assert.Equal(
            [
                ((OrderStatus?)null, OrderStatus.Pending, ActorKind.Customer),
                (OrderStatus.Pending, OrderStatus.Confirmed, ActorKind.Staff),
                (OrderStatus.Confirmed, OrderStatus.Shipped, ActorKind.Staff),
                (OrderStatus.Shipped, OrderStatus.Delivered, ActorKind.Staff),
                (OrderStatus.Delivered, OrderStatus.CashCollected, ActorKind.Staff),
            ],
            order.History.Select(h => (h.FromStatus, h.ToStatus, h.ActorKind)));
        Assert.Equal(5, order.DomainEvents.Count);
        Assert.Equal("staff-user-1", order.History[1].ActorId);
    }

    // ---------- invalid transitions ----------

    [Fact]
    public void Steps_cannot_be_skipped()
    {
        Pending().Ship(Staff, "Courier", "T1", NoBatches, Now).Fails(InvalidTransition);
        Pending().MarkDelivered(Staff, Now).Fails(InvalidTransition);
        Confirmed().MarkCashCollected(Staff, 41.90m, null, Now).Fails(InvalidTransition);
        Confirmed().MarkRefused(Staff, null, Now).Fails(InvalidTransition);
        Delivered().MarkRefused(Staff, null, Now).Fails(InvalidTransition);
    }

    [Fact]
    public void Steps_cannot_be_repeated()
    {
        Confirmed().Confirm(Staff, Now.AddHours(2)).Fails(InvalidTransition);
        Shipped().Ship(Staff, "Courier", "T1", NoBatches, Now).Fails(InvalidTransition);
        Delivered().MarkDelivered(Staff, Now).Fails(InvalidTransition);
    }

    [Fact]
    public void Closed_orders_cannot_change()
    {
        var cancelled = Pending();
        cancelled.Cancel(Staff, "Duplicate order", Now).Ok();

        cancelled.Confirm(Staff, Now).Fails(InvalidTransition);
        cancelled.Cancel(Staff, "again", Now).Fails(InvalidTransition);
    }

    [Fact]
    public void Only_staff_move_orders_forward()
    {
        var customer = OrderActor.Customer(Guid.NewGuid());

        Pending().Confirm(customer, Now).Fails("Orders.Order.OnlyStaff");
        Confirmed().Ship(customer, "Courier", "T1", NoBatches, Now).Fails("Orders.Order.OnlyStaff");
        Shipped().MarkDelivered(OrderActor.System, Now).Fails("Orders.Order.OnlyStaff");
    }

    // ---------- cancelling ----------

    [Fact]
    public void Customer_may_cancel_only_while_pending()
    {
        var customerId = Guid.NewGuid();
        var pending = Pending(customerId);
        pending.Cancel(OrderActor.Customer(customerId), null, Now).Ok();
        Assert.Equal(OrderStatus.Cancelled, pending.Status);

        var confirmed = Confirmed();
        confirmed.Cancel(OrderActor.Customer(confirmed.CustomerId), null, Now).Fails(InvalidTransition);
    }

    [Fact]
    public void Staff_may_cancel_until_shipped_with_a_reason()
    {
        Confirmed().Cancel(Staff, null, Now).Fails("Orders.Order.ReasonRequired");
        Confirmed().Cancel(Staff, "Klienti nuk përgjigjet", Now).Ok();
        Shipped().Cancel(Staff, "Too late", Now).Fails(InvalidTransition);
    }

    // ---------- shipping, refusal, cash ----------

    [Fact]
    public void Ship_requires_courier_and_tracking_and_stores_batches()
    {
        var order = Confirmed();
        order.Ship(Staff, "", "PK1", NoBatches, Now).Fails("Orders.Order.CourierRequired");
        order.Ship(Staff, "Posta", " ", NoBatches, Now).Fails("Orders.Order.CourierRequired");

        var lineId = order.Lines.Single().Id;
        order.Ship(Staff, "Posta e Kosovës", "PK123", new Dictionary<Guid, IReadOnlyList<LineBatch>> { [lineId] = [new("B2391", 1)] }, Now).Ok();

        Assert.Equal(("Posta e Kosovës", "PK123"), (order.CourierName, order.TrackingNumber));
        Assert.Equal("B2391", order.Lines.Single().Batches.Single().BatchCode);
    }

    [Fact]
    public void Only_a_shipped_order_can_be_refused()
    {
        var order = Shipped();

        order.MarkRefused(Staff, "Klienti refuzoi pakon", Now).Ok();

        Assert.Equal(OrderStatus.Refused, order.Status);
        Assert.NotNull(order.ClosedAtUtc);
    }

    [Fact]
    public void Collected_amount_different_from_total_needs_a_note()
    {
        var order = Delivered();

        order.MarkCashCollected(Staff, order.Total - 1, null, Now).Fails("Orders.Order.CollectionNoteRequired");
        order.MarkCashCollected(Staff, 10.555m, "x", Now).Fails("Orders.Order.AmountInvalid");
        order.MarkCashCollected(Staff, order.Total - 1, "Korrieri mbajti 1 € për kthim", Now).Ok();

        Assert.Equal(order.Total - 1, order.CashCollectedAmount);
    }

    // ---------- expiry ----------

    [Fact]
    public void Pending_order_expires_only_after_its_window()
    {
        var order = Pending();

        order.Expire(Now.AddHours(47)).Fails(InvalidTransition);
        order.Expire(Now.AddHours(48)).Ok();

        Assert.Equal(OrderStatus.Expired, order.Status);
        Assert.Equal(ActorKind.System, order.History.Last().ActorKind);
    }

    [Fact]
    public void Confirming_after_the_window_fails_and_confirmed_orders_never_expire()
    {
        Pending().Confirm(Staff, Now.AddHours(48)).Fails("Orders.Order.ConfirmationWindowPassed");
        Confirmed().Expire(Now.AddDays(30)).Fails(InvalidTransition);
    }

    // ---------- notes and settings ----------

    [Fact]
    public void Internal_notes_are_staff_only()
    {
        var order = Pending();

        order.AddNote(OrderActor.Customer(order.CustomerId), "hi", Now).Fails("Orders.Order.OnlyStaff");
        order.AddNote(Staff, "Klienti kërkoi dorëzim pas orës 17", Now).Ok();

        Assert.Single(order.Notes);
    }

    [Theory]
    [InlineData(39.99, 2.00)]
    [InlineData(40.00, 0)]
    [InlineData(120.00, 0)]
    public void Delivery_is_free_from_the_threshold(decimal subtotal, decimal expectedFee)
    {
        var rate = new DeliveryRate("XK", "Kosovo", true, 2.00m, 40.00m);

        Assert.Equal(expectedFee, rate.FeeFor(subtotal));
    }

    [Fact]
    public void Delivery_rate_and_settings_validate_their_values()
    {
        new DeliveryRate("XK", "Kosovo", true, 2m, 40m).Update(true, -1m, null, Now).Fails("Orders.Delivery.Invalid");
        OrderSettings.Defaults.Update(0, 2, 48, Now).Fails("Orders.Settings.Invalid");
        OrderSettings.Defaults.Update(3, 1, 24, Now).Ok();
    }

    [Fact]
    public void Vat_is_the_18_percent_part_of_a_gross_price()
    {
        Assert.Equal(18.00m, Vat.Of(118.00m));
        Assert.Equal(6.09m, Vat.Of(39.90m));
        Assert.Equal(0m, Vat.Of(0m));
    }
}
