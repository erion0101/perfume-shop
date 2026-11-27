using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Orders.Domain;
using PerfumeShop.Modules.Orders.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Modules;

namespace PerfumeShop.Modules.Orders.Features.DeliveryOptions;

/// <summary>Where we deliver and what it costs (EUR, VAT incl.), for the shop's banner and delivery page.</summary>
/// <param name="FreeDeliveryFrom">Subtotal from which delivery is free; null: never free.</param>
internal sealed record DeliveryOptionDto(string CountryCode, string CountryName, decimal Fee, decimal? FreeDeliveryFrom, string Currency);

internal sealed class DeliveryOptionsHandler(OrdersDbContext db) : IHandler
{
    /// <summary>Enabled countries only, Kosovo first.</summary>
    public async Task<IReadOnlyList<DeliveryOptionDto>> HandleAsync(CancellationToken cancellationToken)
    {
        var rates = await db.DeliveryRates.AsNoTracking().Where(r => r.IsEnabled).ToListAsync(cancellationToken);
        return [.. rates
            .OrderBy(r => r.CountryCode == "XK" ? 0 : 1)
            .ThenBy(r => r.CountryName)
            .Select(r => new DeliveryOptionDto(r.CountryCode, r.CountryName, r.Fee, r.FreeDeliveryFrom, Order.Currency))];
    }
}

internal static class DeliveryOptionsEndpoint
{
    /// <summary>Public: the storefront shows "falas mbi … €" to every visitor.</summary>
    public static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/cart/delivery-options", async (DeliveryOptionsHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(ct)))
            .AllowAnonymous()
            .WithTags("Cart")
            .WithName("GetDeliveryOptions");
}
