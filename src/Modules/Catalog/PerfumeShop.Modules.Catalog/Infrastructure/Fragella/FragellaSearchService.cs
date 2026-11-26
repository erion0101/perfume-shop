using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Catalog.Infrastructure.Fragella;

internal sealed record FragellaSearchOutcome(IReadOnlyList<FragellaFragranceSnapshot> Items, bool FromCache);

internal static class FragellaErrors
{
    public static readonly Error NotConfigured = Error.Unavailable(
        "Catalog.Fragella.NotConfigured", "Fragella API key is not configured (Fragella:ApiKey).");

    public static readonly Error UpstreamFailed = Error.Unavailable(
        "Catalog.Fragella.UpstreamFailed", "Fragella did not answer correctly. Try again later.");

    public static readonly Error ProviderQuotaExceeded = Error.LimitExceeded(
        "Catalog.Fragella.ProviderQuotaExceeded", "Fragella rejected the request: plan quota exceeded.");

    public static Error BudgetExhausted(int limit) => Error.LimitExceeded(
        "Catalog.Fragella.BudgetExhausted", $"Monthly Fragella budget of {limit} requests is used up. Cached searches still work.");
}

/// <summary>
/// Search with a 30-day DB cache in front of Fragella and a monthly budget behind it.
/// Order: cache → budget → API. Every returned fragrance is stored, so import needs no API call.
/// </summary>
internal sealed class FragellaSearchService(
    CatalogDbContext db,
    IFragellaClient client,
    IFragellaBudget budget,
    IOptions<FragellaOptions> options,
    TimeProvider timeProvider,
    ILogger<FragellaSearchService> logger)
{
    public static string NormalizeQuery(string query) =>
        string.Join(' ', query.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    public async Task<Result<FragellaSearchOutcome>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var key = NormalizeQuery(query);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var cached = await db.FragellaSearchCache.FirstOrDefaultAsync(c => c.Query == key, cancellationToken);
        if (cached is not null && cached.ExpiresAtUtc > now)
        {
            return new FragellaSearchOutcome(await LoadSnapshotsAsync(cached.FragellaIds, cancellationToken), FromCache: true);
        }

        if (string.IsNullOrWhiteSpace(options.Value.ApiKey))
        {
            return FragellaErrors.NotConfigured;
        }

        if (!await budget.TryReserveAsync(cancellationToken))
        {
            return FragellaErrors.BudgetExhausted(options.Value.MonthlyRequestLimit);
        }

        FragellaHttpResponse response;
        try
        {
            response = await client.SearchAsync(key, options.Value.SearchResultLimit, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await budget.RefundAsync(CancellationToken.None);
            logger.LogWarning(ex, "Fragella search for {Query} failed", key);
            return FragellaErrors.UpstreamFailed;
        }

        List<FragellaFragranceSnapshot> items;
        if (response.IsSuccess)
        {
            if (!TryParseItems(response.Body, now, out items))
            {
                logger.LogWarning("Fragella returned an unexpected body for {Query}", key);
                return FragellaErrors.UpstreamFailed;
            }
        }
        else
        {
            // Only 2xx responses are billed by Fragella.
            await budget.RefundAsync(CancellationToken.None);

            switch (response.StatusCode)
            {
                case HttpStatusCode.NotFound:
                    items = []; // "no fragrances found": cache the empty answer too
                    break;
                case HttpStatusCode.TooManyRequests:
                    return FragellaErrors.ProviderQuotaExceeded;
                default:
                    logger.LogWarning("Fragella search for {Query} returned {Status}", key, (int)response.StatusCode);
                    return FragellaErrors.UpstreamFailed;
            }
        }

        await UpsertSnapshotsAsync(items, cancellationToken);
        UpsertCacheEntry(cached, key, items, now);
        await db.SaveChangesAsync(cancellationToken);

        return new FragellaSearchOutcome(items, FromCache: false);
    }

    private async Task<IReadOnlyList<FragellaFragranceSnapshot>> LoadSnapshotsAsync(List<string> ids, CancellationToken cancellationToken)
    {
        var snapshots = await db.FragellaFragrances.AsNoTracking()
            .Where(s => ids.Contains(s.FragellaId))
            .ToDictionaryAsync(s => s.FragellaId, cancellationToken);

        return [.. ids.Where(snapshots.ContainsKey).Select(id => snapshots[id])];
    }

    private async Task UpsertSnapshotsAsync(List<FragellaFragranceSnapshot> items, CancellationToken cancellationToken)
    {
        var ids = items.Select(i => i.FragellaId).ToList();
        var existing = await db.FragellaFragrances
            .Where(s => ids.Contains(s.FragellaId))
            .ToDictionaryAsync(s => s.FragellaId, cancellationToken);

        foreach (var item in items)
        {
            if (existing.TryGetValue(item.FragellaId, out var snapshot))
            {
                snapshot.Name = item.Name;
                snapshot.Brand = item.Brand;
                snapshot.Raw = item.Raw;
                snapshot.FetchedAtUtc = item.FetchedAtUtc;
            }
            else
            {
                db.FragellaFragrances.Add(item);
            }
        }
    }

    private void UpsertCacheEntry(FragellaSearchCacheEntry? entry, string key, List<FragellaFragranceSnapshot> items, DateTime now)
    {
        if (entry is null)
        {
            entry = new FragellaSearchCacheEntry { Query = key };
            db.FragellaSearchCache.Add(entry);
        }

        entry.FragellaIds = [.. items.Select(i => i.FragellaId)];
        entry.FetchedAtUtc = now;
        entry.ExpiresAtUtc = now.AddDays(options.Value.SearchCacheDays);
    }

    /// <summary>Accepts a top-level array or a paginated <c>{"data": [...]}</c> object.</summary>
    private static bool TryParseItems(string body, DateTime now, out List<FragellaFragranceSnapshot> items)
    {
        items = [];
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var array = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data) ? data : root;
            if (array.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var element in array.EnumerateArray())
            {
                var id = FragellaJson.String(element, "_id");
                var name = FragellaJson.String(element, "Name");
                if (id is null || name is null || items.Exists(i => i.FragellaId == id))
                {
                    continue;
                }

                items.Add(new FragellaFragranceSnapshot
                {
                    FragellaId = id,
                    Name = name,
                    Brand = FragellaJson.String(element, "Brand") ?? string.Empty,
                    Raw = element.GetRawText(),
                    FetchedAtUtc = now,
                });
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
