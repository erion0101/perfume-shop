using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Orders.Domain;

namespace PerfumeShop.Modules.Orders.Infrastructure;

/// <summary>
/// Next number for the year, inside the caller's transaction: the counter row stays locked until
/// commit, and a rolled-back order gives its number back, so numbers have no gaps.
/// </summary>
internal sealed class OrderNumbers(OrdersDbContext db)
{
    public async Task<string> NextAsync(int year, CancellationToken cancellationToken)
    {
        var next = await db.Database.SqlQuery<int>($"""
            INSERT INTO orders.order_number_counters (year, last_number) VALUES ({year}, 1)
            ON CONFLICT (year) DO UPDATE SET last_number = order_number_counters.last_number + 1
            RETURNING last_number AS "Value"
            """).ToListAsync(cancellationToken);

        return OrderNumber.Format(year, next.Single());
    }
}
