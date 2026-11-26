using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PerfumeShop.Modules.Orders.Domain;
using PerfumeShop.Modules.Orders.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Modules;

namespace PerfumeShop.Modules.Orders.Features.QuoteCart;

internal sealed record CartItemRequest(Guid VariantId, int Quantity);

/// <param name="CountryCode">Delivery country: XK (default), AL, MK.</param>
internal sealed record QuoteCartRequest(List<CartItemRequest> Items, string? CountryCode);

/// <param name="Problem">NotFound, NotForSale, OutOfStock, NotEnoughStock or QuantityTooHigh; null when the line can be ordered.</param>
internal sealed record CartLineDto(
    Guid VariantId,
    int Quantity,
    string? Sku,
    string? BrandName,
    string? FragranceName,
    string? VariantType,
    decimal? SizeValue,
    string? SizeUnit,
    string? ImageUrl,
    decimal? UnitPrice,
    decimal? LineTotal,
    bool IsAvailable,
    string? Problem);

/// <summary>Everything the storefront shows for a cart. The frontend never calculates prices itself.</summary>
internal sealed record CartQuoteDto(
    IReadOnlyList<CartLineDto> Lines,
    string Currency,
    string CountryCode,
    bool DeliveryAvailable,
    decimal Subtotal,
    decimal DeliveryFee,
    decimal Total,
    decimal VatAmount,
    decimal? FreeDeliveryFrom,
    decimal? AmountUntilFreeDelivery,
    bool CanCheckout,
    IReadOnlyList<string> Problems);

internal sealed class QuoteCartValidator : AbstractValidator<QuoteCartRequest>
{
    public QuoteCartValidator()
    {
        RuleFor(x => x.Items).NotEmpty().Must(i => i.Count <= 50);
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.VariantId).NotEmpty();
            item.RuleFor(i => i.Quantity).InclusiveBetween(1, 100);
        });
        RuleFor(x => x.CountryCode).Length(2).When(x => x.CountryCode is not null);
    }
}

internal sealed class QuoteCartHandler(OrderPricing pricing) : IHandler
{
    public const string DefaultCountry = "XK";

    public async Task<CartQuoteDto> HandleAsync(QuoteCartRequest request, CancellationToken cancellationToken)
    {
        var cart = await pricing.PriceAsync(
            [.. request.Items.Select(i => new CartItem(i.VariantId, i.Quantity))],
            (request.CountryCode ?? DefaultCountry).ToUpperInvariant(),
            cancellationToken);
        return ToDto(cart);
    }

    internal static CartQuoteDto ToDto(PricedCart cart) => new(
        [.. cart.Lines.Select(l => new CartLineDto(
            l.VariantId, l.Quantity, l.Variant?.Sku, l.Variant?.BrandName, l.Variant?.FragranceName, l.Variant?.VariantType,
            l.Variant?.SizeValue, l.Variant?.SizeUnit, l.Variant?.ImageUrl, l.UnitPrice, l.LineTotal, l.IsAvailable, l.Problem))],
        Order.Currency,
        cart.CountryCode,
        cart.DeliveryAvailable,
        cart.Subtotal,
        cart.DeliveryFee,
        cart.Total,
        cart.VatAmount,
        cart.FreeDeliveryFrom,
        cart.AmountUntilFreeDelivery,
        cart.CanCheckout,
        cart.Problems);
}

internal static class QuoteCartEndpoint
{
    /// <summary>Public: guests can see prices before signing in.</summary>
    public static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/api/cart/quote", async (QuoteCartRequest request, QuoteCartHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(request, ct)))
            .AllowAnonymous()
            .AddEndpointFilter<PerfumeShop.Shared.Infrastructure.Http.ValidationFilter<QuoteCartRequest>>()
            .WithTags("Cart")
            .WithName("QuoteCart");
}
