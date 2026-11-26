using System.Net;
using Microsoft.Extensions.DependencyInjection;
using PerfumeShop.Modules.Catalog.Contracts;
using PerfumeShop.Modules.Catalog.Features.ListFragrances;
using PerfumeShop.Modules.Catalog.Features.Shared;
using PerfumeShop.Shared.Infrastructure.Http;
using static PerfumeShop.Modules.Catalog.Tests.Integration.CatalogScenario;
using static PerfumeShop.Modules.Catalog.Tests.Integration.HttpTestExtensions;

namespace PerfumeShop.Modules.Catalog.Tests.Integration;

public sealed class FragranceEndpointsTests(CatalogApiFactory factory)
{
    private readonly HttpClient _admin = factory.CreateAdminClient();
    private readonly HttpClient _shop = factory.CreateClient();

    // ---------- the full admin flow, seen from the storefront ----------

    [Fact]
    public async Task Import_price_activate_publish_then_visible_in_the_shop()
    {
        var fragrance = await ImportKhamrahAsync(_admin);
        var variant = await CreateVariantAsync(_admin, fragrance.Id, new { sku = $"LAT-{Unique()}", type = "FullBottle", size = new { value = 100, unit = "Ml" } });
        var variantUrl = $"{Admin}/fragrances/{fragrance.Id}/variants/{variant.Id}";

        // Not purchasable yet: no price.
        var activateWithoutPrice = await _admin.PostJsonAsync($"{variantUrl}/activate");
        Assert.Equal(HttpStatusCode.BadRequest, activateWithoutPrice.StatusCode);
        Assert.Equal("Catalog.Variant.ActivationRequiresPrice", await activateWithoutPrice.ErrorCodeAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await _shop.GetTestAsync($"{Shop}/fragrances/{fragrance.Slug}")).StatusCode);

