using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PerfumeShop.Modules.Catalog.Domain.Common;
using PerfumeShop.Modules.Catalog.Domain.Fragrances;

namespace PerfumeShop.Modules.Catalog.Infrastructure.Configurations;

/// <summary>Value objects that are stored as a single text column.</summary>
internal static class CatalogConverters
{
    public static readonly ValueConverter<Slug, string> Slug =
        new(slug => slug.Value, value => Domain.Common.Slug.Create(value).Value);

    public static readonly ValueConverter<Sku, string> Sku =
        new(sku => sku.Value, value => Domain.Fragrances.Sku.Create(value).Value);

    /// <summary>EF never passes null to a converter; nullability is for the optional column.</summary>
    public static readonly ValueConverter<Ean?, string?> Ean =
        new(ean => ean!.Value, value => Domain.Fragrances.Ean.Create(value).Value);
}
