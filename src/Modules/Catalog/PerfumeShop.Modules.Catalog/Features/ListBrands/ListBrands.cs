using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Catalog.Features.Shared;
using PerfumeShop.Modules.Catalog.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Modules;

namespace PerfumeShop.Modules.Catalog.Features.ListBrands;

internal sealed record ListBrandsQuery(string? Search);

internal sealed class ListBrandsHandler(CatalogDbContext db) : IHandler
{
    public async Task<IReadOnlyList<BrandDto>> HandleAsync(ListBrandsQuery query, CancellationToken cancellationToken)
    {
        var brands = db.Brands.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            brands = brands.Where(b => b.Name.ToLower().Contains(term));
        }

        var rows = await brands
            .OrderBy(b => b.Name)
            .Select(b => new { Brand = b, Count = db.Fragrances.Count(f => f.BrandId == b.Id) })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(r => r.Brand.ToDto(r.Count))];
    }
}

internal static class ListBrandsEndpoint
{
    public static void Map(RouteGroupBuilder admin) =>
        admin.MapGet("/brands", async ([AsParameters] ListBrandsQuery query, ListBrandsHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(query, ct)))
            .WithName("ListBrands");
}
