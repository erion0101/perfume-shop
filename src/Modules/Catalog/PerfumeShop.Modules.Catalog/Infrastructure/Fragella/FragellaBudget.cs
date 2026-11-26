using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace PerfumeShop.Modules.Catalog.Infrastructure.Fragella;

internal sealed record FragellaUsageReport(DateOnly Month, int Used, int Limit)
{
    public int Remaining => Math.Max(Limit - Used, 0);
}

/// <summary>Monthly request budget stored in catalog.fragella_usage; safe under concurrent requests.</summary>
internal interface IFragellaBudget
{
    /// <summary>Reserves one request. False when the monthly limit is reached.</summary>
    Task<bool> TryReserveAsync(CancellationToken cancellationToken);

    /// <summary>Gives a reserved request back (Fragella does not bill non-2xx responses).</summary>
    Task RefundAsync(CancellationToken cancellationToken);

    Task<FragellaUsageReport> GetUsageAsync(CancellationToken cancellationToken);
}

internal sealed class FragellaBudget(CatalogDbContext db, IOptions<FragellaOptions> options, TimeProvider timeProvider) : IFragellaBudget
{
    public async Task<bool> TryReserveAsync(CancellationToken cancellationToken)
    {
        var limit = options.Value.MonthlyRequestLimit;
        if (limit <= 0)
        {
            return false;
        }

        var month = CurrentMonth();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // One atomic statement: insert the month row or increment it, but only while below the limit.
        // No row returned = limit reached.
        var counts = await db.Database.SqlQuery<int>($"""
            INSERT INTO catalog.fragella_usage (month, request_count, updated_at_utc)
            VALUES ({month}, 1, {now})
            ON CONFLICT (month) DO UPDATE
                SET request_count = fragella_usage.request_count + 1,
                    updated_at_utc = EXCLUDED.updated_at_utc
                WHERE fragella_usage.request_count < {limit}
            RETURNING request_count AS "Value"
            """).ToListAsync(cancellationToken);

        return counts.Count == 1;
    }

    public async Task RefundAsync(CancellationToken cancellationToken)
    {
        var month = CurrentMonth();
        await db.Database.ExecuteSqlAsync($"""
            UPDATE catalog.fragella_usage
            SET request_count = GREATEST(request_count - 1, 0)
            WHERE month = {month}
            """, cancellationToken);
    }

    public async Task<FragellaUsageReport> GetUsageAsync(CancellationToken cancellationToken)
    {
        var month = CurrentMonth();
        var used = await db.FragellaUsage.AsNoTracking()
            .Where(u => u.Month == month)
            .Select(u => u.RequestCount)
            .FirstOrDefaultAsync(cancellationToken);

        return new FragellaUsageReport(month, used, options.Value.MonthlyRequestLimit);
    }

    private DateOnly CurrentMonth()
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return new DateOnly(now.Year, now.Month, 1);
    }
}
