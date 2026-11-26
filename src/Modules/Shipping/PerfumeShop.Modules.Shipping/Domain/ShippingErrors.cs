using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Shipping.Domain;

internal static class ShippingErrors
{
    public static readonly Error CourierInvalid = Error.Validation("Shipping.Courier.Invalid",
        "Courier needs a name (max 100), countries from XK/AL/MK, fees 0-100 EUR, COD % 0-10, remittance days 1-60.");
    public static readonly Error CourierDisabled = Error.Conflict("Shipping.Courier.Disabled", "This courier is disabled.");
    public static readonly Error CourierNotForFlammables = Error.Conflict(
        "Shipping.Courier.NotForFlammables", "This courier does not accept flammable goods (perfume is UN1266, Class 3).");
    public static readonly Error CourierCountry = Error.Conflict("Shipping.Courier.Country", "This courier does not deliver to the order's country.");
    public static readonly Error TrackingRequired = Error.Validation("Shipping.Shipment.TrackingRequired", "Tracking number is required (max 100 characters).");
    public static readonly Error ParcelsInvalid = Error.Validation("Shipping.Shipment.ParcelsInvalid", "Parcel count 1-10, weight 1-30000 g.");
    public static readonly Error AlreadySettled = Error.Conflict("Shipping.Shipment.AlreadySettled", "The cash for this shipment was already settled.");

    public static readonly Error ReceiptOverrideReasonRequired = Error.Validation(
        "Shipping.Shipment.ReceiptOverrideReasonRequired", "Handing over a parcel without an issued fiscal receipt needs a reason (5-500 characters).");

    /// <summary>Details "parcels": [{ trackingNumber, orderNumber, receipt }] so the admin can list them.</summary>
    public static Error ReceiptMissing(IReadOnlyCollection<(string TrackingNumber, string OrderNumber, string Receipt)> parcels) =>
        Error.Conflict(
            "Shipping.Shipment.ReceiptMissing",
            "No issued fiscal receipt for: " + string.Join(", ", parcels.Select(p => $"{p.TrackingNumber} ({p.OrderNumber}, receipt {p.Receipt})"))
            + ". Retry the receipt in Payments, or hand over with overrideMissingReceipt = true and a reason.")
        .WithDetails("parcels", parcels.Select(p => new { trackingNumber = p.TrackingNumber, orderNumber = p.OrderNumber, receipt = p.Receipt }).ToList());

    public static Error InvalidTransition(ShipmentStatus from, ShipmentStatus to) =>
        Error.Conflict("Shipping.Shipment.InvalidTransition", $"A shipment that is {from} cannot become {to}.");
}
