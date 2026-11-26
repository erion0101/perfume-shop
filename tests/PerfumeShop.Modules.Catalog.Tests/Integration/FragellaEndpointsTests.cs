using System.Net;
using PerfumeShop.Modules.Catalog.Features.Shared;
using PerfumeShop.Modules.Catalog.Infrastructure.Fragella;
using static PerfumeShop.Modules.Catalog.Tests.Integration.HttpTestExtensions;

namespace PerfumeShop.Modules.Catalog.Tests.Integration;

public sealed class FragellaEndpointsTests(CatalogApiFactory factory)
{
    private const string Import = $"{CatalogScenario.Admin}/fragrances/import";
    private readonly HttpClient _admin = factory.CreateAdminClient();

    [Fact]
    public async Task Search_returns_results_and_repeats_come_from_the_cache()
    {
        var query = $"khamrah {Unique()}";

        var first = await CatalogScenario.SearchAsync(_admin, query);
        var second = await CatalogScenario.SearchAsync(_admin, query);

        Assert.False(first.FromCache);
        Assert.True(second.FromCache);
        Assert.Equal(["Khamrah", "Khamrah Qahwa"], first.Items.Select(i => i.Name));
        Assert.Equal("Eau de Parfum", first.Items[0].OilType);
        Assert.Equal(1, factory.Fragella.CallsFor(query));
    }

    [Fact]
    public async Task Search_needs_three_characters()
    {
        var response = await _admin.GetTestAsync($"{CatalogScenario.Admin}/fragella/search?q=ab");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.ValidationErrorsAsync()).TryGetProperty("Q", out _));
    }

    [Fact]
    public async Task Import_creates_a_draft_without_variants_and_a_brand()
    {
        var token = Unique();
        var search = await CatalogScenario.SearchAsync(_admin, $"khamrah {token}");

        var response = await _admin.PostJsonAsync(Import, new { fragellaId = search.Items[0].FragellaId });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var fragrance = await response.ReadAsync<FragranceAdminDto>();
        Assert.Equal("Draft", fragrance.Status);
        Assert.Empty(fragrance.Variants);
        Assert.Equal("Khamrah", fragrance.Name);
        Assert.Equal($"Lattafa {token}", fragrance.Brand.Name);
        Assert.Equal($"lattafa-{token}-khamrah-edp", fragrance.Slug);
        Assert.Equal("EauDeParfum", fragrance.Concentration);
        Assert.Equal("Unisex", fragrance.Gender);
        Assert.Equal(2022, fragrance.LaunchYear);
        Assert.Equal(["Cinnamon", "Nutmeg", "Bergamot"], fragrance.Notes.Top.Select(n => n.Name));
        Assert.Equal("sweet", fragrance.Accords[0].Name);
        Assert.Equal(["Autumn", "Winter"], fragrance.Seasons);
        Assert.Equal($"Lattafa-Khamrah-{token}", fragrance.FragellaId);
        Assert.NotNull(fragrance.FragellaImportedAtUtc);
    }

    [Fact]
    public async Task Import_costs_no_extra_Fragella_call()
    {
        var query = $"khamrah {Unique()}";
        var search = await CatalogScenario.SearchAsync(_admin, query);

        await _admin.PostJsonAsync(Import, new { fragellaId = search.Items[0].FragellaId });

        Assert.Equal(1, factory.Fragella.CallsFor(query));
    }

    [Fact]
    public async Task Imported_fragrance_is_marked_in_later_searches_and_cannot_be_imported_twice()
    {
        var query = $"khamrah {Unique()}";
        var search = await CatalogScenario.SearchAsync(_admin, query);
        var fragrance = await (await _admin.PostJsonAsync(Import, new { fragellaId = search.Items[0].FragellaId })).ReadAsync<FragranceAdminDto>();

        var again = await CatalogScenario.SearchAsync(_admin, query);
        var duplicate = await _admin.PostJsonAsync(Import, new { fragellaId = search.Items[0].FragellaId });

        Assert.Equal(fragrance.Id, again.Items[0].ImportedFragranceId);
        Assert.Null(again.Items[1].ImportedFragranceId);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("Catalog.Fragella.AlreadyImported", await duplicate.ErrorCodeAsync());
    }

    [Fact]
    public async Task Import_of_unsearched_id_is_not_found()
    {
        var response = await _admin.PostJsonAsync(Import, new { fragellaId = $"never-searched-{Unique()}" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Catalog.Fragella.NotSearched", await response.ErrorCodeAsync());
    }

    [Fact]
    public async Task Missing_oil_type_needs_a_concentration_in_the_request()
    {
        var search = await CatalogScenario.SearchAsync(_admin, $"qahwa {Unique()}");
        var qahwa = search.Items[1].FragellaId;

        var withoutConcentration = await _admin.PostJsonAsync(Import, new { fragellaId = qahwa });
        var withConcentration = await _admin.PostJsonAsync(Import, new { fragellaId = qahwa, concentration = "EauDeParfum" });

        Assert.Equal(HttpStatusCode.BadRequest, withoutConcentration.StatusCode);
        Assert.Equal("Catalog.Fragella.ConcentrationUnknown", await withoutConcentration.ErrorCodeAsync());
        Assert.Equal(HttpStatusCode.Created, withConcentration.StatusCode);
        var fragrance = await withConcentration.ReadAsync<FragranceAdminDto>();
        Assert.Equal("Male", fragrance.Gender);
        Assert.Equal(["Coffee", "Cardamom", "Praline", "Vanilla"], fragrance.Notes.Heart.Select(n => n.Name));
    }

    [Fact]
    public async Task Import_reuses_an_existing_brand_with_the_same_name()
    {
        var token = Unique();
        var search = await CatalogScenario.SearchAsync(_admin, $"khamrah {token}");

        var first = await (await _admin.PostJsonAsync(Import, new { fragellaId = search.Items[0].FragellaId })).ReadAsync<FragranceAdminDto>();
        var second = await (await _admin.PostJsonAsync(Import, new { fragellaId = search.Items[1].FragellaId, concentration = "EauDeParfum" })).ReadAsync<FragranceAdminDto>();

        Assert.Equal(first.Brand.Id, second.Brand.Id);
    }

    [Fact]
    public async Task Usage_reports_the_monthly_budget()
    {
        await CatalogScenario.SearchAsync(_admin, $"khamrah {Unique()}");

        var usage = await (await _admin.GetTestAsync($"{CatalogScenario.Admin}/fragella/usage")).ReadAsync<FragellaUsageReport>();

        Assert.Equal(4500, usage.Limit);
        Assert.True(usage.Used >= 1);
    }
}
