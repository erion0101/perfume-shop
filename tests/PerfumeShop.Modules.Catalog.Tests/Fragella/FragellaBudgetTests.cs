using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PerfumeShop.Modules.Catalog.Infrastructure;
using PerfumeShop.Modules.Catalog.Infrastructure.Fragella;
using PerfumeShop.Modules.Catalog.Tests.Integration;

namespace PerfumeShop.Modules.Catalog.Tests.Fragella;

/// <summary>Runs against real PostgreSQL because the budget relies on an atomic upsert.</summary>
public sealed class FragellaBudgetTests(CatalogApiFactory factory)
{
    private static FragellaBudget Budget(CatalogDbContext db, int limit, TimeProvider time) =>
        new(db, Options.Create(new FragellaOptions { MonthlyRequestLimit = limit }), time);

    [Fact]
    public async Task Reservations_stop_at_the_monthly_limit()
    {
        await using var scope = factory.CreateScope();
        var budget = Budget(scope.ServiceProvider.GetRequiredService<CatalogDbContext>(), limit: 3, FixedTimeProvider.InUnusedMonth());
        var ct = TestContext.Current.CancellationToken;

        Assert.True(await budget.TryReserveAsync(ct));
        Assert.True(await budget.TryReserveAsync(ct));
        Assert.True(await budget.TryReserveAsync(ct));
        Assert.False(await budget.TryReserveAsync(ct));

        var usage = await budget.GetUsageAsync(ct);
        Assert.Equal(3, usage.Used);
        Assert.Equal(0, usage.Remaining);
    }

    [Fact]
    public async Task Refund_gives_a_request_back()
    {
        await using var scope = factory.CreateScope();
        var budget = Budget(scope.ServiceProvider.GetRequiredService<CatalogDbContext>(), limit: 1, FixedTimeProvider.InUnusedMonth());
        var ct = TestContext.Current.CancellationToken;

        Assert.True(await budget.TryReserveAsync(ct));
        Assert.False(await budget.TryReserveAsync(ct));
        await budget.RefundAsync(ct);

        Assert.True(await budget.TryReserveAsync(ct));
    }

    [Fact]
    public async Task Each_month_has_its_own_budget()
    {
        await using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var time = FixedTimeProvider.InUnusedMonth();
        var ct = TestContext.Current.CancellationToken;

        Assert.True(await Budget(db, 1, time).TryReserveAsync(ct));
        Assert.False(await Budget(db, 1, time).TryReserveAsync(ct));

        time.Now = time.Now.AddMonths(1);
        Assert.True(await Budget(db, 1, time).TryReserveAsync(ct));
    }

    [Fact]
    public async Task Zero_limit_blocks_all_requests()
    {
        await using var scope = factory.CreateScope();
        var budget = Budget(scope.ServiceProvider.GetRequiredService<CatalogDbContext>(), limit: 0, FixedTimeProvider.InUnusedMonth());

        Assert.False(await budget.TryReserveAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Concurrent_reservations_never_exceed_the_limit()
    {
        var time = FixedTimeProvider.InUnusedMonth();

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            await using var scope = factory.CreateScope();
            return await Budget(scope.ServiceProvider.GetRequiredService<CatalogDbContext>(), limit: 5, time)
                .TryReserveAsync(TestContext.Current.CancellationToken);
        }));

        Assert.Equal(5, results.Count(granted => granted));
    }
}
