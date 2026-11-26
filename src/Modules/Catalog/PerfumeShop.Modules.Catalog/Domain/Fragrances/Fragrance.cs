using System.Text.Json;
using PerfumeShop.Modules.Catalog.Domain.Common;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Catalog.Domain.Fragrances;

/// <summary>
/// A fragrance (e.g. Lattafa Khamrah EDP) with its scent profile and sellable variants.
/// Aggregate root: variants are created and changed only through this class.
/// </summary>
internal sealed class Fragrance : AggregateRoot<Guid>, IAuditable
{
    public const int NameMaxLength = 200;
    public const int FragellaIdMaxLength = 100;
    public const int PerfumerMaxLength = 100;
    public const int MaxPerfumers = 10;
    public const int MaxAccords = 20;
    public const int MaxSimilarTo = 10;
    public const int MinLaunchYear = 1900;

    private readonly List<FragranceVariant> _variants = [];
    private List<string> _perfumers = [];

    private Fragrance() { }

    private Fragrance(Guid id, Guid brandId, string name, Slug slug, Concentration concentration, Gender gender) : base(id)
    {
        BrandId = brandId;
        Name = name;
        Slug = slug;
        Concentration = concentration;
        Gender = gender;
        Status = FragranceStatus.Draft;
    }

    public Guid BrandId { get; private set; }

    public string Name { get; private set; } = null!;

    public Slug Slug { get; private set; } = null!;

    public Concentration Concentration { get; private set; }

    public Gender Gender { get; private set; }

    public OlfactiveFamily? OlfactiveFamily { get; private set; }

    public int? LaunchYear { get; private set; }

    public IReadOnlyList<string> Perfumers => _perfumers.AsReadOnly();

    /// <summary>Albanian product description.</summary>
    public string? Description { get; private set; }

    public NotePyramid Notes { get; private set; } = NotePyramid.Empty;

    /// <summary>Sorted by percentage, strongest first.</summary>
    public IReadOnlyList<Accord> Accords { get; private set; } = [];

    public Longevity? Longevity { get; private set; }

    public Sillage? Sillage { get; private set; }

    public Season Seasons { get; private set; }

    public Occasion Occasions { get; private set; }

    public IReadOnlyList<FragranceReference> SimilarTo { get; private set; } = [];

    public FragranceStatus Status { get; private set; }

    public string? FragellaId { get; private set; }

    /// <summary>Raw Fragella response, kept so it can be re-processed without new API calls.</summary>
    public string? FragellaRaw { get; private set; }

    public DateTime? FragellaImportedAtUtc { get; private set; }

    public IReadOnlyList<FragranceVariant> Variants => _variants.AsReadOnly();

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static Result<Fragrance> Create(Guid brandId, string? name, Slug slug, Concentration concentration, Gender gender)
    {
        if (brandId == Guid.Empty)
        {
            return Result<Fragrance>.Failure(FragranceErrors.BrandRequired);
        }

        var validName = ValidateName(name);
        if (validName.IsFailure)
        {
            return Result<Fragrance>.Failure(validName.Error);
        }

        var classification = ValidateClassification(concentration, gender);
        return classification.IsFailure
            ? Result<Fragrance>.Failure(classification.Error)
            : Result<Fragrance>.Success(
                new Fragrance(Guid.CreateVersion7(), brandId, validName.Value, slug, concentration, gender));
    }

    /// <summary>Default slug: "{brand} {name} {concentration}" → "lattafa-khamrah-edp".</summary>
    public static Result<Slug> SuggestSlug(string brandName, string name, Concentration concentration) =>
        Slug.Create($"{brandName} {name} {concentration.Abbreviation()}");

    public Result Rename(string? name, Slug slug)
    {
        var validName = ValidateName(name);
        if (validName.IsFailure)
        {
            return validName;
        }

        Name = validName.Value;
        Slug = slug;
        return Result.Success();
    }

