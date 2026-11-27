using PerfumeShop.Modules.Catalog.Domain.Brands;
using PerfumeShop.Modules.Catalog.Domain.Fragrances;
using PerfumeShop.Modules.Inventory.Contracts;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Catalog.Features.Shared;

internal sealed record MoneyDto(decimal Amount, string Currency);

internal sealed record SizeDto(decimal Value, string Unit);

internal sealed record NoteDto(string Name, string? ImageUrl);

internal sealed record NotePyramidDto(IReadOnlyList<NoteDto> Top, IReadOnlyList<NoteDto> Heart, IReadOnlyList<NoteDto> Base);

internal sealed record AccordDto(string Name, int Percentage);

internal sealed record SimilarFragranceDto(string BrandName, string Name, Guid? FragranceId);

internal sealed record ImageDto(string Url, string? AltText, int SortOrder);

internal sealed record VariantDto(
    Guid Id,
    string Sku,
    string? Ean,
    string Type,
    SizeDto? Size,
    MoneyDto? Price,
    MoneyDto? CompareAtPrice,
    bool IsActive,
    IReadOnlyList<ImageDto> Images,
    bool? InStock = null);

internal sealed record BrandDto(
    Guid Id,
    string Name,
    string Slug,
    string? CountryCode,
    string? LogoUrl,
    string? Description,
    int FragranceCount);

internal sealed record BrandRefDto(Guid Id, string Name, string Slug);

/// <summary>Everything the admin edits, including drafts, inactive variants and Fragella metadata.</summary>
internal sealed record FragranceAdminDto(
    Guid Id,
    BrandRefDto Brand,
    string Name,
    string Slug,
    string Status,
    string Concentration,
    string Gender,
    string? OlfactiveFamily,
    int? LaunchYear,
    IReadOnlyList<string> Perfumers,
    string? Description,
    NotePyramidDto Notes,
    IReadOnlyList<AccordDto> Accords,
    string? Longevity,
    string? Sillage,
    IReadOnlyList<string> Seasons,
    IReadOnlyList<string> Occasions,
    IReadOnlyList<SimilarFragranceDto> SimilarTo,
    string? FragellaId,
    DateTime? FragellaImportedAtUtc,
    IReadOnlyList<VariantDto> Variants,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    string? MoodOverride,
    string? Mood);

/// <summary>
/// Storefront product page: published fragrance, active variants only (cheapest first).
/// <c>InStock</c> comes from Inventory; exact quantities are not exposed publicly.
/// </summary>
internal sealed record FragranceDetailsDto(
    Guid Id,
    BrandRefDto Brand,
    string Name,
    string Slug,
    string Concentration,
    string Gender,
    string? OlfactiveFamily,
    int? LaunchYear,
    IReadOnlyList<string> Perfumers,
    string? Description,
    NotePyramidDto Notes,
    IReadOnlyList<AccordDto> Accords,
    string? Longevity,
    string? Sillage,
    IReadOnlyList<string> Seasons,
    IReadOnlyList<string> Occasions,
    IReadOnlyList<SimilarFragranceDto> SimilarTo,
    IReadOnlyList<VariantDto> Variants,
    bool InStock,
    string? Mood,
    DateTime UpdatedAtUtc);

internal static class CatalogDtoMappings
{
    public static BrandRefDto ToRef(this Brand brand) => new(brand.Id, brand.Name, brand.Slug.Value);

    public static BrandDto ToDto(this Brand brand, int fragranceCount) =>
        new(brand.Id, brand.Name, brand.Slug.Value, brand.CountryCode, brand.LogoUrl, brand.Description, fragranceCount);

    public static MoneyDto? ToDto(this Money? money) => money is null ? null : new(money.Amount, money.Currency);

    public static NotePyramidDto ToDto(this NotePyramid notes) =>
        new([.. notes.Top.Select(ToDto)], [.. notes.Heart.Select(ToDto)], [.. notes.Base.Select(ToDto)]);

