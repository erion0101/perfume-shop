using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Catalog.Features.Shared;

internal static class CatalogFeatureErrors
{
    public static readonly Error BrandNotFound = Error.NotFound("Catalog.Brand.NotFound", "Brand not found.");
    public static readonly Error BrandNameTaken = Error.Conflict("Catalog.Brand.NameTaken", "A brand with this name already exists.");
    public static readonly Error BrandHasFragrances = Error.Conflict("Catalog.Brand.HasFragrances", "The brand still has fragrances. Move or delete them first.");
    public static readonly Error FragranceNotFound = Error.NotFound("Catalog.Fragrance.NotFound", "Fragrance not found.");
    public static readonly Error FragranceExists = Error.Conflict("Catalog.Fragrance.Exists", "This brand already has a fragrance with the same name and concentration.");
    public static readonly Error SlugTaken = Error.Conflict("Catalog.Slug.Taken", "This slug is already used.");
    public static readonly Error SlugExhausted = Error.Conflict("Catalog.Slug.Exhausted", "Could not find a free slug. Choose one explicitly.");
    public static readonly Error SkuTaken = Error.Conflict("Catalog.Variant.SkuTaken", "This SKU is already used by another variant.");
    public static readonly Error EanTaken = Error.Conflict("Catalog.Variant.EanTaken", "This EAN is already used by another variant.");
    public static readonly Error FragellaNotSearched = Error.NotFound(
        "Catalog.Fragella.NotSearched", "Unknown Fragella id. Search Fragella first, then import one of the results.");
    public static readonly Error FragellaPayloadInvalid = Error.Validation(
        "Catalog.Fragella.PayloadInvalid", "The stored Fragella payload has no id or name.");
    public static readonly Error ConcentrationUnknown = Error.Validation(
        "Catalog.Fragella.ConcentrationUnknown", "Fragella has no usable OilType for this fragrance. Send 'concentration' in the request.");
    public static readonly Error GenderUnknown = Error.Validation(
        "Catalog.Fragella.GenderUnknown", "Fragella has no usable gender for this fragrance. Send 'gender' in the request.");

    public static Error AlreadyImported(Guid fragranceId) => Error.Conflict(
        "Catalog.Fragella.AlreadyImported", $"This Fragella fragrance is already imported as {fragranceId}.");
}
