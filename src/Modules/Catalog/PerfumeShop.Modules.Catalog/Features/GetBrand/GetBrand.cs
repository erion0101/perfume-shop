using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Catalog.Features.Shared;
using PerfumeShop.Modules.Catalog.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Catalog.Features.GetBrand;

internal sealed class GetBrandHandler(CatalogDbContext db) : IHandler
{
    public async Task<Result<BrandDto>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await db.Brands.AsNoTracking()
            .Where(b => b.Id == id)
            .Select(b => new { Brand = b, Count = db.Fragrances.Count(f => f.BrandId == b.Id) })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? CatalogFeatureErrors.BrandNotFound : row.Brand.ToDto(row.Count);
    }
}

internal static class GetBrandEndpoint
{
    public static void Map(RouteGroupBuilder admin) =>
        admin.MapGet("/brands/{id:guid}", async (Guid id, GetBrandHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(id, ct)).ToOk())
            .WithName("GetBrand");
}
