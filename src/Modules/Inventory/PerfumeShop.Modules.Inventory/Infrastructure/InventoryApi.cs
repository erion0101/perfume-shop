using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Inventory.Contracts;
using PerfumeShop.Modules.Inventory.Domain;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Inventory.Infrastructure;

internal sealed class InventoryApi(InventoryDbContext db, StockTransactions transactions, TimeProvider timeProvider) : IInventoryApi
{
    public const string Actor = "orders";

    public async Task<StockAvailabilityDto> GetAvailabilityAsync(Guid variantId, CancellationToken cancellationToken = default) =>
        (await GetAvailabilityAsync([variantId], cancellationToken))[variantId];

    public async Task<IReadOnlyList<Guid>> GetVariantIdsInStockAsync(CancellationToken cancellationToken = default) =>
        await db.StockItems.AsNoTracking().Where(i => i.OnHand - i.Reserved > 0).Select(i => i.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<LowStockItemDto>> GetLowStockAsync(CancellationToken cancellationToken = default) =>
        await db.StockItems.AsNoTracking()
            .Where(i => i.OnHand - i.Reserved <= i.LowStockThreshold)
            .OrderBy(i => i.OnHand - i.Reserved)
            .ThenBy(i => i.Id)
            .Select(i => new LowStockItemDto(i.Id, i.OnHand - i.Reserved, i.LowStockThreshold))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, StockAvailabilityDto>> GetAvailabilityAsync(IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken = default)
    {
        var ids = variantIds.Distinct().ToList();
        var rows = await db.StockItems.AsNoTracking()
            .Where(i => ids.Contains(i.Id))
            .Select(i => new { i.Id, i.OnHand, i.Reserved, i.LowStockThreshold })
            .ToDictionaryAsync(i => i.Id, cancellationToken);

        return ids.ToDictionary(id => id, id =>
        {
            var available = rows.TryGetValue(id, out var row) ? row.OnHand - row.Reserved : 0;
            return new StockAvailabilityDto(id, available, available > 0, row is not null && available <= row.LowStockThreshold);
        });
    }

    public Task<Result> ReserveAsync(Guid orderId, IReadOnlyList<StockReservationLine> lines, DateTime? expiresAtUtc, CancellationToken cancellationToken = default)
    {
        if (orderId == Guid.Empty || lines.Count == 0 || lines.Any(l => l.OrderLineId == Guid.Empty || l.Quantity < 1)
            || lines.Select(l => l.OrderLineId).Distinct().Count() != lines.Count)
        {
            return Task.FromResult<Result>(Error.Validation("Inventory.Reservation.Invalid", "An order, unique order lines and quantities above 0 are required."));
        }

        return transactions.RunAsync(async ct =>
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var lineIds = lines.Select(l => l.OrderLineId).ToList();
            var existing = await db.Reservations.Where(r => lineIds.Contains(r.OrderLineId)).ToDictionaryAsync(r => r.OrderLineId, ct);

            foreach (var line in lines.Where(l => existing.ContainsKey(l.OrderLineId)))
            {
                var previous = existing[line.OrderLineId];
                if (previous.VariantId != line.VariantId || previous.Quantity != line.Quantity)
                {
                    return InventoryErrors.ReservationMismatch;
                }
            }

            var pending = lines.Where(l => !existing.ContainsKey(l.OrderLineId)).ToList();
            if (pending.Count == 0)
            {
                return Result.Success(); // repeated call
            }

            var variantIds = pending.Select(l => l.VariantId).Distinct().ToList();
            var items = await db.StockItems.Include(i => i.Batches).Where(i => variantIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);

            // Expired holds on these variants are released first, so they never block a real order.
            var expired = await db.Reservations
                .Where(r => variantIds.Contains(r.VariantId) && r.Status == ReservationStatus.Reserved && r.ExpiresAtUtc <= now)
                .ToListAsync(ct);
            foreach (var reservation in expired)
            {
                items[reservation.VariantId].Release(reservation, "Reservation expired", "system", now);
            }

            foreach (var line in pending)
            {
                if (!items.TryGetValue(line.VariantId, out var item))
                {
                    return InventoryErrors.Insufficient(0);
                }

                var reserved = item.Reserve(orderId, line.OrderLineId, line.Quantity, expiresAtUtc, Actor, now);
                if (reserved.IsFailure)
                {
                    return reserved.Error; // nothing is saved: all lines or none
                }

                db.Reservations.Add(reserved.Value);
            }

            await db.SaveChangesAsync(ct);
            return Result.Success();
        }, cancellationToken);
    }

