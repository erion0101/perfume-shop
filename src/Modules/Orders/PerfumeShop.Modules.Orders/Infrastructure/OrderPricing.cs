using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Catalog.Contracts;
using PerfumeShop.Modules.Inventory.Contracts;
using PerfumeShop.Modules.Orders.Domain;

namespace PerfumeShop.Modules.Orders.Infrastructure;

internal sealed record CartItem(Guid VariantId, int Quantity);

internal static class CartProblems
{
    public const string NotFound = "NotFound";
    public const string NotForSale = "NotForSale";
    public const string OutOfStock = "OutOfStock";
    public const string NotEnoughStock = "NotEnoughStock";
    public const string QuantityTooHigh = "QuantityTooHigh";
    public const string DeliveryNotAvailable = "DeliveryNotAvailable";
}

internal sealed record PricedLine(
    Guid VariantId,
    int Quantity,
    VariantSummaryDto? Variant,
    decimal? UnitPrice,
    decimal? LineTotal,
    bool IsAvailable,
    string? Problem);

internal sealed record PricedCart(
    IReadOnlyList<PricedLine> Lines,
    string CountryCode,
    bool DeliveryAvailable,
    decimal Subtotal,
    decimal DeliveryFee,
    decimal? FreeDeliveryFrom,
    IReadOnlyList<string> Problems)
{
    public decimal Total => Subtotal + DeliveryFee;

    public decimal VatAmount => Lines.Where(l => l.IsAvailable).Sum(l => Vat.Of(l.LineTotal!.Value)) + Vat.Of(DeliveryFee);

    public bool CanCheckout => Problems.Count == 0 && Lines.Count > 0;

    public decimal? AmountUntilFreeDelivery =>
        DeliveryAvailable && DeliveryFee > 0 && FreeDeliveryFrom is { } from ? Math.Max(from - Subtotal, 0) : null;
}

/// <summary>
/// The only place prices are calculated: current prices from Catalog, availability from Inventory,
/// delivery from the country's rate. Used by the cart quote and by PlaceOrder.
/// </summary>
internal sealed class OrderPricing(OrdersDbContext db, ICatalogApi catalog, IInventoryApi inventory)
{
    public async Task<PricedCart> PriceAsync(IReadOnlyList<CartItem> requested, string countryCode, CancellationToken cancellationToken)
    {
        // The same variant twice is one line.
        var items = requested.GroupBy(i => i.VariantId).Select(g => new CartItem(g.Key, g.Sum(i => i.Quantity))).ToList();
        var ids = items.Select(i => i.VariantId).ToList();
        var variants = await catalog.GetVariantSummariesAsync(ids, cancellationToken);
        var stock = await inventory.GetAvailabilityAsync(ids, cancellationToken);

        var lines = items.Select(item =>
        {
            var variant = variants.GetValueOrDefault(item.VariantId);
            var available = stock.TryGetValue(item.VariantId, out var s) ? s.Available : 0;
            var problem =
                variant is null ? CartProblems.NotFound
                : !variant.IsPurchasable || variant.PriceAmount is null ? CartProblems.NotForSale
                : item.Quantity > Order.MaxQuantityPerLine ? CartProblems.QuantityTooHigh
                : available <= 0 ? CartProblems.OutOfStock
                : available < item.Quantity ? CartProblems.NotEnoughStock
                : null;
            var price = problem is CartProblems.NotFound or CartProblems.NotForSale ? null : variant!.PriceAmount;
            return new PricedLine(item.VariantId, item.Quantity, variant, price, price * item.Quantity, problem is null, problem);
        }).ToList();

        var subtotal = lines.Where(l => l.IsAvailable).Sum(l => l.LineTotal!.Value);
        var rate = await db.DeliveryRates.AsNoTracking().FirstOrDefaultAsync(r => r.CountryCode == countryCode, cancellationToken);
        var deliveryAvailable = rate is { IsEnabled: true };
        var fee = deliveryAvailable && subtotal > 0 ? rate!.FeeFor(subtotal) : 0m;

        List<string> problems = [.. lines.Where(l => l.Problem is not null).Select(l => $"{l.VariantId}: {l.Problem}")];
        if (!deliveryAvailable)
        {
            problems.Add(CartProblems.DeliveryNotAvailable);
        }

        if (lines.Count > Order.MaxLines)
        {
            problems.Add($"At most {Order.MaxLines} different products per order.");
        }

        return new PricedCart(lines, countryCode, deliveryAvailable, subtotal, fee, rate?.FreeDeliveryFrom, problems);
    }
}
