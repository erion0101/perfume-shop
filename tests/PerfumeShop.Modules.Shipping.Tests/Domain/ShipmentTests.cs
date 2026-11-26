using PerfumeShop.Modules.Shipping.Domain;
using static PerfumeShop.Modules.Shipping.Tests.TestHelpers;

namespace PerfumeShop.Modules.Shipping.Tests.Domain;

public sealed class ShipmentTests
{
    private const string Invalid = "Shipping.Shipment.InvalidTransition";

    [Fact]
    public void New_shipment_is_created_with_the_cod_amount_and_a_timeline_entry()
    {
        var shipment = Created();

        Assert.Equal((ShipmentStatus.Created, 41.90m, "PK2026000001XK"), (shipment.Status, shipment.CodAmount, shipment.TrackingNumber));
        var entry = Assert.Single(shipment.History);
        Assert.Equal((null, ShipmentStatus.Created, UpdateSource.Manual), (entry.FromStatus, entry.ToStatus, entry.Source));
    }

    [Fact]
    public void Tracking_is_required_and_normalized() =>
        CreateShipment(tracking: " ").Fails("Shipping.Shipment.TrackingRequired");

    [Fact]
    public void Happy_path_records_actor_and_source()
    {
        var shipment = Created();
        shipment.MoveTo(ShipmentStatus.HandedOver, "staff-1", UpdateSource.Manual, "Manifest 26.11", Now).Ok();
        shipment.MoveTo(ShipmentStatus.InTransit, "staff-2", UpdateSource.CsvImport, null, Now).Ok();
        shipment.MoveTo(ShipmentStatus.Delivered, "staff-2", UpdateSource.CsvImport, "Pranoi vetë", Now).Ok();

        Assert.Equal(
            [(ShipmentStatus.Created, UpdateSource.Manual), (ShipmentStatus.HandedOver, UpdateSource.Manual), (ShipmentStatus.InTransit, UpdateSource.CsvImport), (ShipmentStatus.Delivered, UpdateSource.CsvImport)],
            shipment.History.Select(h => (h.ToStatus, h.Source)));
        Assert.Equal("Pranoi vetë", shipment.History[^1].Note);
        Assert.NotNull(shipment.DeliveredAtUtc);
    }

    [Theory]
    [InlineData((int)ShipmentStatus.Created, (int)ShipmentStatus.Delivered)]
    [InlineData((int)ShipmentStatus.Created, (int)ShipmentStatus.Refused)]
    [InlineData((int)ShipmentStatus.Created, (int)ShipmentStatus.ReturnedToWarehouse)]
    [InlineData((int)ShipmentStatus.HandedOver, (int)ShipmentStatus.ReturnedToWarehouse)]
    [InlineData((int)ShipmentStatus.Delivered, (int)ShipmentStatus.Refused)]
    [InlineData((int)ShipmentStatus.Refused, (int)ShipmentStatus.Delivered)]
    [InlineData((int)ShipmentStatus.Delivered, (int)ShipmentStatus.ReturnedToWarehouse)]
    public void Invalid_transitions_are_rejected(int from, int to) =>
        At((ShipmentStatus)from).MoveTo((ShipmentStatus)to, "staff", UpdateSource.Manual, null, Now).Fails(Invalid);

    [Fact]
    public void Delivery_may_skip_in_transit()
    {
        var shipment = At(ShipmentStatus.HandedOver);

        Assert.Equal(TransitionOutcome.Applied, shipment.MoveTo(ShipmentStatus.Delivered, "staff", UpdateSource.CsvImport, null, Now).Ok());
    }

    [Theory]
    [InlineData((int)ShipmentStatus.Delivered, (int)ShipmentStatus.Delivered)]   // repeated
    [InlineData((int)ShipmentStatus.Delivered, (int)ShipmentStatus.InTransit)]   // late
    [InlineData((int)ShipmentStatus.InTransit, (int)ShipmentStatus.HandedOver)]  // late
    [InlineData((int)ShipmentStatus.ReturnedToWarehouse, (int)ShipmentStatus.Refused)]
    public void Repeated_or_late_updates_change_nothing(int current, int update)
    {
        var shipment = At((ShipmentStatus)current);
        var historyBefore = shipment.History.Count;

        var outcome = shipment.MoveTo((ShipmentStatus)update, "staff", UpdateSource.CsvImport, null, Now).Ok();

        Assert.Equal(TransitionOutcome.AlreadyApplied, outcome);
        Assert.Equal((ShipmentStatus)current, shipment.Status);
        Assert.Equal(historyBefore, shipment.History.Count);
    }

    [Fact]
    public void Refused_parcel_comes_back_to_the_warehouse()
    {
        var shipment = At(ShipmentStatus.Refused);

        shipment.MoveTo(ShipmentStatus.ReturnedToWarehouse, "staff", UpdateSource.Manual, "Skanuar në magazinë", Now).Ok();

        Assert.NotNull(shipment.ReturnedAtUtc);
    }

    [Fact]
    public void Only_a_delivered_parcel_can_be_settled_and_only_once()
    {
        At(ShipmentStatus.InTransit).Settle(Guid.NewGuid(), 41.90m, 1.50m, Now).Fails(Invalid);

        var delivered = At(ShipmentStatus.Delivered);
        delivered.Settle(Guid.NewGuid(), 41.90m, 1.50m, Now).Ok();
        delivered.Settle(Guid.NewGuid(), 41.90m, 1.50m, Now).Fails("Shipping.Shipment.AlreadySettled");
        Assert.Equal((41.90m, 1.50m), (delivered.CollectedAmount!.Value, delivered.CodFee!.Value));
    }

    // ---------- couriers ----------

    [Fact]
    public void Courier_must_accept_flammable_goods_to_be_selectable()
    {
        CreateShipment(Courier(flammable: false)).Fails("Shipping.Courier.NotForFlammables");
        CreateShipment(Courier(enabled: false)).Fails("Shipping.Courier.Disabled");
        CreateShipment(country: "AL").Fails("Shipping.Courier.Country");
        Assert.True(Courier().IsSelectableFor("XK"));
    }

    [Theory]
    [InlineData(1.00, 1.00, 41.90, 1.42)]
    [InlineData(1.50, 0, 41.90, 1.50)]
    [InlineData(0, 2.50, 100, 2.50)]
    public void Expected_cod_fee_is_fixed_plus_percent(decimal fixedFee, decimal percent, decimal cod, decimal expected) =>
        Assert.Equal(expected, PerfumeShop.Modules.Shipping.Domain.Courier.Create(Details(codFixed: fixedFee, codPercent: percent)).Ok().ExpectedCodFee(cod));

    [Fact]
    public void Courier_details_are_validated()
    {
        PerfumeShop.Modules.Shipping.Domain.Courier.Create(Details(countries: ["DE"])).Fails("Shipping.Courier.Invalid");
        PerfumeShop.Modules.Shipping.Domain.Courier.Create(Details(codPercent: 15)).Fails("Shipping.Courier.Invalid");
        PerfumeShop.Modules.Shipping.Domain.Courier.Create(Details(remittanceDays: 0)).Fails("Shipping.Courier.Invalid");
    }
}