    public async Task<Result> ConfirmAsync(IReadOnlyCollection<Guid> orderLineIds, CancellationToken cancellationToken = default)
    {
        var ids = orderLineIds.Distinct().ToList();
        var reservations = await db.Reservations.Where(r => ids.Contains(r.OrderLineId)).ToListAsync(cancellationToken);
        if (reservations.Count != ids.Count)
        {
            return InventoryErrors.ReservationNotFound;
        }

        if (reservations.Any(r => r.Status is ReservationStatus.Released or ReservationStatus.Returned))
        {
            return InventoryErrors.ReservationReleased;
        }

        reservations.ForEach(r => r.Confirm());
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public Task<Result<IReadOnlyList<CommittedLineDto>>> CommitAsync(IReadOnlyCollection<Guid> orderLineIds, CancellationToken cancellationToken = default) =>
        transactions.RunAsync<IReadOnlyList<CommittedLineDto>>(async ct =>
        {
            var ids = orderLineIds.Distinct().ToList();
            var reservations = await db.Reservations.Include(r => r.Allocations).Where(r => ids.Contains(r.OrderLineId)).ToListAsync(ct);
            if (reservations.Count != ids.Count)
            {
                return InventoryErrors.ReservationNotFound;
            }

            var items = await LoadItemsAsync(reservations, ct);
            var now = timeProvider.GetUtcNow().UtcDateTime;
            foreach (var reservation in reservations)
            {
                var committed = items[reservation.VariantId].Commit(reservation, Actor, now);
                if (committed.IsFailure)
                {
                    return committed.Error;
                }
            }

            await db.SaveChangesAsync(ct);
            return reservations
                .Select(r => new CommittedLineDto(r.OrderLineId, r.VariantId, r.Quantity,
                    [.. r.Allocations.Select(a => new BatchAllocationDto(a.BatchCode, a.Quantity))]))
                .ToList();
        }, cancellationToken);

    public Task<Result> ReleaseAsync(IReadOnlyCollection<Guid> orderLineIds, string reason, CancellationToken cancellationToken = default) =>
        transactions.RunAsync(async ct =>
        {
            var ids = orderLineIds.Distinct().ToList();
            var reservations = await db.Reservations.Include(r => r.Allocations).Where(r => ids.Contains(r.OrderLineId)).ToListAsync(ct);
            if (reservations.Count == 0)
            {
                return Result.Success(); // nothing was reserved for these lines
            }

            var items = await LoadItemsAsync(reservations, ct);
            var now = timeProvider.GetUtcNow().UtcDateTime;
            foreach (var reservation in reservations)
            {
                var released = items[reservation.VariantId].Release(reservation, reason, Actor, now);
                if (released.IsFailure)
                {
                    return released;
                }
            }

            await db.SaveChangesAsync(ct);
            return Result.Success();
        }, cancellationToken);

    private Task<Dictionary<Guid, StockItem>> LoadItemsAsync(IEnumerable<StockReservation> reservations, CancellationToken cancellationToken)
    {
        var variantIds = reservations.Select(r => r.VariantId).Distinct().ToList();
        return db.StockItems.Include(i => i.Batches).Where(i => variantIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, cancellationToken);
    }
}
