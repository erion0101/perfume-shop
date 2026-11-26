using System.Net;
using Microsoft.Extensions.DependencyInjection;
using PerfumeShop.Modules.Shipping.Features.Reports;
using PerfumeShop.Modules.Shipping.Features.Shared;
using PerfumeShop.Shared.Infrastructure.Auth;
using PerfumeShop.Testing;

namespace PerfumeShop.Modules.Shipping.Tests.Integration;

public sealed class ShipmentRulesTests(ShippingApiFactory factory)
{
    private const string Api = "/api/admin/shipping";
    private readonly HttpClient _staff = factory.CreateAdminClient();

    private Task<HttpResponseMessage> Create(Guid orderId, Guid courierId, string tracking) =>
        _staff.PostJson($"{Api}/shipments", new { orderId, courierId, trackingNumber = tracking });

    [Fact]
    public async Task Only_couriers_that_accept_flammable_goods_and_serve_the_country_can_be_used()
    {
        var order = await factory.OrderAsync();
        var noFlammables = await factory.CourierAsync(flammable: false);
        var albaniaOnly = await factory.CourierAsync(countries: ["AL"]);

        var first = await Create(order.Id, noFlammables.Id, ShippingApiFactory.Tracking());
        var second = await Create(order.Id, albaniaOnly.Id, ShippingApiFactory.Tracking());

        Assert.Equal("Shipping.Courier.NotForFlammables", await first.ErrorCode());
        Assert.Equal("Shipping.Courier.Country", await second.ErrorCode());
        Assert.False(noFlammables.IsSelectable);
        Assert.Equal("Confirmed", (await factory.OrderStateAsync(order.Id)).Status); // nothing shipped
    }

    [Fact]
    public async Task Only_confirmed_orders_can_be_shipped()
    {
        var courier = await factory.CourierAsync();
        var pending = await factory.OrderAsync(confirm: false);

        var response = await Create(pending.Id, courier.Id, ShippingApiFactory.Tracking());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Shipping.Order.NotConfirmed", await response.ErrorCode());
    }

    [Fact]
    public async Task Creating_the_same_shipment_twice_returns_it_and_conflicting_data_is_rejected()
    {
        var courier = await factory.CourierAsync();
        var order = await factory.OrderAsync();
        var other = await factory.OrderAsync();
        var tracking = ShippingApiFactory.Tracking();

        var first = await Create(order.Id, courier.Id, tracking);
        var repeat = await Create(order.Id, courier.Id, tracking.ToLowerInvariant());
        var otherTracking = await Create(order.Id, courier.Id, ShippingApiFactory.Tracking());
        var reusedTracking = await Create(other.Id, courier.Id, tracking);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        Assert.Equal((await first.Read<ShipmentDto>()).Id, (await repeat.Read<ShipmentDto>()).Id);
        Assert.Equal("Shipping.Order.HasShipment", await otherTracking.ErrorCode());
        Assert.Equal("Shipping.Shipment.TrackingTaken", await reusedTracking.ErrorCode());
    }

    /// <summary>If saving the shipment failed after Orders was already marked Shipped, a retry repairs it.</summary>
    [Fact]
    public async Task A_retry_after_a_failed_save_adopts_the_already_shipped_order()
    {
        var courier = await factory.CourierAsync();
        var order = await factory.OrderAsync();
        var tracking = ShippingApiFactory.Tracking();
        await using (var scope = factory.CreateScope())
        {
            var orders = scope.ServiceProvider.GetRequiredService<PerfumeShop.Modules.Orders.Contracts.IOrdersApi>();
            Assert.True((await orders.MarkShippedAsync(order.Id, courier.Name, tracking, "staff", TestContext.Current.CancellationToken)).IsSuccess);
        }

        var adopted = await Create(order.Id, courier.Id, tracking);

        Assert.Equal(HttpStatusCode.Created, adopted.StatusCode);
    }

