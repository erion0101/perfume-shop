namespace PerfumeShop.Modules.Catalog.Contracts;

/// <summary>Synchronous API other modules use (e.g. Orders when building a cart or order).</summary>
public interface ICatalogApi
{
    /// <summary>Current price of a variant; null when the variant does not exist or has no price.</summary>
    Task<VariantPriceDto?> GetVariantPriceAsync(Guid variantId, CancellationToken cancellationToken = default);

    /// <summary>What an order line snapshots; null when the variant does not exist.</summary>
    Task<VariantSummaryDto?> GetVariantSummaryAsync(Guid variantId, CancellationToken cancellationToken = default);

    /// <summary>Summaries of many variants in one query; unknown ids are left out.</summary>
    Task<IReadOnlyDictionary<Guid, VariantSummaryDto>> GetVariantSummariesAsync(IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken = default);
}

/// <param name="Amount">EUR, VAT (18%) included.</param>
/// <param name="IsPurchasable">Variant active, priced, and its fragrance published.</param>
public sealed record VariantPriceDto(
    Guid VariantId,
    decimal Amount,
    string Currency,
    decimal? CompareAtAmount,
    bool IsPurchasable);

/// <param name="VariantType">FullBottle, Tester, Mini, GiftSet, SampleSet.</param>
/// <param name="SizeUnit">Ml, Gram, Piece; null for sets without a size.</param>
public sealed record VariantSummaryDto(
    Guid VariantId,
    Guid FragranceId,
    string Sku,
    string? Ean,
    string BrandName,
    string FragranceName,
    string FragranceSlug,
    string Concentration,
    string VariantType,
    decimal? SizeValue,
    string? SizeUnit,
    decimal? PriceAmount,
    string? PriceCurrency,
    bool IsActive,
    bool IsPurchasable,
    string? ImageUrl);
