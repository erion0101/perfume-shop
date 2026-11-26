using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Catalog.Domain.Brands;
using PerfumeShop.Modules.Catalog.Domain.Fragrances;
using PerfumeShop.Modules.Catalog.Infrastructure.Fragella;
using PerfumeShop.Shared.Infrastructure.Persistence;

namespace PerfumeShop.Modules.Catalog.Infrastructure;

internal sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : ModuleDbContext(options)
{
    internal const string SchemaName = "catalog";

    public DbSet<Brand> Brands => Set<Brand>();

    public DbSet<Fragrance> Fragrances => Set<Fragrance>();

    /// <summary>Read side only; variants are changed through <see cref="Fragrance"/>.</summary>
    public IQueryable<FragranceVariant> Variants => Set<FragranceVariant>();

    public DbSet<FragellaUsage> FragellaUsage => Set<FragellaUsage>();

    public DbSet<FragellaSearchCacheEntry> FragellaSearchCache => Set<FragellaSearchCacheEntry>();

    public DbSet<FragellaFragranceSnapshot> FragellaFragrances => Set<FragellaFragranceSnapshot>();

    protected override string Schema => SchemaName;
}