    [Fact]
    public async Task Cash_for_a_statement_mismatch_is_recorded_manually_with_a_note()
    {
        var courier = await factory.CourierAsync();
        var order = await factory.OrderAsync();
        var shipment = await (await Create(order.Id, courier.Id, ShippingApiFactory.Tracking())).Read<ShipmentDto>();
        await _staff.PostJson($"{Api}/shipments/{shipment.Id}/handed-over");
        await _staff.PostJson($"{Api}/shipments/{shipment.Id}/delivered");

        var withoutNote = await _staff.PostJson($"{Api}/shipments/{shipment.Id}/cash-collected", new { amount = 40.00m });
        var withNote = await _staff.PostJson($"{Api}/shipments/{shipment.Id}/cash-collected", new { amount = 40.00m, note = "Korrieri ktheu 1.90 € më pak" });
        var statement = await _staff.PostCsv($"{Api}/couriers/{courier.Id}/settlements", $"tracking_number;collected_amount;fee\n{shipment.TrackingNumber};41.90;1.42\n");

        Assert.Equal("Shipping.Shipment.ManualCollectionInvalid", await withoutNote.ErrorCode());
        Assert.Equal(HttpStatusCode.OK, withNote.StatusCode);
        Assert.Equal("CashCollected", (await factory.OrderStateAsync(order.Id)).Status);
        Assert.Equal("AlreadySettled", (await statement.Read<PerfumeShop.Modules.Shipping.Features.Settlements.SettlementDto>()).Lines.Single().Status);
    }

    [Fact]
    public async Task Manual_steps_follow_the_status_machine()
    {
        var courier = await factory.CourierAsync();
        var shipment = await (await Create((await factory.OrderAsync()).Id, courier.Id, ShippingApiFactory.Tracking())).Read<ShipmentDto>();

        var tooEarly = await _staff.PostJson($"{Api}/shipments/{shipment.Id}/delivered");
        var notRefused = await _staff.PostJson($"{Api}/shipments/{shipment.Id}/returned");

        Assert.Equal("Shipping.Shipment.InvalidTransition", await tooEarly.ErrorCode());
        Assert.Equal(HttpStatusCode.Conflict, notRefused.StatusCode);
    }

    [Fact]
    public async Task Reports_show_old_parcels_refusals_and_money_held_by_couriers()
    {
        var courier = await factory.CourierAsync();
        var inTransit = await (await Create((await factory.OrderAsync()).Id, courier.Id, ShippingApiFactory.Tracking())).Read<ShipmentDto>();
        var delivered = await (await Create((await factory.OrderAsync()).Id, courier.Id, ShippingApiFactory.Tracking())).Read<ShipmentDto>();
        var refused = await (await Create((await factory.OrderAsync()).Id, courier.Id, ShippingApiFactory.Tracking())).Read<ShipmentDto>();
        foreach (var s in new[] { inTransit, delivered, refused })
        {
            await _staff.PostJson($"{Api}/shipments/{s.Id}/handed-over");
        }

        await _staff.PostJson($"{Api}/shipments/{delivered.Id}/delivered");
        await _staff.PostJson($"{Api}/shipments/{refused.Id}/refused");

        var old = await (await _staff.Get($"{Api}/reports/in-transit?olderThanDays=0")).Read<List<InTransitDto>>();
        var refusals = await (await _staff.Get($"{Api}/reports/refusals")).Read<RefusalReportDto>();
        var held = await (await _staff.Get($"{Api}/reports/cod-held")).Read<List<CodHeldDto>>();

        Assert.Contains(old, s => s.ShipmentId == inTransit.Id);
        Assert.DoesNotContain(old, s => s.ShipmentId == delivered.Id);
        var byCourier = refusals.ByCourier.Single(c => c.CourierId == courier.Id);
        Assert.Equal((1, 1, 0.5m), (byCourier.Delivered, byCourier.Refused, byCourier.RefusalRate));
        Assert.Contains(refusals.ByCustomer, c => c.Refused == 1 && c.RefusalRate == 1m);
        var money = held.Single(h => h.CourierId == courier.Id);
        Assert.Equal((1, 41.90m, 0), (money.Parcels, money.HeldAmount, money.OverdueParcels));
    }

    [Fact]
    public async Task Couriers_are_configured_by_admin_and_read_by_staff()
    {
        var staff = factory.CreateClientWithToken(ApiFactory.CreateTestToken([Roles.Staff]));

        var create = await staff.PostJson($"{Api}/couriers", new { name = "X" });
        var list = await staff.Get($"{Api}/couriers");
        var invalid = await factory.CreateAdminClient().PostJson($"{Api}/couriers", new
        {
            name = "Bad", enabledCountries = new[] { "DE" }, deliveryFee = 2m, codFeeFixed = 0m, codFeePercent = 50m,
            acceptsFlammableGoods = true, remittanceSchedule = "Weekly", remittanceDays = 7, isEnabled = true,
        });

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }
}
