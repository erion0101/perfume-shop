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

namespace PerfumeShop.Modules.Inventory.Features.GetStockItem;

/// <param name="Movements">Latest first; at most <see cref="GetStockItemHandler.MovementLimit"/>.</param>
internal sealed record StockItemDetailsDto(
    StockItemDto Stock,
    IReadOnlyList<BatchDto> Batches,
    IReadOnlyList<ReservationDto> OpenReservations,
    IReadOnlyList<MovementDto> Movements);

internal sealed class GetStockItemHandler(InventoryDbContext db, ICatalogApi catalog) : IHandler
{
    public const int MovementLimit = 200;

    public async Task<Result<StockItemDetailsDto>> HandleAsync(Guid variantId, CancellationToken cancellationToken)
    {
        var item = await db.StockItems.AsNoTracking().Include(i => i.Batches).FirstOrDefaultAsync(i => i.Id == variantId, cancellationToken);
        if (item is null)
        {
            return InventoryFeatureErrors.StockItemNotFound;
        }

        var reservations = await db.Reservations.AsNoTracking()
            .Where(r => r.VariantId == variantId && r.Status == ReservationStatus.Reserved)
            .OrderBy(r => r.CreatedAtUtc)
            .Select(r => new ReservationDto(r.OrderId, r.OrderLineId, r.Quantity, r.Status.ToString(), r.ExpiresAtUtc, r.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        var movements = await db.Movements.AsNoTracking()
            .Where(m => m.VariantId == variantId)
            .OrderByDescending(m => m.OccurredAtUtc).ThenByDescending(m => m.Id)
            .Take(MovementLimit)
            .ToListAsync(cancellationToken);

        return new StockItemDetailsDto(
            item.ToDto(await catalog.GetVariantSummaryAsync(variantId, cancellationToken)),
            [.. item.Batches.OrderByDescending(b => b.ReceivedOn).ThenByDescending(b => b.Sequence).Select(b => b.ToDto())],
            reservations,
            [.. movements.Select(m => m.ToDto())]);
    }
}

internal static class GetStockItemEndpoint
{
    public static void Map(RouteGroupBuilder admin) =>
        admin.MapGet("/items/{variantId:guid}", async (Guid variantId, GetStockItemHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(variantId, ct)).ToOk())
            .WithName("GetStockItem");
}
