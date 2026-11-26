namespace PerfumeShop.Modules.Catalog.Tests;

public sealed class CatalogModuleTests
{
    [Fact]
    public void Module_name_matches_its_database_schema()
    {
        Assert.Equal("catalog", new CatalogModule().Name);
    }
}