    public static VariantDto ToDto(this FragranceVariant v) => new(
        v.Id,
        v.Sku.Value,
        v.Ean?.Value,
        v.Type.ToString(),
        v.Size is null ? null : new SizeDto(v.Size.Value, v.Size.Unit.ToString()),
        v.Price.ToDto(),
        v.CompareAtPrice.ToDto(),
        v.IsActive,
        [.. v.Images.Select(i => new ImageDto(i.Url, i.AltText, i.SortOrder))]);

    public static FragranceAdminDto ToAdminDto(this Fragrance f, Brand brand) => new(
        f.Id,
        brand.ToRef(),
        f.Name,
        f.Slug.Value,
        f.Status.ToString(),
        f.Concentration.ToString(),
        f.Gender.ToString(),
        f.OlfactiveFamily?.ToString(),
        f.LaunchYear,
        f.Perfumers,
        f.Description,
        f.Notes.ToDto(),
        [.. f.Accords.Select(a => new AccordDto(a.Name, a.Percentage))],
        f.Longevity?.ToString(),
        f.Sillage?.ToString(),
        FlagNames(f.Seasons),
        FlagNames(f.Occasions),
        [.. f.SimilarTo.Select(ToDto)],
        f.FragellaId,
        f.FragellaImportedAtUtc,
        [.. f.Variants.OrderBy(v => v.CreatedAtUtc).Select(v => v.ToDto())],
        f.CreatedAtUtc,
        f.UpdatedAtUtc,
        f.MoodOverride?.ToString(),
        f.Mood?.ToString());

    public static FragranceDetailsDto ToDetailsDto(this Fragrance f, Brand brand, IReadOnlyDictionary<Guid, StockAvailabilityDto> stock)
    {
        VariantDto[] variants = [.. f.Variants
            .Where(v => v.IsActive)
            .OrderBy(v => v.Price?.Amount)
            .Select(v => v.ToDto() with { InStock = stock.TryGetValue(v.Id, out var s) && s.InStock })];
        return f.ToDetailsDto(brand, variants);
    }

    private static FragranceDetailsDto ToDetailsDto(this Fragrance f, Brand brand, VariantDto[] variants) => new(
        f.Id,
        brand.ToRef(),
        f.Name,
        f.Slug.Value,
        f.Concentration.ToString(),
        f.Gender.ToString(),
        f.OlfactiveFamily?.ToString(),
        f.LaunchYear,
        f.Perfumers,
        f.Description,
        f.Notes.ToDto(),
        [.. f.Accords.Select(a => new AccordDto(a.Name, a.Percentage))],
        f.Longevity?.ToString(),
        f.Sillage?.ToString(),
        FlagNames(f.Seasons),
        FlagNames(f.Occasions),
        [.. f.SimilarTo.Select(ToDto)],
        variants,
        variants.Any(v => v.InStock == true),
        f.Mood?.ToString(),
        f.UpdatedAtUtc);

    /// <summary>Flags as names, e.g. Season.Autumn | Season.Winter → ["Autumn", "Winter"].</summary>
    public static IReadOnlyList<string> FlagNames<TFlags>(TFlags flags)
        where TFlags : struct, Enum
    {
        var value = Convert.ToInt32(flags, System.Globalization.CultureInfo.InvariantCulture);
        return [.. Enum.GetValues<TFlags>()
            .Select(f => (Flag: f, Bits: Convert.ToInt32(f, System.Globalization.CultureInfo.InvariantCulture)))
            .Where(f => f.Bits != 0 && (f.Bits & (f.Bits - 1)) == 0 && (value & f.Bits) == f.Bits)
            .Select(f => f.Flag.ToString())];
    }

    private static NoteDto ToDto(Note note) => new(note.Name, note.ImageUrl);

    private static SimilarFragranceDto ToDto(FragranceReference r) => new(r.BrandName, r.Name, r.FragranceId);
}
