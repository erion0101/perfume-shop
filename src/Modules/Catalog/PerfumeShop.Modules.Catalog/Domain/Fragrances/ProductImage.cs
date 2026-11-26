using System.Text.Json.Serialization;
using PerfumeShop.Modules.Catalog.Domain.Common;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Catalog.Domain.Fragrances;

/// <summary>Photo of a variant (from the distributor, hosted on Cloudflare R2).</summary>
internal sealed record ProductImage
{
    public const int AltTextMaxLength = 200;

    [JsonConstructor]
    private ProductImage(string url, string? altText, int sortOrder)
    {
        Url = url;
        AltText = altText;
        SortOrder = sortOrder;
    }

    public string Url { get; }

    public string? AltText { get; }

    public int SortOrder { get; }

    public static Result<ProductImage> Create(string? url, string? altText, int sortOrder)
    {
        url = url?.Trim();
        if (!HttpUrl.IsValid(url))
        {
            return Result<ProductImage>.Failure(VariantErrors.ImageUrlInvalid);
        }

        altText = string.IsNullOrWhiteSpace(altText) ? null : altText.Trim();
        if (altText?.Length > AltTextMaxLength)
        {
            return Result<ProductImage>.Failure(VariantErrors.ImageAltTooLong);
        }

        return sortOrder < 0
            ? Result<ProductImage>.Failure(VariantErrors.ImageSortOrderInvalid)
            : Result<ProductImage>.Success(new ProductImage(url!, altText, sortOrder));
    }
}
