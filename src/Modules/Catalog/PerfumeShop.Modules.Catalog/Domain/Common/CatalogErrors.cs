using PerfumeShop.Modules.Catalog.Domain.Fragrances;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Catalog.Domain.Common;

internal static class SlugErrors
{
    public static readonly Error Empty = new("Catalog.Slug.Empty", "Slug must contain at least one letter or digit.");
    public static readonly Error TooLong = new("Catalog.Slug.TooLong", $"Slug cannot be longer than {Slug.MaxLength} characters.");
    public static readonly Error InvalidSuffix = new("Catalog.Slug.InvalidSuffix", "Slug suffix must be 2 or greater.");
}

internal static class BrandErrors
{
    public static readonly Error NameRequired = new("Catalog.Brand.NameRequired", "Brand name is required.");
    public static readonly Error NameTooLong = new("Catalog.Brand.NameTooLong", "Brand name is too long.");
    public static readonly Error InvalidCountryCode = new("Catalog.Brand.InvalidCountryCode", "Country must be a 2-letter ISO 3166-1 code, e.g. AE.");
    public static readonly Error InvalidLogoUrl = new("Catalog.Brand.InvalidLogoUrl", "Logo URL must be an absolute http(s) URL.");
}

internal static class FragranceErrors
{
    public static readonly Error BrandRequired = new("Catalog.Fragrance.BrandRequired", "Fragrance must belong to a brand.");
    public static readonly Error NameRequired = new("Catalog.Fragrance.NameRequired", "Fragrance name is required.");
    public static readonly Error NameTooLong = new("Catalog.Fragrance.NameTooLong", "Fragrance name is too long.");
    public static readonly Error InvalidConcentration = new("Catalog.Fragrance.InvalidConcentration", "Unknown concentration.");
    public static readonly Error InvalidGender = new("Catalog.Fragrance.InvalidGender", "Unknown gender.");
    public static readonly Error InvalidOlfactiveFamily = new("Catalog.Fragrance.InvalidOlfactiveFamily", "Unknown olfactive family.");
    public static readonly Error InvalidMood = new("Catalog.Fragrance.InvalidMood", "Unknown mood.");
    public static readonly Error InvalidLaunchYear = new("Catalog.Fragrance.InvalidLaunchYear", "Launch year is out of range.");
    public static readonly Error InvalidPerfumer = new("Catalog.Fragrance.InvalidPerfumer", "Perfumer names must be 1-100 characters, max 10 perfumers.");
    public static readonly Error InvalidProfile = new("Catalog.Fragrance.InvalidProfile", "Unknown longevity, sillage, season or occasion.");
    public static readonly Error TooManyAccords = new("Catalog.Fragrance.TooManyAccords", $"A fragrance can have at most {Fragrance.MaxAccords} accords.");
    public static readonly Error DuplicateAccord = new("Catalog.Fragrance.DuplicateAccord", "Each accord can appear only once.");
    public static readonly Error TooManySimilar = new("Catalog.Fragrance.TooManySimilar", $"A fragrance can have at most {Fragrance.MaxSimilarTo} similar references.");
    public static readonly Error DuplicateSimilar = new("Catalog.Fragrance.DuplicateSimilar", "Each similar fragrance can appear only once.");
    public static readonly Error SimilarToItself = new("Catalog.Fragrance.SimilarToItself", "A fragrance cannot be similar to itself.");
    public static readonly Error FragellaIdRequired = new("Catalog.Fragrance.FragellaIdRequired", "Fragella id is required (max 100 characters).");
    public static readonly Error FragellaIdMismatch = Error.Conflict("Catalog.Fragrance.FragellaIdMismatch", "Fragrance is already linked to a different Fragella id.");
    public static readonly Error FragellaRawInvalid = new("Catalog.Fragrance.FragellaRawInvalid", "Fragella payload must be a JSON object.");
    public static readonly Error TimestampNotUtc = new("Catalog.Fragrance.TimestampNotUtc", "Timestamp must be UTC.");
    public static readonly Error PublishRequiresActiveVariant = Error.Conflict("Catalog.Fragrance.PublishRequiresActiveVariant", "A fragrance can be published only with at least one active variant.");
    public static readonly Error LastActiveVariant = Error.Conflict("Catalog.Fragrance.LastActiveVariant", "A published fragrance must keep at least one active variant. Unpublish it first.");
    public static readonly Error VariantNotFound = Error.NotFound("Catalog.Fragrance.VariantNotFound", "Variant not found on this fragrance.");
    public static readonly Error DuplicateSku = Error.Conflict("Catalog.Fragrance.DuplicateSku", "Another variant already uses this SKU.");
    public static readonly Error DuplicateEan = Error.Conflict("Catalog.Fragrance.DuplicateEan", "Another variant already uses this EAN.");
    public static readonly Error DuplicateVariant = Error.Conflict("Catalog.Fragrance.DuplicateVariant", "A variant with the same type and size already exists.");
}

