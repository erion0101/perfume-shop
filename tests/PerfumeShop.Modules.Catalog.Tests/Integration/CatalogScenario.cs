using System.Net;
using System.Text.Json;
using PerfumeShop.Modules.Catalog.Features.SearchFragella;
using PerfumeShop.Modules.Catalog.Features.Shared;

namespace PerfumeShop.Modules.Catalog.Tests.Integration;

internal static class CatalogScenario
{
    public const string Admin = "/api/admin/catalog";
    public const string Shop = "/api/catalog";

    /// <summary>Search Fragella (fake) with a unique token and import the first result (Khamrah, EDP).</summary>
    public static async Task<FragranceAdminDto> ImportKhamrahAsync(HttpClient admin)
    {
        var search = await SearchAsync(admin, $"khamrah {HttpTestExtensions.Unique()}");
        var response = await admin.PostJsonAsync($"{Admin}/fragrances/import", new { fragellaId = search.Items[0].FragellaId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<FragranceAdminDto>();
    }

    public static async Task<SearchFragellaResponse> SearchAsync(HttpClient admin, string query)
    {
        var response = await admin.GetTestAsync($"{Admin}/fragella/search?q={Uri.EscapeDataString(query)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<SearchFragellaResponse>();
    }

    public static async Task<VariantDto> CreateVariantAsync(HttpClient admin, Guid fragranceId, object body)
    {
        var response = await admin.PostJsonAsync($"{Admin}/fragrances/{fragranceId}/variants", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<VariantDto>();
    }

    /// <summary>Imported, one active 100 ml bottle at 39.90 EUR, published.</summary>
    public static async Task<(FragranceAdminDto Fragrance, VariantDto Variant)> PublishedKhamrahAsync(HttpClient admin, decimal price = 39.90m)
    {
        var fragrance = await ImportKhamrahAsync(admin);
        var variant = await CreateVariantAsync(admin, fragrance.Id, new
        {
            sku = $"LAT-KHA-{HttpTestExtensions.Unique()}",
            type = "FullBottle",
            size = new { value = 100, unit = "Ml" },
            price,
            images = new[] { new { url = "https://cdn.example.com/khamrah.webp", altText = "Khamrah 100 ml", sortOrder = 0 } },
        });

        Assert.Equal(HttpStatusCode.OK, (await admin.PostJsonAsync($"{Admin}/fragrances/{fragrance.Id}/variants/{variant.Id}/activate")).StatusCode);
        var published = await admin.PostJsonAsync($"{Admin}/fragrances/{fragrance.Id}/publish");
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        return (await published.ReadAsync<FragranceAdminDto>(), variant);
    }

    /// <summary>The "code" extension of a ProblemDetails response.</summary>
    public static async Task<string?> ErrorCodeAsync(this HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    public static async Task<JsonElement> ValidationErrorsAsync(this HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return document.RootElement.GetProperty("errors").Clone();
    }
}
