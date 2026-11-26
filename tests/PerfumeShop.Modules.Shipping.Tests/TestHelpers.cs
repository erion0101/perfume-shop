using PerfumeShop.Modules.Shipping.Domain;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Shipping.Tests;

internal static class TestHelpers
{
    public static readonly DateTime Now = new(2026, 11, 26, 10, 0, 0, DateTimeKind.Utc);

    public static T Ok<T>(this Result<T> result)
    {
        Assert.True(result.IsSuccess, $"Expected success but got {result.Error.Code}: {result.Error.Message}");
        return result.Value;
    }

    public static void Ok(this Result result) =>
        Assert.True(result.IsSuccess, $"Expected success but got {result.Error.Code}: {result.Error.Message}");

    public static void Fails(this Result result, string expectedCode)
    {
        Assert.True(result.IsFailure, $"Expected {expectedCode} but the operation succeeded.");
        Assert.Equal(expectedCode, result.Error.Code);
    }

    public static CourierDetails Details(bool flammable = true, bool enabled = true, string[]? countries = null, decimal codFixed = 1.00m, decimal codPercent = 1.00m, int remittanceDays = 7) =>
        new("Posta e Kosovës", "Arta", "+383 38 000 000", null, countries ?? ["XK"], 2.50m, codFixed, codPercent, flammable,
            RemittanceSchedule.Weekly, remittanceDays, null, enabled);

    public static Courier Courier(bool flammable = true, bool enabled = true) => PerfumeShop.Modules.Shipping.Domain.Courier.Create(Details(flammable, enabled)).Ok();

    public static Recipient Recipient(string country = "XK") => new("Arbër Krasniqi", "+38344123456", country, "Lipjan", "Rr. Adem Jashari 12", "kati 2");

    public static Result<Shipment> CreateShipment(Courier? courier = null, string tracking = "PK2026000001XK", decimal cod = 41.90m, string country = "XK") =>
        Shipment.Create(Guid.NewGuid(), "PS-2026-000001", Guid.NewGuid(), courier ?? Courier(), tracking, cod, 1, 450, Recipient(country), null, "staff-1", Now);

    public static Shipment Created() => CreateShipment().Ok();

    public static Shipment At(ShipmentStatus status)
    {
        var shipment = Created();
        ShipmentStatus[] path = status switch
        {
            ShipmentStatus.HandedOver => [ShipmentStatus.HandedOver],
            ShipmentStatus.InTransit => [ShipmentStatus.HandedOver, ShipmentStatus.InTransit],
            ShipmentStatus.Delivered => [ShipmentStatus.HandedOver, ShipmentStatus.InTransit, ShipmentStatus.Delivered],
            ShipmentStatus.Refused => [ShipmentStatus.HandedOver, ShipmentStatus.Refused],
            ShipmentStatus.ReturnedToWarehouse => [ShipmentStatus.HandedOver, ShipmentStatus.Refused, ShipmentStatus.ReturnedToWarehouse],
            _ => [],
        };
        foreach (var step in path)
        {
            shipment.MoveTo(step, "staff-1", UpdateSource.Manual, null, Now).Ok();
        }

        return shipment;
    }
}
