using PerfumeShop.Modules.Catalog.Domain.Common;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Catalog.Domain.Fragrances;

/// <summary>
/// What is actually sold: e.g. Khamrah EDP 100 ml full bottle.
/// Part of the <see cref="Fragrance"/> aggregate; change it only through the fragrance.
/// Stock is owned by the Inventory module, not here.
/// </summary>
internal sealed class FragranceVariant : Entity<Guid>, IAuditable
{
    public const int MaxImages = 20;

    private FragranceVariant() { }

    private FragranceVariant(Guid id, Guid fragranceId, Sku sku, VariantType type, VariantSize? size, Ean? ean) : base(id)
    {
        FragranceId = fragranceId;
        Sku = sku;
        Type = type;
        Size = size;
        Ean = ean;
    }

    public Guid FragranceId { get; private set; }

    public Sku Sku { get; private set; } = null!;

    public Ean? Ean { get; private set; }

    public VariantType Type { get; private set; }

    /// <summary>100 ml, 40 g, 12 pieces. Null only for sets.</summary>
    public VariantSize? Size { get; private set; }

    /// <summary>EUR, VAT included. Null until the admin sets it; required before activation.</summary>
    public Money? Price { get; private set; }

    /// <summary>Optional "was" price shown crossed out. Always greater than <see cref="Price"/>.</summary>
    public Money? CompareAtPrice { get; private set; }

    public bool IsActive { get; private set; }

    public IReadOnlyList<ProductImage> Images { get; private set; } = [];

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    internal static Result<FragranceVariant> Create(Guid fragranceId, Sku sku, VariantType type, VariantSize? size, Ean? ean)
    {
        if (!Enum.IsDefined(type))
        {
            return Result<FragranceVariant>.Failure(VariantErrors.InvalidType);
        }

        if (size is null && type.RequiresSize())
        {
            return Result<FragranceVariant>.Failure(VariantErrors.SizeRequired);
        }

        return Result<FragranceVariant>.Success(
            new FragranceVariant(Guid.CreateVersion7(), fragranceId, sku, type, size, ean));
    }

    internal bool IsSameOffer(VariantType type, VariantSize? size) => Type == type && Size == size;

    internal void ChangeCodes(Sku sku, Ean? ean)
    {
        Sku = sku;
        Ean = ean;
    }

    internal Result SetPrice(Money price, Money? compareAtPrice)
    {
        if (price.Currency != Money.Eur || (compareAtPrice is not null && compareAtPrice.Currency != Money.Eur))
        {
            return Result.Failure(VariantErrors.PriceCurrency);
        }

        if (price.Amount <= 0)
        {
            return Result.Failure(VariantErrors.PriceMustBePositive);
        }

        if (compareAtPrice is not null && compareAtPrice.Amount <= price.Amount)
        {
            return Result.Failure(VariantErrors.CompareAtPriceTooLow);
        }

        Price = price;
        CompareAtPrice = compareAtPrice;
        return Result.Success();
    }

    internal Result Activate()
    {
        if (Price is null)
        {
            return Result.Failure(VariantErrors.ActivationRequiresPrice);
        }

        IsActive = true;
        return Result.Success();
    }

    internal void Deactivate() => IsActive = false;

    internal Result SetImages(IEnumerable<ProductImage> images)
    {
        ProductImage[] list = [.. images.OrderBy(image => image.SortOrder)];

        if (list.Length > MaxImages)
        {
            return Result.Failure(VariantErrors.TooManyImages);
        }

        if (list.DistinctBy(image => image.Url, StringComparer.OrdinalIgnoreCase).Count() != list.Length)
        {
            return Result.Failure(VariantErrors.DuplicateImage);
        }

        Images = list;
        return Result.Success();
    }
}
