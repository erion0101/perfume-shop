using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Catalog.Domain.Brands;
using PerfumeShop.Modules.Catalog.Domain.Common;
using PerfumeShop.Modules.Catalog.Domain.Fragrances;
using PerfumeShop.Modules.Catalog.Infrastructure;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Catalog.Features.Shared;

/// <summary>Loading helpers shared by the admin fragrance slices.</summary>
internal static class FragranceStore
{
    public static async Task<Result<Fragrance>> LoadAsync(this CatalogDbContext db, Guid id, CancellationToken cancellationToken)
    {
        var fragrance = await db.Fragrances.Include(f => f.Variants).FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        return fragrance is null ? CatalogFeatureErrors.FragranceNotFound : fragrance;
    }

    public static async Task<FragranceAdminDto> ToAdminDtoAsync(this CatalogDbContext db, Fragrance fragrance, CancellationToken cancellationToken)
    {
        var brand = await db.Brands.AsNoTracking().FirstAsync(b => b.Id == fragrance.BrandId, cancellationToken);
        return fragrance.ToAdminDto(brand);
    }

    /// <summary>Applies a domain change, saves, and returns the admin view.</summary>
    public static async Task<Result<FragranceAdminDto>> ChangeAsync(
        this CatalogDbContext db,
        Guid id,
        Func<Fragrance, Result> change,
        CancellationToken cancellationToken)
    {
        var fragrance = await db.LoadAsync(id, cancellationToken);
        if (fragrance.IsFailure)
        {
            return fragrance.Error;
        }

        var result = change(fragrance.Value);
        if (result.IsFailure)
        {
            return result.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await db.ToAdminDtoAsync(fragrance.Value, cancellationToken);
    }

    public static Task<bool> BrandSlugTakenAsync(this CatalogDbContext db, Slug slug, Guid? exceptBrandId, CancellationToken cancellationToken) =>
        db.Brands.AnyAsync(b => b.Slug == slug && b.Id != exceptBrandId, cancellationToken);

    public static Task<bool> FragranceSlugTakenAsync(this CatalogDbContext db, Slug slug, Guid? exceptFragranceId, CancellationToken cancellationToken) =>
        db.Fragrances.AnyAsync(f => f.Slug == slug && f.Id != exceptFragranceId, cancellationToken);

    public static Task<Brand?> FindBrandByNameAsync(this CatalogDbContext db, string name, CancellationToken cancellationToken)
    {
        var lowered = name.Trim().ToLowerInvariant();
        return db.Brands.FirstOrDefaultAsync(b => b.Name.ToLower() == lowered, cancellationToken);
    }
}
