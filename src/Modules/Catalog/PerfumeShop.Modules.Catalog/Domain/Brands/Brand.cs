using PerfumeShop.Modules.Catalog.Domain.Common;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Catalog.Domain.Brands;

/// <summary>A perfume house, e.g. Lattafa, Armaf, Maison Alhambra.</summary>
internal sealed class Brand : AggregateRoot<Guid>, IAuditable
{
    public const int NameMaxLength = 100;

    private Brand() { }

    private Brand(Guid id, string name, Slug slug) : base(id)
    {
        Name = name;
        Slug = slug;
    }

    public string Name { get; private set; } = null!;

    public Slug Slug { get; private set; } = null!;

    /// <summary>ISO 3166-1 alpha-2, e.g. "AE".</summary>
    public string? CountryCode { get; private set; }

    public string? LogoUrl { get; private set; }

    /// <summary>Albanian description shown on the brand page.</summary>
    public string? Description { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static Result<Brand> Create(string? name, Slug slug)
    {
        var validName = ValidateName(name);
        return validName.IsFailure
            ? Result<Brand>.Failure(validName.Error)
            : Result<Brand>.Success(new Brand(Guid.CreateVersion7(), validName.Value, slug));
    }

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

    public Result UpdateDetails(string? countryCode, string? logoUrl, string? description)
    {
        countryCode = string.IsNullOrWhiteSpace(countryCode) ? null : countryCode.Trim().ToUpperInvariant();
        if (countryCode is not null && (countryCode.Length != 2 || !countryCode.All(char.IsAsciiLetterUpper)))
        {
            return Result.Failure(BrandErrors.InvalidCountryCode);
        }

        logoUrl = string.IsNullOrWhiteSpace(logoUrl) ? null : logoUrl.Trim();
        if (logoUrl is not null && !HttpUrl.IsValid(logoUrl))
        {
            return Result.Failure(BrandErrors.InvalidLogoUrl);
        }

        CountryCode = countryCode;
        LogoUrl = logoUrl;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        return Result.Success();
    }

    private static Result<string> ValidateName(string? name)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return Result<string>.Failure(BrandErrors.NameRequired);
        }

        return name.Length > NameMaxLength
            ? Result<string>.Failure(BrandErrors.NameTooLong)
            : Result<string>.Success(name);
    }
}