        var priced = await _admin.PutJsonAsync(variantUrl, new { sku = variant.Sku, price = 39.90m, compareAtPrice = 49.90m });
        Assert.Equal(HttpStatusCode.OK, priced.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _admin.PostJsonAsync($"{variantUrl}/activate")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _admin.PostJsonAsync($"{Admin}/fragrances/{fragrance.Id}/publish")).StatusCode);

        var page = await _shop.GetTestAsync($"{Shop}/fragrances/{fragrance.Slug}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var details = await page.ReadAsync<FragranceDetailsDto>();
        Assert.Equal(39.90m, details.Variants.Single().Price!.Amount);
        Assert.Equal(49.90m, details.Variants.Single().CompareAtPrice!.Amount);
        Assert.Equal(fragrance.Brand.Slug, details.Brand.Slug);

        // The last active variant of a published fragrance cannot be switched off …
        var deactivate = await _admin.PostJsonAsync($"{variantUrl}/deactivate");
        Assert.Equal(HttpStatusCode.Conflict, deactivate.StatusCode);
        Assert.Equal("Catalog.Fragrance.LastActiveVariant", await deactivate.ErrorCodeAsync());

        // … until the fragrance is unpublished, which hides it from the shop.
        Assert.Equal(HttpStatusCode.OK, (await _admin.PostJsonAsync($"{Admin}/fragrances/{fragrance.Id}/unpublish")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _shop.GetTestAsync($"{Shop}/fragrances/{fragrance.Slug}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _admin.PostJsonAsync($"{variantUrl}/deactivate")).StatusCode);
    }

    [Fact]
    public async Task Publishing_needs_an_active_variant()
    {
        var fragrance = await ImportKhamrahAsync(_admin);

        var response = await _admin.PostJsonAsync($"{Admin}/fragrances/{fragrance.Id}/publish");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Catalog.Fragrance.PublishRequiresActiveVariant", await response.ErrorCodeAsync());
    }

    // ---------- variants ----------

    [Fact]
    public async Task Sku_and_ean_are_unique_across_the_catalog()
    {
        var first = await ImportKhamrahAsync(_admin);
        var second = await ImportKhamrahAsync(_admin);
        var sku = $"DUP-{Unique()}";
        await CreateVariantAsync(_admin, first.Id, new { sku, type = "FullBottle", size = new { value = 100, unit = "Ml" }, ean = "4006381333931" });

        var sameSku = await _admin.PostJsonAsync($"{Admin}/fragrances/{second.Id}/variants", new { sku, type = "Tester", size = new { value = 100, unit = "Ml" } });
        var sameEan = await _admin.PostJsonAsync($"{Admin}/fragrances/{second.Id}/variants", new { sku = $"OTHER-{Unique()}", type = "Tester", size = new { value = 100, unit = "Ml" }, ean = "4006381333931" });

        Assert.Equal("Catalog.Variant.SkuTaken", await sameSku.ErrorCodeAsync());
        Assert.Equal("Catalog.Variant.EanTaken", await sameEan.ErrorCodeAsync());
    }

    [Fact]
    public async Task Bakhoor_variant_is_sold_by_weight()
    {
        var fragrance = await ImportKhamrahAsync(_admin);

        var variant = await CreateVariantAsync(_admin, fragrance.Id, new { sku = $"BAK-{Unique()}", type = "FullBottle", size = new { value = 40, unit = "Gram" }, price = 12.50m });

        Assert.Equal(new SizeDto(40, "Gram"), variant.Size);
        Assert.False(variant.IsActive);
    }

    [Fact]
    public async Task Invalid_variant_requests_are_rejected()
    {
        var fragrance = await ImportKhamrahAsync(_admin);
        var url = $"{Admin}/fragrances/{fragrance.Id}/variants";

        var badPrices = await _admin.PostJsonAsync(url, new { sku = $"X-{Unique()}", type = "FullBottle", size = new { value = 100, unit = "Ml" }, price = 30m, compareAtPrice = 25m });
        var zeroSize = await _admin.PostJsonAsync(url, new { sku = $"X-{Unique()}", type = "FullBottle", size = new { value = 0, unit = "Ml" } });
        var noSize = await _admin.PostJsonAsync(url, new { sku = $"X-{Unique()}", type = "Mini" });
        var badEan = await _admin.PostJsonAsync(url, new { sku = $"X-{Unique()}", type = "FullBottle", size = new { value = 100, unit = "Ml" }, ean = "4006381333932" });

        Assert.True((await badPrices.ValidationErrorsAsync()).TryGetProperty("CompareAtPrice", out _));
        Assert.Equal(HttpStatusCode.BadRequest, zeroSize.StatusCode);
        Assert.Equal("Catalog.Variant.SizeRequired", await noSize.ErrorCodeAsync());
        Assert.Equal("Catalog.Variant.EanInvalid", await badEan.ErrorCodeAsync());
    }

    [Fact]
    public async Task Unknown_variant_is_not_found()
    {
        var fragrance = await ImportKhamrahAsync(_admin);

        var response = await _admin.PostJsonAsync($"{Admin}/fragrances/{fragrance.Id}/variants/{Guid.NewGuid()}/activate");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- details ----------

    [Fact]
    public async Task Update_details_replaces_the_profile()
    {
        var fragrance = await ImportKhamrahAsync(_admin);

        var response = await _admin.PutJsonAsync($"{Admin}/fragrances/{fragrance.Id}", new
        {
            name = "Khamrah",
            concentration = "EauDeParfum",
            gender = "Unisex",
            olfactiveFamily = "Amber",
            launchYear = 2022,
            perfumers = Array.Empty<string>(),
            description = "Parfum i ëmbël oriental me hurma, kanellë dhe vanilje.",
            longevity = "LongLasting",
            sillage = "Strong",
            seasons = new[] { "Autumn", "Winter" },
            occasions = new[] { "Evening" },
            notes = new { top = new[] { new { name = "Cinnamon" } }, heart = new[] { new { name = "Dates" } }, @base = new[] { new { name = "Vanilla" } } },
            accords = new[] { new { name = "amber", percentage = 100 }, new { name = "sweet", percentage = 90 } },
            similarTo = new[] { new { brandName = "Kilian", name = "Angels' Share" } },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.ReadAsync<FragranceAdminDto>();
        Assert.Equal("Parfum i ëmbël oriental me hurma, kanellë dhe vanilje.", updated.Description);
        Assert.Equal("Amber", updated.OlfactiveFamily);
        Assert.Equal(["Evening"], updated.Occasions);
        Assert.Equal(["Dates"], updated.Notes.Heart.Select(n => n.Name));
        Assert.Equal("Kilian", updated.SimilarTo.Single().BrandName);
        Assert.Equal(fragrance.Slug, updated.Slug);
    }

    [Fact]
    public async Task Update_details_reports_domain_rule_violations()
    {
        var fragrance = await ImportKhamrahAsync(_admin);

        var response = await _admin.PutJsonAsync($"{Admin}/fragrances/{fragrance.Id}", new
        {
            name = "Khamrah",
            concentration = "EauDeParfum",
            gender = "Unisex",
            launchYear = 1800,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Catalog.Fragrance.InvalidLaunchYear", await response.ErrorCodeAsync());
    }

    // ---------- storefront listing ----------

    [Fact]
    public async Task Shop_lists_only_published_fragrances_and_filters_them()
    {
        var (published, _) = await PublishedKhamrahAsync(_admin, price: 39.90m);
        var draft = await ImportKhamrahAsync(_admin);
        var brand = published.Brand.Slug;

        var all = await ListAsync($"brand={brand}");
        var draftBrand = await ListAsync($"brand={draft.Brand.Slug}");
        var cheap = await ListAsync($"brand={brand}&maxPrice=30");
        var inRange = await ListAsync($"brand={brand}&minPrice=30&maxPrice=40&gender=unisex");
        var wrongGender = await ListAsync($"brand={brand}&gender=Female");

        var item = Assert.Single(all.Items);
        Assert.Equal(published.Slug, item.Slug);
        Assert.Equal(39.90m, item.FromPrice.Amount);
        Assert.Equal("https://cdn.example.com/khamrah.webp", item.Image!.Url);
        Assert.Equal(3, item.TopAccords.Count);
        Assert.Empty(draftBrand.Items);
        Assert.Empty(cheap.Items);
        Assert.Single(inRange.Items);
        Assert.Empty(wrongGender.Items);
    }

    [Fact]
    public async Task Shop_list_validates_filters()
    {
        var response = await _shop.GetTestAsync($"{Shop}/fragrances?gender=robot&pageSize=500&sort=random");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await response.ValidationErrorsAsync();
        Assert.True(errors.TryGetProperty("Gender", out _));
        Assert.True(errors.TryGetProperty("PageSize", out _));
        Assert.True(errors.TryGetProperty("Sort", out _));
    }

    // ---------- contract for other modules ----------

    [Fact]
    public async Task Catalog_api_returns_variant_price_and_summary()
    {
        var (fragrance, variant) = await PublishedKhamrahAsync(_admin, price: 44.00m);
        await using var scope = factory.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<ICatalogApi>();

        var price = await catalog.GetVariantPriceAsync(variant.Id, TestContext.Current.CancellationToken);
        var summary = await catalog.GetVariantSummaryAsync(variant.Id, TestContext.Current.CancellationToken);

        Assert.Equal(new VariantPriceDto(variant.Id, 44.00m, "EUR", null, IsPurchasable: true), price);
        Assert.NotNull(summary);
        Assert.Equal(fragrance.Brand.Name, summary.BrandName);
        Assert.Equal("Khamrah", summary.FragranceName);
        Assert.Equal("FullBottle", summary.VariantType);
        Assert.Equal(100m, summary.SizeValue);
        Assert.Equal("Ml", summary.SizeUnit);
        Assert.True(summary.IsPurchasable);
        Assert.Equal("https://cdn.example.com/khamrah.webp", summary.ImageUrl);
        Assert.Null(await catalog.GetVariantPriceAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));
    }

    private async Task<PagedResponse<FragranceListItemDto>> ListAsync(string query)
    {
        var response = await _shop.GetTestAsync($"{Shop}/fragrances?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<PagedResponse<FragranceListItemDto>>();
    }
}
