using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using PerfumeShop.Modules.Catalog.Features.ListFragrancesAdmin;
using PerfumeShop.Modules.Catalog.Features.UploadImage;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Testing;
using static PerfumeShop.Modules.Catalog.Tests.Integration.CatalogScenario;

namespace PerfumeShop.Modules.Catalog.Tests.Integration;

/// <summary>API support for the admin panel: in-stock filter, image upload, strict JSON numbers.</summary>
public sealed class AdminPanelSupportTests(CatalogApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private readonly HttpClient _admin = factory.CreateAdminClient();

    [Fact]
    public async Task Admin_list_filters_by_stock()
    {
        var (fragrance, variant) = await PublishedKhamrahAsync(_admin);
        var url = $"{Admin}/fragrances?search={Uri.EscapeDataString(fragrance.Name)}&pageSize=100";

        Assert.DoesNotContain((await (await _admin.GetTestAsync(url + "&inStock=true")).ReadAsync<PagedResponse<FragranceAdminListItemDto>>()).Items, i => i.Id == fragrance.Id);
        var outOfStock = (await (await _admin.GetTestAsync(url + "&inStock=false")).ReadAsync<PagedResponse<FragranceAdminListItemDto>>()).Items.Single(i => i.Id == fragrance.Id);
        Assert.False(outOfStock.InStock);

        (await _admin.PostJsonAsync("/api/admin/inventory/receipts", new
        {
            supplierName = "Lattafa Distribution FZE",
            supplierInvoiceNumber = $"INV-{HttpTestExtensions.Unique()}",
            supplierInvoiceDate = new DateOnly(2026, 11, 18),
            receivedOn = new DateOnly(2026, 11, 20),
            lines = new[] { new { variantId = variant.Id, batchCode = "B1", quantity = 2, unitCost = 18.50m } },
        })).EnsureSuccessStatusCode();

        var inStock = (await (await _admin.GetTestAsync(url + "&inStock=true")).ReadAsync<PagedResponse<FragranceAdminListItemDto>>()).Items.Single(i => i.Id == fragrance.Id);
        Assert.True(inStock.InStock);
        Assert.DoesNotContain((await (await _admin.GetTestAsync(url + "&inStock=false")).ReadAsync<PagedResponse<FragranceAdminListItemDto>>()).Items, i => i.Id == fragrance.Id);
    }

    [Fact]
    public async Task Admin_lists_sort_on_the_server()
    {
        await ImportKhamrahAsync(_admin);
        await ImportKhamrahAsync(_admin);

        var byName = (await (await _admin.GetTestAsync($"{Admin}/fragrances?sort=name&pageSize=100")).ReadAsync<PagedResponse<FragranceAdminListItemDto>>()).Items;
        var byNameDesc = (await (await _admin.GetTestAsync($"{Admin}/fragrances?sort=-name&pageSize=100")).ReadAsync<PagedResponse<FragranceAdminListItemDto>>()).Items;

        Assert.Equal(byName.Select(i => i.Name).Order(StringComparer.Ordinal), byName.Select(i => i.Name), StringComparer.Ordinal);
        Assert.Equal(byNameDesc.Select(i => i.Name).OrderDescending(StringComparer.Ordinal), byNameDesc.Select(i => i.Name), StringComparer.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.GetTestAsync($"{Admin}/fragrances?sort=price")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _admin.GetTestAsync("/api/admin/inventory/items?sort=-available")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.GetTestAsync("/api/admin/inventory/items?sort=price")).StatusCode);
    }

    [Fact]
    public async Task Price_changes_are_audited_with_before_and_after()
    {
        var (fragrance, variant) = await PublishedKhamrahAsync(_admin, price: 39.90m);

        var updated = await _admin.PutJsonAsync($"{Admin}/fragrances/{fragrance.Id}/variants/{variant.Id}", new { sku = variant.Sku, price = 42.90m });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var audit = System.Text.Json.Nodes.JsonNode.Parse(await (await _admin.GetTestAsync($"/api/admin/audit?action=Catalog.Variant.PriceChanged&entityId={variant.Id}")).Content.ReadAsStringAsync(Ct))!;
        var entry = audit["items"]![0]!;
        Assert.EndsWith("çmimi 39.90 → 42.90 EUR", entry["summary"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(ApiFactory.AdminEmail, entry["actorEmail"]!.GetValue<string>());
    }

    [Fact]
    public async Task Numbers_sent_as_strings_are_rejected()
    {
        var fragrance = await ImportKhamrahAsync(_admin);

        var response = await _admin.PostAsync($"{Admin}/fragrances/{fragrance.Id}/variants",
            new StringContent("""{"sku":"STR-1","type":"FullBottle","size":{"value":"100","unit":"Ml"},"price":"39.90"}""", System.Text.Encoding.UTF8, "application/json"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Image_upload_is_unavailable_outside_development()
    {
        var response = await _admin.PostAsync($"{Admin}/images", Upload(Png, "a.png"), Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Catalog.Image.StorageNotConfigured", await response.ErrorCodeAsync());
    }

    [Fact]
    public async Task Development_stores_uploaded_images_and_serves_them()
    {
        await using var host = new DevelopmentHost(factory.ConnectionString);
        await host.StartAsync();
        var admin = host.CreateAdminClient();

        var uploaded = await admin.PostAsync($"{Admin}/images", Upload(Png, "photo.PNG"), Ct);
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        var image = (await uploaded.Content.ReadFromJsonAsync<UploadedImageDto>(ApiFactory.Json, Ct))!;
        Assert.Matches(@"^http://localhost/media/catalog/\d{4}/[0-9a-f]{32}\.png$", image.Url);
        Assert.Equal(("image/png", Png.LongLength), (image.ContentType, image.Bytes));

        var served = await host.CreateClient().GetAsync(new Uri(image.Url).PathAndQuery, Ct);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("image/png", served.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Png, await served.Content.ReadAsByteArrayAsync(Ct));

        // The URL is accepted by the variant (the domain requires absolute image URLs).
        var fragrance = await ImportKhamrahAsync(_admin); // the test host has the fake Fragella client; same database
        var created = await admin.PostJsonAsync($"{Admin}/fragrances/{fragrance.Id}/variants", new
        {
            sku = $"IMG-{HttpTestExtensions.Unique()}",
            type = "FullBottle",
            size = new { value = 100, unit = "Ml" },
            price = 39.90m,
            images = new[] { new { url = image.Url, altText = "Shishja", sortOrder = 0 } },
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        // Not an image (renamed text file), empty, traversal attempts.
        Assert.Equal("Catalog.Image.Invalid", await (await admin.PostAsync($"{Admin}/images", Upload("hello"u8.ToArray(), "x.png"), Ct)).ErrorCodeAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await host.CreateClient().GetAsync("/media/catalog/2026/..%2F..%2Fappsettings.json", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.CreateClient().PostAsync($"{Admin}/images", Upload(Png, "a.png"), Ct)).StatusCode);
    }

    private static MultipartFormDataContent Upload(byte[] bytes, string name)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png"); // declared type is ignored: the bytes decide
        return new MultipartFormDataContent { { file, "file", name } };
    }

    private sealed class DevelopmentHost(string connectionString) : ApiFactory(connectionString)
    {
        protected override string EnvironmentName => "Development";

        protected override void ConfigureSettings(IDictionary<string, string?> settings) =>
            settings["Catalog:Images:LocalPath"] = Path.Combine(Path.GetTempPath(), "perfumeshop-test-media");
    }
}
