using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Catalog.Domain.Common;
using PerfumeShop.Modules.Catalog.Domain.Fragrances;
using PerfumeShop.Modules.Catalog.Features.Shared;
using PerfumeShop.Modules.Catalog.Infrastructure;
using PerfumeShop.Modules.Inventory.Contracts;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Catalog.Features.GetFragranceBySlug;

internal sealed class GetFragranceBySlugHandler(CatalogDbContext db, IInventoryApi inventory) : IHandler
{
    /// <summary>Drafts and fragrances without an active variant are reported as not found.</summary>
    public async Task<Result<FragranceDetailsDto>> HandleAsync(string slugText, CancellationToken cancellationToken)
    {
        var slug = Slug.Create(slugText);
        if (slug.IsFailure || slug.Value.Value != slugText)
        {
            return CatalogFeatureErrors.FragranceNotFound;
        }

        var fragrance = await db.Fragrances.AsNoTracking()
            .Include(f => f.Variants.Where(v => v.IsActive))
            .Where(f => f.Slug == slug.Value && f.Status == FragranceStatus.Published && f.Variants.Any(v => v.IsActive))
            .FirstOrDefaultAsync(cancellationToken);
        if (fragrance is null)
        {
            return CatalogFeatureErrors.FragranceNotFound;
        }

        var brand = await db.Brands.AsNoTracking().FirstAsync(b => b.Id == fragrance.BrandId, cancellationToken);
        var stock = await inventory.GetAvailabilityAsync([.. fragrance.Variants.Select(v => v.Id)], cancellationToken);
        return fragrance.ToDetailsDto(brand, stock);
    }
}

internal static class GetFragranceBySlugEndpoint
{
    public static void Map(RouteGroupBuilder shop) =>
        shop.MapGet("/fragrances/{slug}", async (string slug, GetFragranceBySlugHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(slug, ct)).ToOk())
            .WithName("GetFragranceBySlug");
}
