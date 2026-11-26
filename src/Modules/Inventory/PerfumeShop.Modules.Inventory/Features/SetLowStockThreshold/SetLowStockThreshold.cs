using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Catalog.Contracts;
using PerfumeShop.Modules.Inventory.Domain;
using PerfumeShop.Modules.Inventory.Features.Shared;
using PerfumeShop.Modules.Inventory.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Audit;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Inventory.Features.SetLowStockThreshold;

internal sealed record SetLowStockThresholdRequest(int LowStockThreshold);

internal sealed class SetLowStockThresholdValidator : AbstractValidator<SetLowStockThresholdRequest>
{
    public SetLowStockThresholdValidator() => RuleFor(x => x.LowStockThreshold).InclusiveBetween(0, StockItem.MaxThreshold);
}

/// <summary>Works before the first receipt too (the stock item is created with 0 on hand).</summary>
internal sealed class SetLowStockThresholdHandler(InventoryDbContext db, StockTransactions transactions, ICatalogApi catalog) : IHandler
{
    public async Task<Result<StockItemDto>> HandleAsync(Guid variantId, SetLowStockThresholdRequest request, CancellationToken cancellationToken)
    {
        var variant = await catalog.GetVariantSummaryAsync(variantId, cancellationToken);
        if (variant is null)
        {
            return InventoryFeatureErrors.UnknownVariants([variantId]);
        }

        var updated = await transactions.RunAsync<StockItem>(async ct =>
        {
            var item = await db.StockItems.FirstOrDefaultAsync(i => i.Id == variantId, ct);
            if (item is null)
            {
                var created = StockItem.Create(variantId, request.LowStockThreshold);
                if (created.IsFailure)
                {
                    return created.Error;
                }

                item = created.Value;
                db.StockItems.Add(item);
            }
            else
            {
                var set = item.SetLowStockThreshold(request.LowStockThreshold);
                if (set.IsFailure)
                {
                    return set.Error;
                }
            }

            await db.SaveChangesAsync(ct);
            return item;
        }, cancellationToken);

        return updated.IsSuccess ? updated.Value.ToDto(variant) : updated.Error;
    }
}

internal static class SetLowStockThresholdEndpoint
{
    public static void Map(RouteGroupBuilder admin) =>
        admin.MapPut("/items/{variantId:guid}/threshold",
                async (Guid variantId, SetLowStockThresholdRequest request, SetLowStockThresholdHandler handler, CancellationToken ct) =>
                    (await handler.HandleAsync(variantId, request, ct)).ToOk())
            .WithValidation<SetLowStockThresholdRequest>()
            .Audited("Inventory.Stock.ThresholdChanged", "StockItem", "Kufiri i stokut u ndryshua")
            .WithName("SetLowStockThreshold");
}
