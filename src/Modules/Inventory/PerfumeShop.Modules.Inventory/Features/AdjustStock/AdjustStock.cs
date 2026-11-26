using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Catalog.Contracts;
using PerfumeShop.Modules.Inventory.Domain;
using PerfumeShop.Modules.Inventory.Features.Shared;
using PerfumeShop.Modules.Inventory.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Inventory.Features.AdjustStock;

/// <param name="Quantity">Negative removes units (breakage, loss), positive puts units back into <paramref name="BatchId"/>.</param>
/// <param name="BatchId">Required when adding; optional when removing (oldest batch first otherwise).</param>
internal sealed record AdjustStockRequest(int Quantity, string Reason, Guid? BatchId);

internal sealed class AdjustStockValidator : AbstractValidator<AdjustStockRequest>
{
    public AdjustStockValidator()
    {
        RuleFor(x => x.Quantity).NotEqual(0).InclusiveBetween(-StockItem.MaxQuantity, StockItem.MaxQuantity);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(StockItem.ReasonMaxLength);
    }
}

internal sealed class AdjustStockHandler(InventoryDbContext db, StockTransactions transactions, ICatalogApi catalog, TimeProvider timeProvider) : IHandler
{
    public async Task<Result<StockItemDto>> HandleAsync(Guid variantId, AdjustStockRequest request, string actor, CancellationToken cancellationToken)
    {
        var adjusted = await transactions.RunAsync<StockItem>(async ct =>
        {
            var item = await db.StockItems.Include(i => i.Batches).FirstOrDefaultAsync(i => i.Id == variantId, ct);
            if (item is null)
            {
                return InventoryFeatureErrors.StockItemNotFound;
            }

            var result = item.Adjust(request.Quantity, request.Reason, request.BatchId, actor, timeProvider.GetUtcNow().UtcDateTime);
            if (result.IsFailure)
            {
                return result.Error;
            }

            await db.SaveChangesAsync(ct);
            return item;
        }, cancellationToken);

        return adjusted.IsSuccess
            ? adjusted.Value.ToDto(await catalog.GetVariantSummaryAsync(variantId, cancellationToken))
            : adjusted.Error;
    }
}

internal static class AdjustStockEndpoint
{
    public static void Map(RouteGroupBuilder admin) =>
        admin.MapPost("/items/{variantId:guid}/adjustments",
                async (Guid variantId, AdjustStockRequest request, ClaimsPrincipal user, AdjustStockHandler handler, CancellationToken ct) =>
                    (await handler.HandleAsync(variantId, request, user.Actor(), ct)).ToOk())
            .WithValidation<AdjustStockRequest>()
            .WithName("AdjustStock");
}
