using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using PerfumeShop.Modules.Shipping.Contracts;
using PerfumeShop.Modules.Shipping.Features.CourierFiles;
using PerfumeShop.Modules.Shipping.Features.Settlements;
using PerfumeShop.Modules.Shipping.Features.Shared;
using PerfumeShop.Modules.Shipping.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Outbox;

namespace PerfumeShop.Modules.Shipping.Tests.Integration;

/// <summary>Order → shipment → courier → settlement, across Shipping, Orders and Inventory.</summary>
public sealed class ShippingFlowTests(ShippingApiFactory factory)
{
    private const string Api = "/api/admin/shipping";
    private readonly HttpClient _staff = factory.CreateAdminClient();

    private static string Amount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private async Task<ShipmentDto> ShipAsync(TestOrder order, CourierDto courier, string? tracking = null)
    {
        var response = await _staff.PostJson($"{Api}/shipments", new { orderId = order.Id, courierId = courier.Id, trackingNumber = tracking ?? ShippingApiFactory.Tracking(), parcelCount = 1, weightGrams = 450 });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Text());
        return await response.Read<ShipmentDto>();
    }

    [Fact]
    public async Task Delivered_parcel_is_settled_and_the_order_becomes_cash_collected()
    {
        var courier = await factory.CourierAsync();
        var order = await factory.OrderAsync();
        var shipment = await ShipAsync(order, courier);

        // Shipping told Orders: the order is shipped and the stock committed (batch codes on the lines).
        var shipped = await factory.OrderStateAsync(order.Id);
        Assert.Equal(("Shipped", courier.Name, shipment.TrackingNumber), (shipped.Status, shipped.CourierName, shipped.TrackingNumber));
        Assert.Equal(41.90m, shipment.CodAmount);

        var slip = System.Net.WebUtility.HtmlDecode(await (await _staff.Get($"{Api}/shipments/{shipment.Id}/packing-slip")).Text());
        Assert.Contains("Hape pakon para se të paguash", slip, StringComparison.Ordinal);
        Assert.Contains("B2391×1", slip, StringComparison.Ordinal);
        Assert.Contains("41.90", slip, StringComparison.Ordinal);

        var manifest = await _staff.Get($"{Api}/couriers/{courier.Id}/manifest?format=csv");
        Assert.Equal("text/csv", manifest.Content.Headers.ContentType!.MediaType);
        Assert.Contains($"{shipment.TrackingNumber};Arbër Krasniqi", Encoding.UTF8.GetString(await manifest.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)), StringComparison.Ordinal);
        var handover = await (await _staff.PostJson($"{Api}/couriers/{courier.Id}/handover")).Read<HandoverResult>();
        Assert.Contains(shipment.TrackingNumber, handover.TrackingNumbers);

        var statuses = await (await _staff.PostCsv($"{Api}/couriers/{courier.Id}/status-imports", $"tracking_number;status;note\n{shipment.TrackingNumber};Dorëzuar;Pranoi vetë\n")).Read<StatusImportResult>();
        Assert.Equal(1, statuses.Applied);
        Assert.Equal("Delivered", (await factory.OrderStateAsync(order.Id)).Status);

        var statement = $"tracking_number;collected_amount;fee\n{shipment.TrackingNumber};{Amount(41.90m).Replace('.', ',')};1,42\n";
        var settlement = await (await _staff.PostCsv($"{Api}/couriers/{courier.Id}/settlements?reference=POSTA-2026-W48", statement)).Read<SettlementDto>();
        Assert.Equal((1, 41.90m, 1.42m, 40.48m, false), (settlement.MatchedCount, settlement.MatchedAmount, settlement.TotalFees, settlement.NetPaid, settlement.Replayed));
        Assert.Empty(settlement.Mismatches);
        Assert.Equal("CashCollected", (await factory.OrderStateAsync(order.Id)).Status);

        // The same statement again is recognised and not counted twice.
        var again = await (await _staff.PostCsv($"{Api}/couriers/{courier.Id}/settlements?reference=whatever", statement)).Read<SettlementDto>();
        Assert.Equal((settlement.Id, true, 1), (again.Id, again.Replayed, again.MatchedCount));

        var final = await (await _staff.Get($"{Api}/shipments/{shipment.Id}")).Read<ShipmentDto>();
        Assert.Equal((settlement.Id, 41.90m), (final.SettlementId!.Value, final.CollectedAmount!.Value));
        Assert.Equal(["Created", "HandedOver", "Delivered"], final.Timeline.Select(t => t.To));
        Assert.Equal(["Manual", "Manual", "CsvImport"], final.Timeline.Select(t => t.Source));

        await using var scope = factory.CreateScope();
        while (await scope.ServiceProvider.GetRequiredService<OutboxProcessor<ShippingDbContext>>().ProcessAsync(TestContext.Current.CancellationToken) > 0)
        {
            // drain: other tests' events may fill the first batches
        }

        Assert.Contains(factory.Events.Received, e => e is ShipmentCreatedIntegrationEvent c && c.ShipmentId == shipment.Id && c.CodAmount == 41.90m);
        Assert.Single(factory.Events.Received, e => e is ShipmentHandedOverIntegrationEvent h && h.ShipmentId == shipment.Id && h.TrackingNumber == shipment.TrackingNumber && h.CodAmount == 41.90m);
        Assert.Contains(factory.Events.Received, e => e is ShipmentDeliveredIntegrationEvent d && d.ShipmentId == shipment.Id);
        Assert.Single(factory.Events.Received, e => e is CodSettledIntegrationEvent s && s.SettlementId == settlement.Id && s.MatchedAmount == 41.90m);
    }

    [Fact]
    public async Task Refused_parcel_returns_stock_only_when_it_is_back_in_the_warehouse()
    {
        var courier = await factory.CourierAsync();
        var order = await factory.OrderAsync(quantity: 2, stock: 3);
        var shipment = await ShipAsync(order, courier);
        await _staff.PostJson($"{Api}/shipments/{shipment.Id}/handed-over");
        Assert.Equal(1, await factory.AvailableAsync(order.VariantId));

        await _staff.PostCsv($"{Api}/couriers/{courier.Id}/status-imports", $"tracking_number;status;note\n{shipment.TrackingNumber};Refuzuar;Nuk e deshi\n");

        // Refused at the door, but the parcel is still with the courier: nothing goes back on sale yet.
        Assert.Equal("Refused", (await (await _staff.Get($"{Api}/shipments/{shipment.Id}")).Read<ShipmentDto>()).Status);
        Assert.Equal("Shipped", (await factory.OrderStateAsync(order.Id)).Status);
        Assert.Equal(1, await factory.AvailableAsync(order.VariantId));

        var returned = await _staff.PostJson($"{Api}/shipments/{shipment.Id}/returned", new { note = "Skanuar në magazinë" });

        Assert.Equal(HttpStatusCode.OK, returned.StatusCode);
        Assert.Equal("ReturnedToWarehouse", (await returned.Read<ShipmentDto>()).Status);
        Assert.Equal("Refused", (await factory.OrderStateAsync(order.Id)).Status);
        Assert.Equal(3, await factory.AvailableAsync(order.VariantId));
    }

    [Fact]
    public async Task Statement_mismatches_are_reported_and_nothing_wrong_is_booked()
    {
        var courier = await factory.CourierAsync();
        var paid = await ShipAsync(await factory.OrderAsync(), courier);
        var underpaid = await ShipAsync(await factory.OrderAsync(), courier);
        var refused = await ShipAsync(await factory.OrderAsync(), courier);
        foreach (var s in new[] { paid, underpaid, refused })
        {
            await _staff.PostJson($"{Api}/shipments/{s.Id}/handed-over");
        }

        await _staff.PostJson($"{Api}/shipments/{paid.Id}/delivered");
        await _staff.PostJson($"{Api}/shipments/{underpaid.Id}/delivered");
        await _staff.PostJson($"{Api}/shipments/{refused.Id}/refused", new { note = "refuzoi" });

        var statement = $"""
            tracking_number;collected_amount;fee
            {paid.TrackingNumber};41.90;1.42
            {underpaid.TrackingNumber};40.00;1.42
            {refused.TrackingNumber};41.90;1.42
            UNKNOWN-PARCEL-1;25.00;1.00
            {paid.TrackingNumber};41.90;1.42
            """;
        var settlement = await (await _staff.PostCsv($"{Api}/couriers/{courier.Id}/settlements", statement)).Read<SettlementDto>();

        Assert.Equal(["Matched", "AmountMismatch", "NotPayable", "UnknownTracking", "DuplicateInFile"], settlement.Lines.Select(l => l.Status));
        Assert.Equal((1, 41.90m), (settlement.MatchedCount, settlement.MatchedAmount));
        Assert.Equal(1, settlement.Mismatches["AmountMismatch"]);
        Assert.Equal("Delivered", (await factory.OrderStateAsync(underpaid.OrderId)).Status); // not booked as collected
        Assert.Equal("Shipped", (await factory.OrderStateAsync(refused.OrderId)).Status);

        // A later statement repeating the paid parcel does not pay it twice.
        var later = await (await _staff.PostCsv($"{Api}/couriers/{courier.Id}/settlements", $"tracking_number;collected_amount;fee\n{paid.TrackingNumber};41.90;1.42\n")).Read<SettlementDto>();
        Assert.Equal(("AlreadySettled", 0), (later.Lines.Single().Status, later.MatchedCount));

        // Delivered but still unpaid after the courier's 7 remittance days.
        await using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        var c = db.Couriers.Single(x => x.Id == courier.Id);
        var overdue = await SettlementsHandler.UnpaidAsync(db, c, DateTime.UtcNow.AddDays(8), TestContext.Current.CancellationToken);
        Assert.Equal([underpaid.TrackingNumber], overdue.Select(o => o.TrackingNumber));
        Assert.Empty(await SettlementsHandler.UnpaidAsync(db, c, DateTime.UtcNow, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Paying_for_a_parcel_still_in_transit_marks_it_delivered_first()
    {
        var courier = await factory.CourierAsync();
        var order = await factory.OrderAsync();
        var shipment = await ShipAsync(order, courier);
        await _staff.PostJson($"{Api}/shipments/{shipment.Id}/handed-over");
        await _staff.PostJson($"{Api}/shipments/{shipment.Id}/in-transit");

        var settlement = await (await _staff.PostCsv($"{Api}/couriers/{courier.Id}/settlements", $"tracking_number;collected_amount;fee\n{shipment.TrackingNumber};41.90;1.42\n")).Read<SettlementDto>();

        Assert.Equal(1, settlement.MatchedCount);
        var final = await (await _staff.Get($"{Api}/shipments/{shipment.Id}")).Read<ShipmentDto>();
        Assert.Equal(("Delivered", "Settlement"), (final.Status, final.Timeline[^1].Source));
        Assert.Equal("CashCollected", (await factory.OrderStateAsync(order.Id)).Status);
    }

    [Fact]
    public async Task Status_files_are_idempotent_and_report_every_line()
    {
        var courier = await factory.CourierAsync();
        var delivered = await ShipAsync(await factory.OrderAsync(), courier);
        var notHandedOver = await ShipAsync(await factory.OrderAsync(), courier);
        await _staff.PostJson($"{Api}/shipments/{delivered.Id}/handed-over");
        var file = $"""
            tracking_number;status;note
            {delivered.TrackingNumber};Delivered;
            {notHandedOver.TrackingNumber};Delivered;
            NOT-OURS-123;Delivered;
            {delivered.TrackingNumber};lost;
            """;

        var first = await (await _staff.PostCsv($"{Api}/couriers/{courier.Id}/status-imports", file)).Read<StatusImportResult>();
        var sameFile = await (await _staff.PostCsv($"{Api}/couriers/{courier.Id}/status-imports", file.Replace("\n", "\r\n", StringComparison.Ordinal))).Read<StatusImportResult>();
        var lateNews = await (await _staff.PostCsv($"{Api}/couriers/{courier.Id}/status-imports", $"tracking_number;status\n{delivered.TrackingNumber};InTransit\n")).Read<StatusImportResult>();

        Assert.Equal(["Applied", "InvalidTransition", "UnknownTracking", "InvalidStatus"], first.Lines.Select(l => l.Outcome));
        Assert.Equal((first.ImportId, true, 1), (sameFile.ImportId, sameFile.Replayed, sameFile.Applied));
        Assert.Equal(("AlreadyApplied", false), (lateNews.Lines.Single().Outcome, lateNews.Replayed));
        var shipment = await (await _staff.Get($"{Api}/shipments/{delivered.Id}")).Read<ShipmentDto>();
        Assert.Single(shipment.Timeline, t => t.To == "Delivered");
    }

    [Fact]
    public async Task Invalid_csv_files_are_rejected_with_line_numbers()
    {
        var courier = await factory.CourierAsync();

        var response = await _staff.PostCsv($"{Api}/couriers/{courier.Id}/settlements", "tracking_number;collected_amount\nPK1;abc\n");
        var empty = await _staff.PostCsv($"{Api}/couriers/{courier.Id}/settlements", "");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("line 2", await response.Text(), StringComparison.Ordinal);
        Assert.Equal("Shipping.Csv.Empty", await empty.ErrorCode());
    }

    [Fact]
    public async Task Shipping_api_finds_a_shipment_by_order_and_by_tracking_number()
    {
        var courier = await factory.CourierAsync();
        var order = await factory.OrderAsync();
        var shipment = await ShipAsync(order, courier);

        await using var scope = factory.CreateScope();
        var api = scope.ServiceProvider.GetRequiredService<IShippingApi>();
        var byOrder = await api.GetShipmentForOrderAsync(order.Id, TestContext.Current.CancellationToken);
        var byTracking = await api.GetShipmentByTrackingNumberAsync(shipment.TrackingNumber.ToLowerInvariant(), TestContext.Current.CancellationToken);

        Assert.Equal((shipment.Id, "Created", courier.Name), (byOrder!.ShipmentId, byOrder.Status, byOrder.CourierName));
        Assert.Equal(shipment.Id, byTracking!.ShipmentId);
    }
}
