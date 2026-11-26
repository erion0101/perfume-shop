using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Orders.Domain;
using PerfumeShop.Modules.Orders.Infrastructure;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Orders.Features.Shared;

internal static class OrderStore
{
    /// <param name="customerId">When set, only that customer's order is found.</param>
    public static async Task<Result<Order>> LoadOrderAsync(this OrdersDbContext db, Guid orderId, CancellationToken cancellationToken, Guid? customerId = null)
    {
        var order = await db.Orders
            .Include(o => o.Lines)
            .Include(o => o.History)
            .Include(o => o.Notes)
            .AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == orderId && (customerId == null || o.CustomerId == customerId), cancellationToken);
        return order is null ? OrderFeatureErrors.OrderNotFound : order;
    }
}
