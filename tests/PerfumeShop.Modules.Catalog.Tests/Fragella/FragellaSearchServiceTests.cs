using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PerfumeShop.Modules.Catalog.Infrastructure;
using PerfumeShop.Modules.Catalog.Infrastructure.Fragella;
using PerfumeShop.Modules.Catalog.Tests.Integration;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Catalog.Tests.Fragella;

public sealed class FragellaSearchServiceTests(CatalogApiFactory factory)
{
    private sealed record Setup(FragellaSearchService Service, FakeFragellaClient Client, FragellaBudget Budget, FixedTimeProvider Time, CatalogDbContext Db);

    private static Setup Create(AsyncServiceScope scope, int limit = 100, string? apiKey = "test-key", FixedTimeProvider? time = null)
    {
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        time ??= FixedTimeProvider.InUnusedMonth();
        var options = Options.Create(new FragellaOptions { ApiKey = apiKey, MonthlyRequestLimit = limit, SearchCacheDays = 30 });
        var budget = new FragellaBudget(db, options, time);
        var client = new FakeFragellaClient();
        return new Setup(new FragellaSearchService(db, client, budget, options, time, NullLogger<FragellaSearchService>.Instance), client, budget, time, db);
    }

    private static string Query() => $"khamrah {HttpTestExtensions.Unique()}";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task First_search_calls_Fragella_and_stores_every_result()
    {
        await using var scope = factory.CreateScope();
        var s = Create(scope);
        var query = Query();

        var result = await s.Service.SearchAsync(query, Ct);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.FromCache);
        Assert.Equal(2, result.Value.Items.Count); // the entry without "_id" is skipped
        Assert.Equal(1, s.Client.CallsFor(query));
        Assert.Equal(1, (await s.Budget.GetUsageAsync(Ct)).Used);

        var ids = result.Value.Items.Select(i => i.FragellaId).ToList();
        Assert.Equal(2, await s.Db.FragellaFragrances.CountAsync(f => ids.Contains(f.FragellaId), Ct));
    }

    [Fact]
    public async Task Same_search_within_30_days_comes_from_the_cache()
    {
        await using var scope = factory.CreateScope();
        var s = Create(scope);
        var query = Query();

        await s.Service.SearchAsync(query, Ct);
        var second = await s.Service.SearchAsync($"  {query.ToUpperInvariant()}  ", Ct);
        Assert.Equal(1, (await s.Budget.GetUsageAsync(Ct)).Used);

        s.Time.Now = s.Time.Now.AddDays(29);
        var third = await s.Service.SearchAsync(query, Ct);

        Assert.True(second.Value.FromCache);
        Assert.True(third.Value.FromCache);
        Assert.Equal(2, third.Value.Items.Count);
        Assert.Equal(1, s.Client.CallsFor(query));
    }

    [Fact]
    public async Task Expired_cache_calls_Fragella_again()
    {
        await using var scope = factory.CreateScope();
        var s = Create(scope);
        var query = Query();

        await s.Service.SearchAsync(query, Ct);
        s.Time.Now = s.Time.Now.AddDays(31);
        var again = await s.Service.SearchAsync(query, Ct);

        Assert.False(again.Value.FromCache);
        Assert.Equal(2, s.Client.CallsFor(query));
    }

    [Fact]
    public async Task Exhausted_budget_blocks_the_call_but_cached_searches_still_work()
    {
        await using var scope = factory.CreateScope();
        var s = Create(scope, limit: 1);
        var cachedQuery = Query();
        await s.Service.SearchAsync(cachedQuery, Ct);

        var blocked = await s.Service.SearchAsync(Query(), Ct);
        var cached = await s.Service.SearchAsync(cachedQuery, Ct);

        Assert.Equal(ErrorType.LimitExceeded, blocked.Error.Type);
        Assert.Equal("Catalog.Fragella.BudgetExhausted", blocked.Error.Code);
        Assert.Single(s.Client.Queries);
        Assert.True(cached.Value.FromCache);
    }

    [Fact]
    public async Task Not_found_is_cached_as_empty_and_not_billed()
    {
        await using var scope = factory.CreateScope();
        var s = Create(scope);
        s.Client.Respond = _ => new FragellaHttpResponse(HttpStatusCode.NotFound, """{"error":"No fragrances found"}""");
        var query = Query();

        var first = await s.Service.SearchAsync(query, Ct);
        var second = await s.Service.SearchAsync(query, Ct);

        Assert.Empty(first.Value.Items);
        Assert.True(second.Value.FromCache);
        Assert.Equal(0, (await s.Budget.GetUsageAsync(Ct)).Used);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "Catalog.Fragella.ProviderQuotaExceeded")]
    [InlineData(HttpStatusCode.InternalServerError, "Catalog.Fragella.UpstreamFailed")]
    [InlineData(HttpStatusCode.Forbidden, "Catalog.Fragella.UpstreamFailed")]
    public async Task Failed_calls_are_refunded_and_not_cached(HttpStatusCode status, string code)
    {
        await using var scope = factory.CreateScope();
        var s = Create(scope);
        s.Client.Respond = _ => new FragellaHttpResponse(status, "{}");
        var query = Query();

        var result = await s.Service.SearchAsync(query, Ct);
        await s.Service.SearchAsync(query, Ct);

        Assert.Equal(code, result.Error.Code);
        Assert.Equal(0, (await s.Budget.GetUsageAsync(Ct)).Used);
        Assert.Equal(2, s.Client.CallsFor(query));
    }

    [Fact]
    public async Task Network_failure_is_refunded()
    {
        await using var scope = factory.CreateScope();
        var s = Create(scope);
        s.Client.Respond = _ => throw new HttpRequestException("connection refused");

        var result = await s.Service.SearchAsync(Query(), Ct);

        Assert.Equal("Catalog.Fragella.UpstreamFailed", result.Error.Code);
        Assert.Equal(0, (await s.Budget.GetUsageAsync(Ct)).Used);
    }

    [Fact]
    public async Task Missing_api_key_fails_without_using_budget()
    {
        await using var scope = factory.CreateScope();
        var s = Create(scope, apiKey: null);

        var result = await s.Service.SearchAsync(Query(), Ct);

        Assert.Equal("Catalog.Fragella.NotConfigured", result.Error.Code);
        Assert.Empty(s.Client.Queries);
    }
}
