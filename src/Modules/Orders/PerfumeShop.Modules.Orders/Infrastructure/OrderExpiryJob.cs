using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PerfumeShop.Modules.Inventory.Contracts;
using PerfumeShop.Modules.Orders.Domain;
using PerfumeShop.Shared.Infrastructure.Jobs;

namespace PerfumeShop.Modules.Orders.Infrastructure;

/// <summary>Recurring: Pending orders past their confirmation window expire and their stock is released.</summary>
internal sealed class OrderExpiryJob(OrdersDbContext db, IInventoryApi inventory, TimeProvider timeProvider, ILogger<OrderExpiryJob> logger) : IRecurringJob
{
    public const int BatchSize = 100;

    public async Task RunAsync(CancellationToken cancellationToken) => await ExpireDueOrdersAsync(cancellationToken);

    public async Task<int> ExpireDueOrdersAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var due = await db.Orders.Include(o => o.Lines).Include(o => o.History)
            .Where(o => o.Status == OrderStatus.Pending && o.ExpiresAtUtc <= now)
            .OrderBy(o => o.ExpiresAtUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        var expired = 0;
        foreach (var order in due)
        {
            if (order.Expire(now).IsFailure)
            {
                continue;
            }

            // Release first (idempotent): if saving fails afterwards, the next run simply retries.
            var released = await inventory.ReleaseAsync([.. order.Lines.Select(l => l.Id)], "Order expired (not confirmed in time)", cancellationToken);
            if (released.IsFailure)
            {
                logger.LogWarning("Could not release stock for expiring order {Number}: {Error}", order.Number, released.Error.Message);
                db.Entry(order).State = EntityState.Unchanged;
                continue;
            }

            await db.SaveChangesAsync(cancellationToken);
            expired++;
        }

        return expired;
    }
}