    public Result ChangeClassification(Concentration concentration, Gender gender)
    {
        var result = ValidateClassification(concentration, gender);
        if (result.IsFailure)
        {
            return result;
        }

        Concentration = concentration;
        Gender = gender;
        return Result.Success();
    }

    public Result UpdateDetails(OlfactiveFamily? family, int? launchYear, IEnumerable<string> perfumers, string? description)
    {
        if (family is { } f && !Enum.IsDefined(f))
        {
            return Result.Failure(FragranceErrors.InvalidOlfactiveFamily);
        }

        if (launchYear is { } year && (year < MinLaunchYear || year > DateTime.UtcNow.Year + 1))
        {
            return Result.Failure(FragranceErrors.InvalidLaunchYear);
        }

        List<string> names = [.. perfumers.Select(p => p.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)];
        if (names.Count > MaxPerfumers || names.Any(n => n.Length is 0 or > PerfumerMaxLength))
        {
            return Result.Failure(FragranceErrors.InvalidPerfumer);
        }

        OlfactiveFamily = family;
        LaunchYear = launchYear;
        _perfumers = names;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        return Result.Success();
    }

    public Result UpdateProfile(Longevity? longevity, Sillage? sillage, Season seasons, Occasion occasions)
    {
        if ((longevity is { } l && !Enum.IsDefined(l))
            || (sillage is { } s && !Enum.IsDefined(s))
            || (seasons & ~Season.All) != 0
            || (occasions & ~Occasion.All) != 0)
        {
            return Result.Failure(FragranceErrors.InvalidProfile);
        }

        Longevity = longevity;
        Sillage = sillage;
        Seasons = seasons;
        Occasions = occasions;
        return Result.Success();
    }

    public void SetNotes(NotePyramid notes) => Notes = notes;

    public Result SetAccords(IEnumerable<Accord> accords)
    {
        Accord[] list = [.. accords.OrderByDescending(a => a.Percentage)];

        if (list.Length > MaxAccords)
        {
            return Result.Failure(FragranceErrors.TooManyAccords);
        }

        if (list.DistinctBy(a => a.Name).Count() != list.Length)
        {
            return Result.Failure(FragranceErrors.DuplicateAccord);
        }

        Accords = list;
        return Result.Success();
    }

    public Result SetSimilarTo(IEnumerable<FragranceReference> references)
    {
        FragranceReference[] list = [.. references];

        if (list.Length > MaxSimilarTo)
        {
            return Result.Failure(FragranceErrors.TooManySimilar);
        }

        if (list.Any(r => r.FragranceId == Id))
        {
            return Result.Failure(FragranceErrors.SimilarToItself);
        }

        if (list.DistinctBy(r => (r.BrandName.ToUpperInvariant(), r.Name.ToUpperInvariant())).Count() != list.Length)
        {
            return Result.Failure(FragranceErrors.DuplicateSimilar);
        }

        SimilarTo = list;
        return Result.Success();
    }

    /// <summary>Stores the Fragella id and raw payload. Re-importing the same id refreshes the payload.</summary>
    public Result LinkToFragella(string? fragellaId, string? rawJson, DateTime importedAtUtc)
    {
        fragellaId = fragellaId?.Trim();
        if (string.IsNullOrEmpty(fragellaId) || fragellaId.Length > FragellaIdMaxLength)
        {
            return Result.Failure(FragranceErrors.FragellaIdRequired);
        }

        if (FragellaId is not null && FragellaId != fragellaId)
        {
            return Result.Failure(FragranceErrors.FragellaIdMismatch);
        }

        if (importedAtUtc.Kind != DateTimeKind.Utc)
        {
            return Result.Failure(FragranceErrors.TimestampNotUtc);
        }

        if (!IsJsonObject(rawJson))
        {
            return Result.Failure(FragranceErrors.FragellaRawInvalid);
        }

        FragellaId = fragellaId;
        FragellaRaw = rawJson;
        FragellaImportedAtUtc = importedAtUtc;
        return Result.Success();
    }

