using System.Net;
using PerfumeShop.Modules.Catalog.Features.Shared;
using static PerfumeShop.Modules.Catalog.Tests.Integration.HttpTestExtensions;

namespace PerfumeShop.Modules.Catalog.Tests.Integration;

public sealed class BrandEndpointsTests(CatalogApiFactory factory)
{
    private const string Brands = $"{CatalogScenario.Admin}/brands";
    private readonly HttpClient _admin = factory.CreateAdminClient();

    [Fact]
    public async Task Create_get_update_list_delete()
    {
        var name = $"Maison Alhambra {Unique()}";

        var created = await _admin.PostJsonAsync(Brands, new { name, countryCode = "ae", description = "Shtëpi parfumesh nga Dubai." });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var brand = await created.ReadAsync<BrandDto>();
        Assert.Equal("AE", brand.CountryCode);
        Assert.StartsWith("maison-alhambra-", brand.Slug, StringComparison.Ordinal);
        Assert.Equal($"/api/admin/catalog/brands/{brand.Id}", created.Headers.Location!.ToString());

        var fetched = await (await _admin.GetTestAsync($"{Brands}/{brand.Id}")).ReadAsync<BrandDto>();
        Assert.Equal(name, fetched.Name);

        var updated = await _admin.PutJsonAsync($"{Brands}/{brand.Id}", new { name = $"{name} Paris", countryCode = "FR" });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var afterUpdate = await updated.ReadAsync<BrandDto>();
        Assert.Equal(brand.Slug, afterUpdate.Slug); // renaming keeps the URL
        Assert.Equal("FR", afterUpdate.CountryCode);

        var list = await (await _admin.GetTestAsync($"{Brands}?search={Uri.EscapeDataString(name)}")).ReadAsync<List<BrandDto>>();
        Assert.Single(list);

        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteTestAsync($"{Brands}/{brand.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetTestAsync($"{Brands}/{brand.Id}")).StatusCode);
    }

    [Fact]
    public async Task Generated_slug_handles_albanian_letters()
    {
        var token = Unique();
        var brand = await (await _admin.PostJsonAsync(Brands, new { name = $"Parfumeri Çelës {token}" })).ReadAsync<BrandDto>();

        Assert.Equal($"parfumeri-celes-{token}", brand.Slug);
    }

    [Fact]
    public async Task Duplicate_name_is_a_conflict()
    {
        var name = $"Rasasi {Unique()}";
        await _admin.PostJsonAsync(Brands, new { name });

        var second = await _admin.PostJsonAsync(Brands, new { name = name.ToUpperInvariant() });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("Catalog.Brand.NameTaken", await second.ErrorCodeAsync());
    }

    [Fact]
    public async Task Explicit_slug_that_is_taken_is_a_conflict()
    {
        var first = await (await _admin.PostJsonAsync(Brands, new { name = $"Afnan {Unique()}" })).ReadAsync<BrandDto>();

        var second = await _admin.PostJsonAsync(Brands, new { name = $"Armaf {Unique()}", slug = first.Slug });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("Catalog.Slug.Taken", await second.ErrorCodeAsync());
    }

    [Fact]
    public async Task Invalid_request_returns_validation_problem()
    {
        var response = await _admin.PostJsonAsync(Brands, new { name = "", countryCode = "UAE" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var errors = await response.ValidationErrorsAsync();
        Assert.True(errors.TryGetProperty("Name", out _));
        Assert.True(errors.TryGetProperty("CountryCode", out _));
    }

    [Fact]
    public async Task Brand_with_fragrances_cannot_be_deleted()
    {
        var fragrance = await CatalogScenario.ImportKhamrahAsync(_admin);

        var response = await _admin.DeleteTestAsync($"{Brands}/{fragrance.Brand.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Catalog.Brand.HasFragrances", await response.ErrorCodeAsync());
    }

    [Fact]
    public async Task Unknown_brand_is_not_found_problem()
    {
        var response = await _admin.GetTestAsync($"{Brands}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Catalog.Brand.NotFound", await response.ErrorCodeAsync());
    }
}
