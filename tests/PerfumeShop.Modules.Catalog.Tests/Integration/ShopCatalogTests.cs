using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PerfumeShop.Modules.Catalog.Features.ListFragrances;
using PerfumeShop.Modules.Catalog.Features.Shared;
using PerfumeShop.Modules.Catalog.Features.Shop;
using PerfumeShop.Modules.Inventory.Contracts;
using PerfumeShop.Shared.Infrastructure.Http;
using static PerfumeShop.Modules.Catalog.Tests.Integration.CatalogScenario;

namespace PerfumeShop.Modules.Catalog.Tests.Integration;

/// <summary>The public endpoints the storefront uses: brands, filters, search, facets, similar fragrances.</summary>
public sealed class ShopCatalogTests(CatalogApiFactory factory)
{
    private readonly HttpClient _admin = factory.CreateAdminClient();
    private readonly HttpClient _shop = factory.CreateClient();

    private async Task<PagedResponse<FragranceListItemDto>> ListAsync(string query) =>
        await (await _shop.GetTestAsync($"{Shop}/fragrances?{query}")).ReadAsync<PagedResponse<FragranceListItemDto>>();

    /// <summary>Replaces the profile of an imported fragrance (same body the admin edit page sends).</summary>
    private async Task<FragranceAdminDto> SetProfileAsync(FragranceAdminDto fragrance, (string Name, int Percentage)[] accords, string[] notes, string? moodOverride = null, object[]? similarTo = null)
    {
        var response = await _admin.PutJsonAsync($"{Admin}/fragrances/{fragrance.Id}", new
        {
            name = fragrance.Name,
            concentration = fragrance.Concentration,
            gender = fragrance.Gender,
            notes = new { top = notes.Select(n => new { name = n }), heart = Array.Empty<object>(), @base = Array.Empty<object>() },
            accords = accords.Select(a => new { name = a.Name, percentage = a.Percentage }),
            similarTo = similarTo ?? [],
            moodOverride,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<FragranceAdminDto>();
    }

    private async Task ReceiveAsync(Guid variantId)
    {
        var receipt = await _admin.PostJsonAsync("/api/admin/inventory/receipts", new
        {
            supplierName = "Lattafa Distribution FZE",
            supplierInvoiceNumber = $"INV-{HttpTestExtensions.Unique()}",
            supplierInvoiceDate = new DateOnly(2026, 11, 18),
            receivedOn = new DateOnly(2026, 11, 20),
            lines = new[] { new { variantId, batchCode = $"B{HttpTestExtensions.Unique()}", quantity = 3, unitCost = 18.50m } },
        });
        Assert.Equal(HttpStatusCode.Created, receipt.StatusCode);
    }

    [Fact]
    public async Task Mood_is_derived_from_accords_and_admin_can_override_it()
    {
        var (fragrance, _) = await PublishedKhamrahAsync(_admin);
        Assert.Equal("Sweet", fragrance.Mood); // imported Khamrah: sweet, warm spicy, amber, vanilla
        Assert.Null(fragrance.MoodOverride);

        var overridden = await SetProfileAsync(fragrance, [("sweet", 100)], ["Vanilla"], moodOverride: "Oud");
        Assert.Equal(("Oud", "Oud"), (overridden.MoodOverride, overridden.Mood));
        var details = await (await _shop.GetTestAsync($"{Shop}/fragrances/{fragrance.Slug}")).ReadAsync<FragranceDetailsDto>();
        Assert.Equal("Oud", details.Mood);

        var invalid = await _admin.PutJsonAsync($"{Admin}/fragrances/{fragrance.Id}", new { name = fragrance.Name, concentration = "EauDeParfum", gender = "Unisex", moodOverride = "Salty" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Listing_filters_by_mood_note_and_stock()
    {
        var (sweet, sweetVariant) = await PublishedKhamrahAsync(_admin);
        var (fresh, _) = await PublishedKhamrahAsync(_admin);
        await SetProfileAsync(sweet, [("sweet", 100), ("vanilla", 80)], ["Vanilla", "Dates"]);
        await SetProfileAsync(fresh, [("citrus", 100), ("fresh", 90)], ["Bergamot"]);
        await ReceiveAsync(sweetVariant.Id);

        var brands = $"brand={sweet.Brand.Slug}";
        Assert.Equal([sweet.Id], (await ListAsync($"{brands}&mood=Sweet")).Items.Select(i => i.Id));
        Assert.Equal("Sweet", (await ListAsync($"{brands}&mood=sweet")).Items.Single().Mood);
        Assert.Empty((await ListAsync($"{brands}&mood=Fresh")).Items);
        Assert.Equal([fresh.Id], (await ListAsync($"brand={fresh.Brand.Slug}&mood=Fresh")).Items.Select(i => i.Id));

        Assert.Equal([sweet.Id], (await ListAsync($"{brands}&note=VANILLA")).Items.Select(i => i.Id));
        Assert.Empty((await ListAsync($"{brands}&note=bergamot")).Items);

        Assert.Equal([sweet.Id], (await ListAsync($"{brands}&inStock=true")).Items.Select(i => i.Id));
        var card = (await ListAsync(brands)).Items.Single();
        Assert.Equal((sweetVariant.Id, true, 100m, "Ml", 1), (card.DefaultVariant.Id, card.DefaultVariant.InStock, card.DefaultVariant.Size!.Value, card.DefaultVariant.Size.Unit, card.VariantCount));
        Assert.Empty((await ListAsync($"brand={fresh.Brand.Slug}&inStock=true")).Items);
        Assert.Single((await ListAsync($"brand={fresh.Brand.Slug}&inStock=false")).Items);

        Assert.Equal(HttpStatusCode.BadRequest, (await _shop.GetTestAsync($"{Shop}/fragrances?mood=Salty")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _shop.GetTestAsync($"{Shop}/fragrances?sort=random")).StatusCode);
    }

    [Fact]
    public async Task Search_tolerates_typos_and_ranks_by_relevance()
    {
        var (fragrance, _) = await PublishedKhamrahAsync(_admin);
        var brand = fragrance.Brand.Name; // "Lattafa {token}", unique per test

        var exact = await ListAsync($"q={Uri.EscapeDataString($"{brand} Khamrah")}");
        Assert.Equal(fragrance.Id, exact.Items[0].Id);

        // Two typos in the name, brand token exact: still found and first.
        var typo = await ListAsync($"q={Uri.EscapeDataString($"{brand.Split(' ')[1]} Kamrha")}");
        Assert.Equal(fragrance.Id, typo.Items[0].Id);

        Assert.Empty((await ListAsync("q=zzqxw")).Items);
        Assert.Equal(HttpStatusCode.BadRequest, (await _shop.GetTestAsync($"{Shop}/fragrances?q={new string('a', 101)}")).StatusCode);
    }

    [Fact]
    public async Task Suggestions_return_fragrances_and_brands_for_a_misspelled_query()
    {
        var (fragrance, _) = await PublishedKhamrahAsync(_admin);
        var token = fragrance.Brand.Name.Split(' ')[1];

        var suggestions = await (await _shop.GetTestAsync($"{Shop}/search/suggestions?q={Uri.EscapeDataString($"Latafa {token}")}")).ReadAsync<SearchSuggestionsDto>();

        Assert.Contains(suggestions.Fragrances, f => f.Id == fragrance.Id);
        Assert.Contains(suggestions.Brands, b => b.Slug == fragrance.Brand.Slug);
        Assert.True(suggestions.Fragrances.Count <= ShopCatalogHandler.SuggestedFragrances);

        var tooShort = await (await _shop.GetTestAsync($"{Shop}/search/suggestions?q=l")).ReadAsync<SearchSuggestionsDto>();
        Assert.Empty(tooShort.Fragrances);
    }

    [Fact]
    public async Task Brands_are_listed_only_with_something_to_sell()
    {
        var (fragrance, _) = await PublishedKhamrahAsync(_admin);
        var draft = await ImportKhamrahAsync(_admin); // its brand has only a draft

        var brands = await (await _shop.GetTestAsync($"{Shop}/brands")).ReadAsync<IReadOnlyList<ShopBrandDto>>();
        Assert.Contains(brands, b => b.Slug == fragrance.Brand.Slug && b.FragranceCount == 1);
        Assert.DoesNotContain(brands, b => b.Slug == draft.Brand.Slug);

        var page = await (await _shop.GetTestAsync($"{Shop}/brands/{fragrance.Brand.Slug}")).ReadAsync<ShopBrandDetailsDto>();
        Assert.Equal((fragrance.Brand.Name, 1), (page.Name, page.FragranceCount));
        Assert.Equal(HttpStatusCode.NotFound, (await _shop.GetTestAsync($"{Shop}/brands/{draft.Brand.Slug}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _shop.GetTestAsync($"{Shop}/brands/no-such-brand")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _shop.GetTestAsync($"{Shop}/brands/Lattafa")).StatusCode); // not canonical
    }

    [Fact]
    public async Task Facets_count_listed_fragrances()
    {
        var (fragrance, _) = await PublishedKhamrahAsync(_admin);
        await SetProfileAsync(fragrance, [("oud", 100)], ["Agarwood"]);

        var facets = await (await _shop.GetTestAsync($"{Shop}/facets")).ReadAsync<ShopFacetsDto>();

        Assert.Contains(facets.Brands, b => b.Value == fragrance.Brand.Slug && b.Label == fragrance.Brand.Name && b.Count == 1);
        Assert.Contains(facets.Moods, m => m.Value == "Oud" && m.Count >= 1);
        Assert.Contains(facets.Notes, n => n.Value == "agarwood");
        Assert.True(facets.MinPrice <= 39.90m && facets.MaxPrice >= 39.90m);
        Assert.True(facets.Total >= 1);
    }

    [Fact]
    public async Task Similar_puts_linked_fragrances_first_then_shared_accords()
    {
        var (source, _) = await PublishedKhamrahAsync(_admin);
        var (linked, _) = await PublishedKhamrahAsync(_admin);
        var (alike, _) = await PublishedKhamrahAsync(_admin);
        await SetProfileAsync(linked, [("citrus", 100)], ["Lemon"]);
        await SetProfileAsync(alike, [("oud", 100), ("smoky", 90)], ["Agarwood"]);
        await SetProfileAsync(source, [("oud", 100), ("smoky", 80)], ["Agarwood"],
            similarTo: [new { brandName = linked.Brand.Name, name = linked.Name, fragranceId = linked.Id }]);

        var similar = await (await _shop.GetTestAsync($"{Shop}/fragrances/{source.Slug}/similar?limit=12")).ReadAsync<IReadOnlyList<FragranceListItemDto>>();

        Assert.Equal(linked.Id, similar[0].Id);
        Assert.Contains(similar, s => s.Id == alike.Id);
        Assert.DoesNotContain(similar, s => s.Id == source.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await _shop.GetTestAsync($"{Shop}/fragrances/no-such-fragrance/similar")).StatusCode);
    }
}
