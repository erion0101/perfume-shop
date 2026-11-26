using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Catalog.Features.Shared;
using PerfumeShop.Modules.Catalog.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Audit;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Catalog.Features.DeleteBrand;

internal sealed class DeleteBrandHandler(CatalogDbContext db) : IHandler
{
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var brand = await db.Brands.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (brand is null)
        {
            return CatalogFeatureErrors.BrandNotFound;
        }

        if (await db.Fragrances.AnyAsync(f => f.BrandId == id, cancellationToken))
        {
            return CatalogFeatureErrors.BrandHasFragrances;
        }

        db.Brands.Remove(brand);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal static class DeleteBrandEndpoint
{
    public static void Map(RouteGroupBuilder admin) =>
        admin.MapDelete("/brands/{id:guid}", async (Guid id, DeleteBrandHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(id, ct)).ToNoContent())
            .Audited("Catalog.Brand.Deleted", "Brand", "Marka u fshi")
            .WithName("DeleteBrand");
}
