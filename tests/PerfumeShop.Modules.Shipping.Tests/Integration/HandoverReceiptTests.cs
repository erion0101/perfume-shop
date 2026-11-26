using System.Net;
using System.Text;
using PerfumeShop.Modules.Shipping.Features.CourierFiles;
using PerfumeShop.Modules.Shipping.Features.Shared;

namespace PerfumeShop.Modules.Shipping.Tests.Integration;

/// <summary>A failed fiscal receipt never blocks creating a shipment, but the parcel cannot leave without it unless staff override (with a reason).</summary>
public sealed class HandoverReceiptTests(ShippingApiFactory factory)
{
    private const string Api = "/api/admin/shipping";
    private readonly HttpClient _staff = factory.CreateAdminClient();

    private async Task<(TestOrder Order, ShipmentDto Shipment)> ShipAsync(CourierDto courier, string? receipt = null)
    {
        var order = await factory.OrderAsync();
        if (receipt is not null)
        {
            factory.Payments.SetReceipt(order.Id, receipt);
        }

        var response = await _staff.PostJson($"{Api}/shipments", new { orderId = order.Id, courierId = courier.Id, trackingNumber = ShippingApiFactory.Tracking() });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (order, await response.Read<ShipmentDto>());
    }

    [Fact]
    public async Task Shipment_is_created_even_when_the_receipt_failed()
    {
        var courier = await factory.CourierAsync();

        var (order, shipment) = await ShipAsync(courier, receipt: "Failed");

        Assert.Equal("Created", shipment.Status);
        Assert.Equal("Shipped", (await factory.OrderStateAsync(order.Id)).Status);
    }

    [Fact]
    public async Task Manifest_flags_every_parcel_without_an_issued_receipt()
    {
        var courier = await factory.CourierAsync();
        var (_, ok) = await ShipAsync(courier);
        var (_, failed) = await ShipAsync(courier, receipt: "Failed");
        var (_, missing) = await ShipAsync(courier, receipt: "Missing");

        var html = WebUtility.HtmlDecode(await (await _staff.Get($"{Api}/couriers/{courier.Id}/manifest")).Text());
        var csv = Encoding.UTF8.GetString(await (await _staff.Get($"{Api}/couriers/{courier.Id}/manifest?format=csv")).Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));

        Assert.Contains("KUJDES: 2 pako PA KUPON FISKAL", html, StringComparison.Ordinal);
        Assert.Contains("PA KUPON (Failed)", html, StringComparison.Ordinal);
        var lines = csv.Split('\n');
        Assert.Contains(";OK;", lines.Single(l => l.Contains(ok.TrackingNumber, StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Contains(";PA KUPON (Failed);", lines.Single(l => l.Contains(failed.TrackingNumber, StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Contains(";PA KUPON (Missing);", lines.Single(l => l.Contains(missing.TrackingNumber, StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manifest_has_no_warning_when_every_receipt_is_issued()
    {
        var courier = await factory.CourierAsync();
        await ShipAsync(courier);

        var html = await (await _staff.Get($"{Api}/couriers/{courier.Id}/manifest")).Text();

        Assert.DoesNotContain("KUJDES", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Handover_without_receipt_is_refused_and_lists_the_parcels()
    {
        var courier = await factory.CourierAsync();
        var (_, ok) = await ShipAsync(courier);
        var (_, failed) = await ShipAsync(courier, receipt: "Failed");

        var bulk = await _staff.PostJson($"{Api}/couriers/{courier.Id}/handover");
        var single = await _staff.PostJson($"{Api}/shipments/{failed.Id}/handed-over");

        Assert.Equal(HttpStatusCode.Conflict, bulk.StatusCode);
        Assert.Equal("Shipping.Shipment.ReceiptMissing", await bulk.ErrorCode());
        Assert.Contains(failed.TrackingNumber, await bulk.Text(), StringComparison.Ordinal);
        Assert.DoesNotContain(ok.TrackingNumber, await bulk.Text(), StringComparison.Ordinal);
        using (var problem = System.Text.Json.JsonDocument.Parse(await bulk.Text()))
        {
            var parcel = Assert.Single(problem.RootElement.GetProperty("parcels").EnumerateArray());
            Assert.Equal((failed.TrackingNumber, failed.OrderNumber, "Failed"),
                (parcel.GetProperty("trackingNumber").GetString(), parcel.GetProperty("orderNumber").GetString(), parcel.GetProperty("receipt").GetString()));
        }

        Assert.Equal("Shipping.Shipment.ReceiptMissing", await single.ErrorCode());
        Assert.Equal("Created", (await (await _staff.Get($"{Api}/shipments/{ok.Id}")).Read<ShipmentDto>()).Status); // all or nothing
    }

    [Fact]
    public async Task Override_needs_a_reason_and_is_recorded()
    {
        var courier = await factory.CourierAsync();
        var (_, ok) = await ShipAsync(courier);
        var (_, failed) = await ShipAsync(courier, receipt: "Pending");

        var noReason = await _staff.PostJson($"{Api}/couriers/{courier.Id}/handover", new { overrideMissingReceipt = true });
        var noFlag = await _staff.PostJson($"{Api}/couriers/{courier.Id}/handover", new { overrideReason = "Shërbimi fiskal nuk punon" });
        Assert.Equal("Shipping.Shipment.ReceiptOverrideReasonRequired", await noReason.ErrorCode());
        Assert.Equal("Shipping.Shipment.OverrideFlagMissing", await noFlag.ErrorCode());

        var response = await _staff.PostJson($"{Api}/couriers/{courier.Id}/handover",
            new { overrideMissingReceipt = true, overrideReason = "Shërbimi fiskal nuk punon, kuponi lëshohet pasdite" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Read<HandoverResult>();
        Assert.Equal([failed.TrackingNumber], result.WithoutReceipt);
        Assert.Contains(ok.TrackingNumber, result.TrackingNumbers);

        var shipment = await (await _staff.Get($"{Api}/shipments/{failed.Id}")).Read<ShipmentDto>();
        Assert.Equal(("HandedOver", "Shërbimi fiskal nuk punon, kuponi lëshohet pasdite"), (shipment.Status, shipment.ReceiptOverrideReason));
        Assert.Contains("PA KUPON FISKAL (receipt Pending) – override:", shipment.Timeline.Last().Note, StringComparison.Ordinal);
        Assert.Null((await (await _staff.Get($"{Api}/shipments/{ok.Id}")).Read<ShipmentDto>()).ReceiptOverrideReason);
    }

    [Fact]
    public async Task Single_parcel_override()
    {
        var courier = await factory.CourierAsync();
        var (_, failed) = await ShipAsync(courier, receipt: "Failed");

        var response = await _staff.PostJson($"{Api}/shipments/{failed.Id}/handed-over",
            new { note = "Merr korrieri", overrideMissingReceipt = true, overrideReason = "Klienti po pret, kuponi pas" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var shipment = await response.Read<ShipmentDto>();
        Assert.Equal("Klienti po pret, kuponi pas", shipment.ReceiptOverrideReason);
        Assert.EndsWith("· Merr korrieri", shipment.Timeline.Last().Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Courier_file_cannot_hand_over_a_parcel_without_receipt()
    {
        var courier = await factory.CourierAsync();
        var (_, failed) = await ShipAsync(courier, receipt: "Failed");

        var import = await (await _staff.PostCsv($"{Api}/couriers/{courier.Id}/status-imports", $"tracking_number;status\n{failed.TrackingNumber};HandedOver\n"))
            .Read<StatusImportResult>();

        Assert.Equal("ReceiptMissing", Assert.Single(import.Lines).Outcome);
    }
}
