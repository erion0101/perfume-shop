using System.Globalization;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Orders.Domain;

internal enum OrderStatus
{
    Pending = 1,
    Confirmed = 2,
    Shipped = 3,
    Delivered = 4,
    CashCollected = 5,
    Cancelled = 6,
    Refused = 7,
    Expired = 8,
}

internal enum ActorKind
{
    Customer = 1,
    Staff = 2,
    System = 3,
}

/// <summary>Who caused a change: a customer, a staff user (ids from the token) or a background job.</summary>
internal sealed record OrderActor(ActorKind Kind, string Id)
{
    public static readonly OrderActor System = new(ActorKind.System, "system");

    public static OrderActor Customer(Guid customerId) => new(ActorKind.Customer, customerId.ToString());

    public static OrderActor Staff(string userId) => new(ActorKind.Staff, userId);

    public bool IsStaff => Kind == ActorKind.Staff;
}

/// <summary>Kosovo VAT, 18%. Prices are gross (VAT included); the VAT part is stored for fiscal receipts.</summary>
internal static class Vat
{
    public const decimal Rate = 0.18m;

    public static decimal Of(decimal gross) => decimal.Round(gross * Rate / (1 + Rate), 2, MidpointRounding.AwayFromZero);
}

internal static class OrderNumber
{
    public static string Format(int year, int sequence) =>
        string.Create(CultureInfo.InvariantCulture, $"PS-{year}-{sequence:D6}");
}

/// <summary>Customer contact copied onto the order at purchase time.</summary>
internal sealed record CustomerSnapshot(string FullName, string Phone);

/// <summary>Delivery address copied onto the order at purchase time.</summary>
internal sealed record DeliveryAddress(Guid? AddressId, string CountryCode, string City, string Street, string? Notes);

/// <summary>A priced line before it becomes part of an order (prices come from Catalog).</summary>
internal sealed record OrderLineDraft(
    Guid LineId,
    Guid VariantId,
    Guid FragranceId,
    string Sku,
    string BrandName,
    string FragranceName,
    string VariantType,
    decimal? SizeValue,
    string? SizeUnit,
    decimal UnitPrice,
    int Quantity);

/// <summary>Batch units shipped for a line (from Inventory), printed on the fiscal receipt if needed.</summary>
internal sealed record LineBatch(string BatchCode, int Quantity);

internal static class OrderErrors
{
    public static readonly Error NoLines = Error.Validation("Orders.Order.NoLines", $"An order needs 1 to {Order.MaxLines} lines.");
    public static readonly Error QuantityInvalid = Error.Validation("Orders.Line.QuantityInvalid", $"Quantity per line must be 1 to {Order.MaxQuantityPerLine}.");
    public static readonly Error DuplicateVariant = Error.Validation("Orders.Line.DuplicateVariant", "Each variant can appear once; increase the quantity instead.");
    public static readonly Error PriceInvalid = Error.Validation("Orders.Line.PriceInvalid", "Every line needs a price above 0.");
    public static readonly Error FullNameRequired = Error.Validation("Orders.Order.FullNameRequired", "Full name (2-100 characters) is required for delivery.");
    public static readonly Error PhoneRequired = Error.Validation("Orders.Order.PhoneRequired", "A phone number is required for delivery.");
    public static readonly Error AddressInvalid = Error.Validation("Orders.Order.AddressInvalid", "Delivery address needs a country, city and street.");
    public static readonly Error NoteTooLong = Error.Validation("Orders.Order.NoteTooLong", "The note can be at most 500 characters.");
    public static readonly Error DeliveryFeeInvalid = Error.Validation("Orders.Order.DeliveryFeeInvalid", "Delivery fee cannot be negative.");
    public static readonly Error IdempotencyKeyInvalid = Error.Validation("Orders.Order.IdempotencyKeyInvalid", "Idempotency-Key must be 8-100 characters.");
    public static readonly Error ReasonRequired = Error.Validation("Orders.Order.ReasonRequired", "A reason (max 500 characters) is required.");
    public static readonly Error CourierRequired = Error.Validation("Orders.Order.CourierRequired", "Courier name and tracking number (max 100 characters each) are required.");
    public static readonly Error AmountInvalid = Error.Validation("Orders.Order.AmountInvalid", "Collected amount must be 0 or more with at most 2 decimals.");
    public static readonly Error CollectionNoteRequired = Error.Validation(
        "Orders.Order.CollectionNoteRequired", "The collected amount differs from the order total: add a note explaining why.");
    public static readonly Error NoteRequired = Error.Validation("Orders.Order.NoteRequired", "Note text is required (max 1000 characters).");
    public static readonly Error OnlyStaff = Error.Forbidden("Orders.Order.OnlyStaff", "Only staff can do this.");
    public static readonly Error ConfirmationWindowPassed = Error.Conflict(
        "Orders.Order.ConfirmationWindowPassed", "The order was not confirmed in time and is expiring.");

    public static Error InvalidTransition(OrderStatus from, string action) =>
        Error.Conflict("Orders.Order.InvalidTransition", $"Cannot {action} an order that is {from}.");
}