    public Result<FragranceVariant> AddVariant(Sku sku, VariantType type, VariantSize? size, Ean? ean)
    {
        var codes = EnsureCodesAreFree(sku, ean, except: null);
        if (codes.IsFailure)
        {
            return Result<FragranceVariant>.Failure(codes.Error);
        }

        if (type.RequiresSize() && _variants.Any(v => v.IsSameOffer(type, size)))
        {
            return Result<FragranceVariant>.Failure(FragranceErrors.DuplicateVariant);
        }

        var variant = FragranceVariant.Create(Id, sku, type, size, ean);
        if (variant.IsSuccess)
        {
            _variants.Add(variant.Value);
        }

        return variant;
    }

    public Result SetVariantPrice(Guid variantId, Money price, Money? compareAtPrice = null) =>
        WithVariant(variantId, v => v.SetPrice(price, compareAtPrice));

    /// <summary>Corrects a SKU or EAN typo. Uniqueness across fragrances is enforced by the database.</summary>
    public Result ChangeVariantCodes(Guid variantId, Sku sku, Ean? ean) =>
        WithVariant(variantId, v =>
        {
            var codes = EnsureCodesAreFree(sku, ean, except: v);
            if (codes.IsFailure)
            {
                return codes;
            }

            v.ChangeCodes(sku, ean);
            return Result.Success();
        });

    public Result SetVariantImages(Guid variantId, IEnumerable<ProductImage> images) =>
        WithVariant(variantId, v => v.SetImages(images));

    public Result ActivateVariant(Guid variantId) => WithVariant(variantId, v => v.Activate());

    public Result DeactivateVariant(Guid variantId) =>
        WithVariant(variantId, v =>
        {
            if (IsLastActiveVariantOfPublished(v))
            {
                return Result.Failure(FragranceErrors.LastActiveVariant);
            }

            v.Deactivate();
            return Result.Success();
        });

    public Result RemoveVariant(Guid variantId) =>
        WithVariant(variantId, v =>
        {
            if (IsLastActiveVariantOfPublished(v))
            {
                return Result.Failure(FragranceErrors.LastActiveVariant);
            }

            _variants.Remove(v);
            return Result.Success();
        });

    public Result Publish()
    {
        if (!_variants.Any(v => v.IsActive))
        {
            return Result.Failure(FragranceErrors.PublishRequiresActiveVariant);
        }

        Status = FragranceStatus.Published;
        return Result.Success();
    }

    public void Unpublish() => Status = FragranceStatus.Draft;

    public bool IsPublished => Status == FragranceStatus.Published;

    private Result WithVariant(Guid variantId, Func<FragranceVariant, Result> action)
    {
        var variant = _variants.Find(v => v.Id == variantId);
        return variant is null ? Result.Failure(FragranceErrors.VariantNotFound) : action(variant);
    }

    private Result EnsureCodesAreFree(Sku sku, Ean? ean, FragranceVariant? except)
    {
        var others = _variants.Where(v => v != except).ToArray();

        if (others.Any(v => v.Sku == sku))
        {
            return Result.Failure(FragranceErrors.DuplicateSku);
        }

        return ean is not null && others.Any(v => v.Ean == ean)
            ? Result.Failure(FragranceErrors.DuplicateEan)
            : Result.Success();
    }

    private bool IsLastActiveVariantOfPublished(FragranceVariant variant) =>
        Status == FragranceStatus.Published && variant.IsActive && _variants.Count(v => v.IsActive) == 1;

    private static Result<string> ValidateName(string? name)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return Result<string>.Failure(FragranceErrors.NameRequired);
        }

        return name.Length > NameMaxLength
            ? Result<string>.Failure(FragranceErrors.NameTooLong)
            : Result<string>.Success(name);
    }

    private static Result ValidateClassification(Concentration concentration, Gender gender)
    {
        if (!Enum.IsDefined(concentration))
        {
            return Result.Failure(FragranceErrors.InvalidConcentration);
        }

        return Enum.IsDefined(gender) ? Result.Success() : Result.Failure(FragranceErrors.InvalidGender);
    }

    private static bool IsJsonObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
