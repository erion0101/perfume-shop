using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using PerfumeShop.Modules.Shipping.Domain;
using PerfumeShop.Modules.Shipping.Infrastructure;
using PerfumeShop.Modules.Shipping.Infrastructure.Couriers;
using PerfumeShop.Shared.Infrastructure.Auth;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Shipping.Features.Shared;

internal sealed record CourierDto(
    Guid Id,
    string Name,
    string? ContactName,
    string? ContactPhone,
    string? ContactEmail,
    IReadOnlyList<string> EnabledCountries,
    decimal DeliveryFee,
    decimal CodFeeFixed,
    decimal CodFeePercent,
    bool AcceptsFlammableGoods,
    string RemittanceSchedule,
    int RemittanceDays,
    string? Notes,
    bool IsEnabled,
    bool IsSelectable);

internal sealed record RecipientDto(string FullName, string Phone, string CountryCode, string City, string Street, string? Notes);

internal sealed record ShipmentTimelineDto(DateTime AtUtc, string? From, string To, string Source, string ActorId, string? Note);

internal sealed record ShipmentDto(
    Guid Id,
    Guid OrderId,
    string OrderNumber,
    Guid CourierId,
    string CourierName,
    string TrackingNumber,
    string Status,
    decimal CodAmount,
    int ParcelCount,
    int? WeightGrams,
    RecipientDto Recipient,
    string? Note,
    DateTime CreatedAtUtc,
    DateTime? HandedOverAtUtc,
    DateTime? DeliveredAtUtc,
    DateTime? RefusedAtUtc,
    DateTime? ReturnedAtUtc,
    Guid? SettlementId,
    DateTime? SettledAtUtc,
    decimal? CollectedAmount,
    decimal? CodFee,
    string? ReceiptOverrideReason,
    IReadOnlyList<ShipmentTimelineDto> Timeline);

internal static class ShippingMappings
{
    public static CourierDto ToDto(this Courier c) => new(
        c.Id, c.Name, c.ContactName, c.ContactPhone, c.ContactEmail, c.EnabledCountries, c.DeliveryFee, c.CodFeeFixed, c.CodFeePercent,
        c.AcceptsFlammableGoods, c.RemittanceSchedule.ToString(), c.RemittanceDays, c.Notes, c.IsEnabled,
        c.EnabledCountries.Any(c.IsSelectableFor));

    public static ShipmentDto ToDto(this Shipment s) => new(
        s.Id, s.OrderId, s.OrderNumber, s.CourierId, s.CourierName, s.TrackingNumber, s.Status.ToString(), s.CodAmount, s.ParcelCount, s.WeightGrams,
        new RecipientDto(s.Recipient.FullName, s.Recipient.Phone, s.Recipient.CountryCode, s.Recipient.City, s.Recipient.Street, s.Recipient.Notes),
        s.Note, s.CreatedAtUtc, s.HandedOverAtUtc, s.DeliveredAtUtc, s.RefusedAtUtc, s.ReturnedAtUtc, s.SettlementId, s.SettledAtUtc, s.CollectedAmount, s.CodFee,
        s.ReceiptOverrideReason,
        [.. s.History.OrderBy(h => h.ChangedAtUtc).ThenBy(h => h.Id)
            .Select(h => new ShipmentTimelineDto(h.ChangedAtUtc, h.FromStatus?.ToString(), h.ToStatus.ToString(), h.Source.ToString(), h.ActorId, h.Note))]);

    public static string Actor(this ClaimsPrincipal user) => user.GetUserId()?.ToString() ?? "unknown";
}

internal static class ShippingFeatureErrors
{
    public static readonly Error CourierNotFound = Error.NotFound("Shipping.Courier.NotFound", "Courier not found.");
    public static readonly Error CourierNameTaken = Error.Conflict("Shipping.Courier.NameTaken", "A courier with this name already exists.");
    public static readonly Error ShipmentNotFound = Error.NotFound("Shipping.Shipment.NotFound", "Shipment not found.");
    public static readonly Error OrderNotFound = Error.NotFound("Shipping.Order.NotFound", "Order not found.");
    public static readonly Error OrderNotConfirmed = Error.Conflict("Shipping.Order.NotConfirmed", "Only confirmed orders can be shipped.");
    public static readonly Error OrderAlreadyHasShipment = Error.Conflict("Shipping.Order.HasShipment", "This order already has a shipment with another courier or tracking number.");
    public static readonly Error TrackingTaken = Error.Conflict("Shipping.Shipment.TrackingTaken", "This tracking number is already used by another shipment.");
    public static readonly Error FileTooLarge = Error.Validation("Shipping.Csv.TooLarge", "The file is larger than 2 MB.");
    public static readonly Error FileEmpty = Error.Validation("Shipping.Csv.Empty", "Send the CSV file as the request body (Content-Type: text/csv).");
}

internal static class ShippingHttp
{
    /// <summary>Reads a CSV file sent as the raw request body.</summary>
    public static async Task<Result<string>> ReadCsvAsync(this HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength > Csv.MaxBytes)
        {
            return ShippingFeatureErrors.FileTooLarge;
        }

        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var content = await reader.ReadToEndAsync(cancellationToken);
        if (content.Length > Csv.MaxBytes)
        {
            return ShippingFeatureErrors.FileTooLarge;
        }

        return string.IsNullOrWhiteSpace(content) ? ShippingFeatureErrors.FileEmpty : content;
    }

    public static TimeZoneInfo Zone(this ShopInfo shop) => TimeZoneInfo.FindSystemTimeZoneById(shop.TimeZone);

    /// <summary>Kosovo calendar day for a UTC instant.</summary>
    public static DateOnly LocalDay(this ShopInfo shop, DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, shop.Zone()));

    /// <summary>First UTC instant after the given Kosovo day.</summary>
    public static DateTime EndOfLocalDayUtc(this ShopInfo shop, DateOnly day) =>
        TimeZoneInfo.ConvertTimeToUtc(day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), shop.Zone());
}
