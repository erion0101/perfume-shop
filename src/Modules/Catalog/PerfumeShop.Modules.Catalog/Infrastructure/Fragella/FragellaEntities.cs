namespace PerfumeShop.Modules.Catalog.Infrastructure.Fragella;

/// <summary>Requests made to Fragella in one calendar month (UTC).</summary>
internal sealed class FragellaUsage
{
    public DateOnly Month { get; set; }

    public int RequestCount { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>A search query and the Fragella ids it returned; reused until <see cref="ExpiresAtUtc"/>.</summary>
internal sealed class FragellaSearchCacheEntry
{
    public required string Query { get; set; }

    public List<string> FragellaIds { get; set; } = [];

    public DateTime FetchedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }
}

/// <summary>Local copy of one Fragella fragrance (raw JSON). Import reads from here; no extra API call.</summary>
internal sealed class FragellaFragranceSnapshot
{
    public required string FragellaId { get; set; }

    public required string Name { get; set; }

    public required string Brand { get; set; }

    public required string Raw { get; set; }

    public DateTime FetchedAtUtc { get; set; }
}
