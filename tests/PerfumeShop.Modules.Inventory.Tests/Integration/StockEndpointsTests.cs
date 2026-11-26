using System.Net;
using PerfumeShop.Modules.Inventory.Features.GetStockItem;
using PerfumeShop.Modules.Inventory.Features.ReceiveStock;
using PerfumeShop.Modules.Inventory.Features.Shared;
using PerfumeShop.Shared.Infrastructure.Auth;
using PerfumeShop.Shared.Infrastructure.Http;
using Microsoft.Extensions.DependencyInjection;
using PerfumeShop.Testing;

namespace PerfumeShop.Modules.Inventory.Tests.Integration;

public sealed class StockEndpointsTests(InventoryApiFactory factory)
{
    private const string Admin = "/api/admin/inventory";
    private readonly HttpClient _admin = factory.CreateAdminClient();

    private static object Receipt(Guid variantId, string batchCode = "B2391", int quantity = 12, string invoice = "INV-2026-0142") => new
    {
        supplierName = "Lattafa Distribution FZE",
        supplierInvoiceNumber = invoice,
        supplierInvoiceDate = new DateOnly(2026, 11, 18),
        receivedOn = new DateOnly(2026, 11, 20),
        lines = new[] { new { variantId, batchCode, quantity, unitCost = 18.50m } },
    };

    [Fact]
    public async Task Goods_receipt_creates_stock_with_a_traceable_batch()
    {
        var variantId = factory.Catalog.Add("LAT-KHA-100");

        var response = await _admin.PostJson($"{Admin}/receipts", Receipt(variantId));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var receipt = await response.Read<ReceiveStockResponse>();
        Assert.Equal(12, receipt.Batches.Single().OnHandAfter);

        var details = await (await _admin.Get($"{Admin}/items/{variantId}")).Read<StockItemDetailsDto>();
        Assert.Equal((12, 0, 12), (details.Stock.OnHand, details.Stock.Reserved, details.Stock.Available));
        Assert.Equal("LAT-KHA-100", details.Stock.Variant!.Sku);
        Assert.Equal("100 ml", details.Stock.Variant.Size);
        var batch = Assert.Single(details.Batches);
        Assert.Equal(("B2391", "INV-2026-0142", 18.50m), (batch.BatchCode, batch.SupplierInvoiceNumber, batch.UnitCost));
        var movement = Assert.Single(details.Movements);
        Assert.Equal("Receipt", movement.Type);
        Assert.True(Guid.TryParse(movement.Actor, out _), "The ledger records the staff user id.");
    }

    [Fact]
    public async Task One_receipt_can_hold_several_variants_and_batches()
    {
        var first = factory.Catalog.Add();
        var second = factory.Catalog.Add();

        var response = await _admin.PostJson($"{Admin}/receipts", new
        {
            supplierName = "Armaf Trading LLC",
            supplierInvoiceNumber = "AT-77881",
            supplierInvoiceDate = new DateOnly(2026, 11, 10),
            receivedOn = new DateOnly(2026, 11, 12),
            lines = new[]
            {
                new { variantId = first, batchCode = "A1", quantity = 6, unitCost = 15m },
                new { variantId = first, batchCode = "A2", quantity = 4, unitCost = 15m },
                new { variantId = second, batchCode = "C9", quantity = 3, unitCost = 22m },
            },
        });

        var receipt = await response.Read<ReceiveStockResponse>();
        Assert.Equal(3, receipt.Batches.Count);
        Assert.Equal(10, receipt.Batches.Last(b => b.VariantId == first).OnHandAfter);
    }

    [Fact]
    public async Task Unknown_variants_are_rejected()
    {
        var response = await _admin.PostJson($"{Admin}/receipts", Receipt(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Inventory.Variant.NotFound", await response.ErrorCode());
    }

    [Fact]
    public async Task Batch_code_and_invoice_number_are_required()
    {
        var variantId = factory.Catalog.Add();

        var noBatch = await _admin.PostJson($"{Admin}/receipts", Receipt(variantId, batchCode: ""));
        var noInvoice = await _admin.PostJson($"{Admin}/receipts", Receipt(variantId, invoice: ""));

        Assert.Equal(HttpStatusCode.BadRequest, noBatch.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noInvoice.StatusCode);
    }

    [Fact]
    public async Task Adjustments_need_a_reason_and_cannot_remove_reserved_stock()
    {
        var variantId = await factory.VariantWithStockAsync(3);
        await using (var scope = factory.CreateScope())
        {
            var api = scope.ServiceProvider.GetRequiredService<Contracts.IInventoryApi>();
            (await api.ReserveAsync(Guid.NewGuid(), [new(Guid.NewGuid(), variantId, 2)], null, TestContext.Current.CancellationToken)).Ok();
        }

        var noReason = await _admin.PostJson($"{Admin}/items/{variantId}/adjustments", new { quantity = -1, reason = "" });
        var tooMuch = await _admin.PostJson($"{Admin}/items/{variantId}/adjustments", new { quantity = -2, reason = "Broken bottle" });
        var ok = await _admin.PostJson($"{Admin}/items/{variantId}/adjustments", new { quantity = -1, reason = "Broken bottle" });

        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, tooMuch.StatusCode);
        Assert.Equal("Inventory.Stock.Insufficient", await tooMuch.ErrorCode());
        Assert.Equal(0, (await ok.Read<StockItemDto>()).Available);

        var details = await (await _admin.Get($"{Admin}/items/{variantId}")).Read<StockItemDetailsDto>();
        Assert.Equal("Broken bottle", details.Movements.First().Reason);
        Assert.Single(details.OpenReservations);
    }

    [Fact]
    public async Task Low_stock_filter_uses_each_variants_threshold()
    {
        var plenty = await factory.VariantWithStockAsync(50);
        var low = await factory.VariantWithStockAsync(5);
        var setThreshold = await _admin.PutJson($"{Admin}/items/{low}/threshold", new { lowStockThreshold = 5 });
        Assert.Equal(HttpStatusCode.OK, setThreshold.StatusCode);

        var lowStock = await (await _admin.Get($"{Admin}/items?lowStock=true&pageSize=200")).Read<PagedResponse<StockItemDto>>();

        Assert.Contains(lowStock.Items, i => i.VariantId == low && i.IsLowStock);
        Assert.DoesNotContain(lowStock.Items, i => i.VariantId == plenty);
    }

    [Fact]
    public async Task Threshold_can_be_set_before_the_first_receipt()
    {
        var variantId = factory.Catalog.Add();

        var response = await _admin.PutJson($"{Admin}/items/{variantId}/threshold", new { lowStockThreshold = 10 });

        var item = await response.Read<StockItemDto>();
        Assert.Equal((0, 10, true), (item.OnHand, item.LowStockThreshold, item.IsLowStock));
    }

    [Fact]
    public async Task Inventory_is_for_admin_and_staff_only()
    {
        var staff = factory.CreateClientWithToken(ApiFactory.CreateTestToken([Roles.Staff]));
        var customer = factory.CreateClientWithToken(ApiFactory.CreateTestToken([Roles.Customer], customerId: Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.OK, (await staff.Get($"{Admin}/items")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.Get($"{Admin}/items")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().Get($"{Admin}/items")).StatusCode);
    }
}
