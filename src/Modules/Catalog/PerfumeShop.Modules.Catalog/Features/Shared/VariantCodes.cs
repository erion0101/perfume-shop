using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Catalog.Domain.Fragrances;
using PerfumeShop.Modules.Catalog.Infrastructure;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Catalog.Features.Shared;

internal static class VariantCodes
{
    /// <summary>SKU and EAN are unique across the whole catalog (the aggregate only sees its own variants).</summary>
    public static async Task<Result> CheckGloballyFreeAsync(CatalogDbContext db, Sku sku, Ean? ean, Guid? exceptVariantId, CancellationToken cancellationToken)
    {
        if (await db.Variants.AnyAsync(v => v.Sku == sku && v.Id != exceptVariantId, cancellationToken))
        {
            return CatalogFeatureErrors.SkuTaken;
        }

        return ean is not null && await db.Variants.AnyAsync(v => v.Ean == ean && v.Id != exceptVariantId, cancellationToken)
            ? CatalogFeatureErrors.EanTaken
            : Result.Success();
    }
}