internal static class VariantErrors
{
    public static readonly Error InvalidType = new("Catalog.Variant.InvalidType", "Unknown variant type.");
    public static readonly Error SizeRequired = new("Catalog.Variant.SizeRequired", "Size is required for full bottles, testers and minis.");
    public static readonly Error InvalidSize = new("Catalog.Variant.InvalidSize", "Size must be greater than 0 and at most 1000, with at most one decimal.");
    public static readonly Error InvalidSizeUnit = new("Catalog.Variant.InvalidSizeUnit", "Unknown size unit. Use Ml, Gram or Piece.");
    public static readonly Error PriceMustBePositive = new("Catalog.Variant.PriceMustBePositive", "Price must be greater than 0.");
    public static readonly Error PriceCurrency = new("Catalog.Variant.PriceCurrency", "Prices must be in EUR.");
    public static readonly Error CompareAtPriceTooLow = new("Catalog.Variant.CompareAtPriceTooLow", "Compare-at price must be greater than the price.");
    public static readonly Error ActivationRequiresPrice = new("Catalog.Variant.ActivationRequiresPrice", "A variant cannot be active without a price.");
    public static readonly Error TooManyImages = new("Catalog.Variant.TooManyImages", $"A variant can have at most {FragranceVariant.MaxImages} images.");
    public static readonly Error DuplicateImage = new("Catalog.Variant.DuplicateImage", "The same image URL is used twice.");
    public static readonly Error SkuInvalid = new("Catalog.Variant.SkuInvalid", "SKU must be 2-64 characters: A-Z, 0-9 and single dashes.");
    public static readonly Error EanInvalid = new("Catalog.Variant.EanInvalid", "EAN must be 8, 12, 13 or 14 digits with a valid check digit.");
    public static readonly Error ImageUrlInvalid = new("Catalog.Variant.ImageUrlInvalid", "Image URL must be an absolute http(s) URL.");
    public static readonly Error ImageAltTooLong = new("Catalog.Variant.ImageAltTooLong", "Image alt text is too long.");
    public static readonly Error ImageSortOrderInvalid = new("Catalog.Variant.ImageSortOrderInvalid", "Image sort order cannot be negative.");
}

internal static class ScentErrors
{
    public static readonly Error NoteNameInvalid = new("Catalog.Note.NameInvalid", "Note name must be 1-100 characters.");
    public static readonly Error NoteImageUrlInvalid = new("Catalog.Note.ImageUrlInvalid", "Note image URL must be an absolute http(s) URL.");
    public static readonly Error TooManyNotes = new("Catalog.Note.TooMany", $"A pyramid layer can have at most {NotePyramid.MaxNotesPerLayer} notes.");
    public static readonly Error DuplicateNote = new("Catalog.Note.Duplicate", "A note can appear only once per layer.");
    public static readonly Error AccordNameInvalid = new("Catalog.Accord.NameInvalid", "Accord name must be 1-50 characters.");
    public static readonly Error AccordPercentageInvalid = new("Catalog.Accord.PercentageInvalid", "Accord percentage must be between 1 and 100.");
    public static readonly Error ReferenceInvalid = new("Catalog.SimilarTo.Invalid", "Similar reference needs a brand (max 100) and a name (max 200).");
}
