using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Inventory.Domain;

namespace PerfumeShop.Modules.Inventory.Infrastructure;

/// <summary>
/// Releases reservations of unconfirmed orders whose hold has lapsed. Reserving a variant already does
/// this for that variant; a periodic job (Hangfire, see docs/before-production.md) should call this too.
/// </summary>
internal sealed class StockReservationSweeper(InventoryDbContext db, StockTransactions transactions, TimeProvider timeProvider)
{
    public async Task<int> ReleaseExpiredAsync(int batchSize, CancellationToken cancellationToken)
    {
        var released = await transactions.RunAsync<int>(async ct =>
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var expired = await db.Reservations
                .Where(r => r.Status == ReservationStatus.Reserved && r.ExpiresAtUtc <= now)
                .OrderBy(r => r.ExpiresAtUtc)
                .Take(batchSize)
                .ToListAsync(ct);
            if (expired.Count == 0)
            {
                return 0;
            }

            var variantIds = expired.Select(r => r.VariantId).Distinct().ToList();
            var items = await db.StockItems.Include(i => i.Batches).Where(i => variantIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
            foreach (var reservation in expired)
            {
                items[reservation.VariantId].Release(reservation, "Reservation expired", "system", now);
            }

            await db.SaveChangesAsync(ct);
            return expired.Count;
        }, cancellationToken);

        return released.Value;
    }
}
