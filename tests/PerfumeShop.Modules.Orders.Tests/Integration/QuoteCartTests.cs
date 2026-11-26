using System.Net;
using PerfumeShop.Modules.Orders.Features.QuoteCart;
using static PerfumeShop.Modules.Orders.Tests.Integration.Http;

namespace PerfumeShop.Modules.Orders.Tests.Integration;

public sealed class QuoteCartTests(OrdersApiFactory factory)
{
    private readonly HttpClient _guest = factory.CreateClient();

    private async Task<CartQuoteDto> QuoteAsync(object items, string? countryCode = null)
    {
        var response = await _guest.PostJson("/api/cart/quote", new { items, countryCode });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Read<CartQuoteDto>();
    }

    [Fact]
    public async Task Guests_get_catalog_prices_delivery_fee_and_vat()
    {
        var khamrah = await factory.VariantAsync(19.90m);

        var quote = await QuoteAsync(Items((khamrah, 1)));

        var line = Assert.Single(quote.Lines);
        Assert.Equal((19.90m, 19.90m, true, "Lattafa"), (line.UnitPrice!.Value, line.LineTotal!.Value, line.IsAvailable, line.BrandName));
        Assert.Equal((19.90m, 2.00m, 21.90m), (quote.Subtotal, quote.DeliveryFee, quote.Total));
        Assert.Equal(3.35m, quote.VatAmount); // 19.90 → 3.04, delivery 2.00 → 0.31 (rounded per part)
        Assert.Equal(20.10m, quote.AmountUntilFreeDelivery);
        Assert.True(quote.CanCheckout);
    }

    [Fact]
    public async Task Delivery_is_free_from_40_eur_and_duplicate_lines_are_merged()
    {
        var khamrah = await factory.VariantAsync(20.00m);

        var quote = await QuoteAsync(Items((khamrah, 1), (khamrah, 1)));

        Assert.Equal(2, Assert.Single(quote.Lines).Quantity);
        Assert.Equal((40.00m, 0m, 40.00m), (quote.Subtotal, quote.DeliveryFee, quote.Total));
        Assert.Null(quote.AmountUntilFreeDelivery);
    }

    [Fact]
    public async Task Unavailable_lines_are_flagged_and_block_checkout()
    {
        var scarce = await factory.VariantAsync(30m, 1);
        var notForSale = factory.Catalog.Add(25m, purchasable: false);
        var outOfStock = factory.Catalog.Add(25m);

        var quote = await QuoteAsync(Items((scarce, 2), (notForSale, 1), (outOfStock, 1), (Guid.NewGuid(), 1)));

        Assert.Equal(["NotEnoughStock", "NotForSale", "OutOfStock", "NotFound"], quote.Lines.Select(l => l.Problem));
        Assert.False(quote.CanCheckout);
        Assert.Equal(0m, quote.Subtotal);
    }

    [Fact]
    public async Task Countries_without_delivery_cannot_check_out()
    {
        var khamrah = await factory.VariantAsync();

        var quote = await QuoteAsync(Items((khamrah, 1)), countryCode: "AL");

        Assert.False(quote.DeliveryAvailable);
        Assert.Contains("DeliveryNotAvailable", quote.Problems);
        Assert.False(quote.CanCheckout);
    }
}
