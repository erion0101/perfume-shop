using System.Security.Claims;
using PerfumeShop.Modules.Orders.Domain;
using PerfumeShop.Shared.Infrastructure.Auth;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Orders.Features.Shared;

internal sealed record OrderLineDto(
    Guid Id,
    Guid VariantId,
    string Sku,
    string BrandName,
    string FragranceName,
    string VariantType,
    decimal? SizeValue,
    string? SizeUnit,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal,
    decimal VatAmount,
    IReadOnlyList<LineBatch> Batches);

internal sealed record OrderAddressViewDto(string CountryCode, string City, string Street, string? Notes);

internal sealed record TimelineEntryDto(DateTime AtUtc, string? From, string To, string ActorKind, string ActorId, string? Note);

internal sealed record OrderNoteDto(DateTime AtUtc, string AuthorId, string Text);

/// <summary>What the customer sees about their order.</summary>
internal sealed record OrderDto(
    Guid Id,
    string Number,
    string Status,
    string PaymentMethod,
    string Currency,
    decimal Subtotal,
    decimal DeliveryFee,
    decimal Total,
    decimal VatAmount,
    string CustomerName,
    string CustomerPhone,
    OrderAddressViewDto DeliveryAddress,
    string? CustomerNote,
    DateTime PlacedAtUtc,
    DateTime? ConfirmEndsAtUtc,
    string? CourierName,
    string? TrackingNumber,
    bool CanCancel,
    IReadOnlyList<OrderLineDto> Lines,
    IReadOnlyList<TimelineEntryDto> Timeline);

/// <summary>Staff view: everything, plus internal notes.</summary>
internal sealed record OrderAdminDto(
    OrderDto Order,
    Guid CustomerId,
    string? CancellationReason,
    decimal? CashCollectedAmount,
    IReadOnlyList<OrderNoteDto> Notes);

internal static class OrderDtoMappings
{
    public static OrderDto ToDto(this Order o, bool forCustomer) => new(
        o.Id,
        o.Number,
        o.Status.ToString(),
        Order.PaymentMethod,
        Order.Currency,
        o.Subtotal,
        o.DeliveryFee,
        o.Total,
        o.VatAmount,
        o.Customer.FullName,
        o.Customer.Phone,
        new OrderAddressViewDto(o.Address.CountryCode, o.Address.City, o.Address.Street, o.Address.Notes),
        o.CustomerNote,
        o.PlacedAtUtc,
        o.Status == OrderStatus.Pending ? o.ExpiresAtUtc : null,
        o.CourierName,
        o.TrackingNumber,
        o.Status == OrderStatus.Pending,
        [.. o.Lines.OrderBy(l => l.Id).Select(l => new OrderLineDto(
            l.Id, l.VariantId, l.Sku, l.BrandName, l.FragranceName, l.VariantType, l.SizeValue, l.SizeUnit,
            l.UnitPrice, l.Quantity, l.LineTotal, l.VatAmount, l.Batches))],
        [.. o.History.OrderBy(h => h.ChangedAtUtc).ThenBy(h => h.Id).Select(h => new TimelineEntryDto(
            h.ChangedAtUtc, h.FromStatus?.ToString(), h.ToStatus.ToString(), h.ActorKind.ToString(),
            // Customers do not see staff user ids.
            forCustomer && h.ActorKind == ActorKind.Staff ? "staff" : h.ActorId,
            h.Note))]);

    public static OrderAdminDto ToAdminDto(this Order o) => new(
        o.ToDto(forCustomer: false),
        o.CustomerId,
        o.CancellationReason,
        o.CashCollectedAmount,
        [.. o.Notes.OrderBy(n => n.CreatedAtUtc).Select(n => new OrderNoteDto(n.CreatedAtUtc, n.AuthorId, n.Text))]);

    public static OrderActor StaffActor(this ClaimsPrincipal user) => OrderActor.Staff(user.GetUserId()?.ToString() ?? "unknown");
}

internal static class OrderFeatureErrors
{
    public static readonly Error OrderNotFound = Error.NotFound("Orders.Order.NotFound", "Order not found.");
    public static readonly Error IdempotencyKeyMissing = Error.Validation(
        "Orders.Order.IdempotencyKeyMissing", "Send an Idempotency-Key header (8-100 characters, e.g. a new GUID per checkout).");
    public static readonly Error IdempotencyKeyReused = Error.Conflict(
        "Orders.Order.IdempotencyKeyReused", "This Idempotency-Key was already used for a different order.");
    public static readonly Error CustomerNotFound = Error.Unauthorized("Orders.Customer.NotFound", "Sign in again.");
    public static readonly Error CustomerBlocked = Error.Forbidden("Orders.Customer.Blocked", "This account cannot place orders. Please contact us.");
    public static readonly Error TooManyRefusals = Error.Forbidden(
        "Orders.Customer.TooManyRefusals", "Orders with payment on delivery are not available for this account. Please contact us.");
    public static readonly Error AddressRequired = Error.Validation(
        "Orders.Order.AddressRequired", "Choose a delivery address (or add one to your account first).");
    public static readonly Error CartNotOrderable = Error.Conflict(
        "Orders.Order.CartNotOrderable", "Some items are no longer available or delivery is not possible. Get a new quote.");

    public static Error TooManyOpenOrders(int max) => Error.Conflict(
        "Orders.Customer.TooManyOpenOrders", $"You already have {max} open order(s). Wait until they are delivered before ordering again.");
}
