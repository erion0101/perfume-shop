using System.Net;
using Microsoft.Extensions.DependencyInjection;
using PerfumeShop.Modules.Catalog.Features.ListFragrances;
using PerfumeShop.Modules.Catalog.Features.Shared;
using PerfumeShop.Modules.Inventory.Contracts;
using PerfumeShop.Shared.Infrastructure.Http;
using static PerfumeShop.Modules.Catalog.Tests.Integration.CatalogScenario;

namespace PerfumeShop.Modules.Catalog.Tests.Integration;

/// <summary>Catalog shows "in stock" from the real Inventory module; Catalog itself stores no stock.</summary>
public sealed class InStockTests(CatalogApiFactory factory)
{
    private readonly HttpClient _admin = factory.CreateAdminClient();
    private readonly HttpClient _shop = factory.CreateClient();

    private async Task<(FragranceDetailsDto Details, FragranceListItemDto Item)> ShopViewAsync(FragranceAdminDto fragrance)
    {
        var details = await (await _shop.GetTestAsync($"{Shop}/fragrances/{fragrance.Slug}")).ReadAsync<FragranceDetailsDto>();
        var list = await (await _shop.GetTestAsync($"{Shop}/fragrances?brand={fragrance.Brand.Slug}")).ReadAsync<PagedResponse<FragranceListItemDto>>();
        return (details, Assert.Single(list.Items));
    }

    [Fact]
    public async Task Published_fragrance_is_out_of_stock_until_goods_are_received()
    {
        var (fragrance, variant) = await PublishedKhamrahAsync(_admin);

        var before = await ShopViewAsync(fragrance);
        Assert.False(before.Details.InStock);
        Assert.False(before.Details.Variants.Single().InStock);
        Assert.False(before.Item.InStock);

        var receipt = await _admin.PostJsonAsync("/api/admin/inventory/receipts", new
        {
            supplierName = "Lattafa Distribution FZE",
            supplierInvoiceNumber = $"INV-{HttpTestExtensions.Unique()}",
            supplierInvoiceDate = new DateOnly(2026, 11, 18),
            receivedOn = new DateOnly(2026, 11, 20),
            lines = new[] { new { variantId = variant.Id, batchCode = "B2391", quantity = 1, unitCost = 18.50m } },
        });
        Assert.Equal(HttpStatusCode.Created, receipt.StatusCode); // Inventory validated the variant against the real Catalog

        var after = await ShopViewAsync(fragrance);
        Assert.True(after.Details.InStock);
        Assert.True(after.Details.Variants.Single().InStock);
        Assert.True(after.Item.InStock);

        // The last bottle gets reserved by an order → out of stock again.
        await using (var scope = factory.CreateScope())
        {
            var inventory = scope.ServiceProvider.GetRequiredService<IInventoryApi>();
            var reserved = await inventory.ReserveAsync(Guid.NewGuid(), [new(Guid.NewGuid(), variant.Id, 1)], null, TestContext.Current.CancellationToken);
            Assert.True(reserved.IsSuccess);
        }

        Assert.False((await ShopViewAsync(fragrance)).Details.InStock);
    }

    [Fact]
    public async Task Receiving_stock_for_a_variant_that_does_not_exist_in_the_catalog_fails()
    {
        var response = await _admin.PostJsonAsync("/api/admin/inventory/receipts", new
        {
            supplierName = "Unknown",
            supplierInvoiceNumber = "X-1",
            supplierInvoiceDate = new DateOnly(2026, 11, 18),
            receivedOn = new DateOnly(2026, 11, 20),
            lines = new[] { new { variantId = Guid.NewGuid(), batchCode = "B1", quantity = 1, unitCost = 1m } },
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
