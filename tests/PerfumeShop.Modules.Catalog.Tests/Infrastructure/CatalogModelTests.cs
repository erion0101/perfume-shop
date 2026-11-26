using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Catalog.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Persistence;

namespace PerfumeShop.Modules.Catalog.Tests.Infrastructure;

/// <summary>Model checks that need no database connection.</summary>
public sealed class CatalogModelTests
{
    private static CatalogDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        options.UseModuleDefaults("Host=localhost;Database=model-only", CatalogDbContext.SchemaName);
        return new CatalogDbContext(options.Options);
    }

    [Fact]
    public void Migrations_are_up_to_date_with_the_model()
    {
        using var context = CreateContext();

        Assert.False(
            context.Database.HasPendingModelChanges(),
            "The Catalog model changed without a migration. Run: dotnet ef migrations add <Name> --project src/Modules/Catalog/PerfumeShop.Modules.Catalog --startup-project src/Bootstrapper/PerfumeShop.Api --context CatalogDbContext --output-dir Infrastructure/Migrations");
    }

    [Fact]
    public void Every_table_is_in_the_catalog_schema()
    {
        using var context = CreateContext();

        var tables = context.Model.GetEntityTypes().Select(e => (e.GetSchema(), e.GetTableName())).ToArray();

        Assert.Equal(
            [
                ("catalog", "brands"),
                ("catalog", "fragella_fragrances"),
                ("catalog", "fragella_search_cache"),
                ("catalog", "fragella_usage"),
                ("catalog", "fragrance_variants"),
                ("catalog", "fragrances"),
            ],
            tables.Order());
    }
}
