using System.Security.Claims;
using PerfumeShop.Modules.Catalog.Contracts;
using PerfumeShop.Modules.Inventory.Domain;
using PerfumeShop.Shared.Infrastructure.Auth;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Inventory.Features.Shared;

/// <summary>What the variant is, from Catalog (Inventory stores only the variant id).</summary>
internal sealed record VariantInfoDto(string Sku, string BrandName, string FragranceName, string VariantType, string? Size);

internal sealed record StockItemDto(
    Guid VariantId,
    VariantInfoDto? Variant,
    int OnHand,
    int Reserved,
    int Available,
    int LowStockThreshold,
    bool IsLowStock,
    DateTime UpdatedAtUtc);

internal sealed record BatchDto(
    Guid Id,
    Guid ReceiptId,
    string BatchCode,
    int QuantityReceived,
    int QuantityRemaining,
    decimal UnitCost,
    string Currency,
    string SupplierName,
    string SupplierInvoiceNumber,
    DateOnly SupplierInvoiceDate,
    DateOnly ReceivedOn);

internal sealed record MovementDto(
    DateTime OccurredAtUtc,
    string Type,
    int Quantity,
    int OnHandDelta,
    int ReservedDelta,
    int OnHandAfter,
    int ReservedAfter,
    Guid? OrderId,
    string? BatchCodes,
    string? Reason,
    string? Actor);

internal sealed record ReservationDto(Guid OrderId, Guid OrderLineId, int Quantity, string Status, DateTime? ExpiresAtUtc, DateTime CreatedAtUtc);

internal static class InventoryFeatureErrors
{
    public static readonly Error StockItemNotFound = Error.NotFound("Inventory.Item.NotFound", "No stock has been recorded for this variant.");

    public static Error UnknownVariants(IEnumerable<Guid> ids) =>
        Error.NotFound("Inventory.Variant.NotFound", $"Unknown Catalog variant(s): {string.Join(", ", ids)}.");
}

internal static class InventoryDtoMappings
{
    public static VariantInfoDto ToInfo(this VariantSummaryDto v) => new(
        v.Sku,
        v.BrandName,
        v.FragranceName,
        v.VariantType,
        v.SizeValue is { } size ? FormattableString.Invariant($"{size:0.#} {(v.SizeUnit switch { "Ml" => "ml", "Gram" => "g", _ => "pcs" })}") : null);

    public static StockItemDto ToDto(this StockItem item, VariantSummaryDto? variant) => new(
        item.Id, variant?.ToInfo(), item.OnHand, item.Reserved, item.Available, item.LowStockThreshold, item.IsLowStock, item.UpdatedAtUtc);

    public static BatchDto ToDto(this StockBatch b) => new(
        b.Id, b.ReceiptId, b.BatchCode, b.QuantityReceived, b.QuantityRemaining, b.UnitCost.Amount, b.UnitCost.Currency,
        b.SupplierName, b.SupplierInvoiceNumber, b.SupplierInvoiceDate, b.ReceivedOn);

    public static MovementDto ToDto(this StockMovement m) => new(
        m.OccurredAtUtc, m.Type.ToString(), m.Quantity, m.OnHandDelta, m.ReservedDelta, m.OnHandAfter, m.ReservedAfter,
        m.OrderId, m.BatchCodes, m.Reason, m.Actor);

    /// <summary>Who did it, for the ledger: the staff user id from the token.</summary>
    public static string Actor(this ClaimsPrincipal user) => user.GetUserId()?.ToString() ?? "unknown";
}
