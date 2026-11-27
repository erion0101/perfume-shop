using Microsoft.EntityFrameworkCore;

namespace PerfumeShop.Modules.Catalog.Infrastructure.Search;

/// <summary>A published fragrance that matches a shop search, best first.</summary>
internal sealed record SearchHit(Guid FragranceId, double Score);

/// <summary>
/// Typo-tolerant shop search over brand + fragrance names ("lattaffa khamra" finds Lattafa Khamrah).
/// v1 is PostgreSQL pg_trgm (<see cref="PostgresTrigramCatalogSearch"/>); Meilisearch can replace it behind this
/// interface without touching the features.
/// </summary>
internal interface ICatalogSearch
{
    /// <summary>Published fragrances only, best match first, at most <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int limit, CancellationToken cancellationToken);

    /// <summary>Brand ids whose name matches, best first (any brand; the caller keeps only brands it lists).</summary>
    Task<IReadOnlyList<Guid>> SearchBrandsAsync(string query, int limit, CancellationToken cancellationToken);
}

internal sealed class PostgresTrigramCatalogSearch(CatalogDbContext db) : ICatalogSearch
{
    public const int MaxQueryLength = 100;

    /// <summary>Lower than pg_trgm's default 0.6 so one or two typos in a short name still match.</summary>
    private const double MinScore = 0.3;

    public static string Normalize(string query)
    {
        var text = string.Join(' ', query.Split((char[])[' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries));
        return text.Length > MaxQueryLength ? text[..MaxQueryLength] : text;
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int limit, CancellationToken cancellationToken)
    {
        var q = Normalize(query);
        if (q.Length < 2)
        {
            return [];
        }

        // Column aliases are snake_case: the module's naming convention also maps raw query results.
        // word_similarity(q, text): best match of q against any part of text, so "khamrah" matches
        // "Lattafa Khamrah" and "lattafa" matches every Lattafa fragrance. Parameters are bound (FormattableString).
        var minScore = MinScore;
        var like = $"%{q.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal)}%";
        var rows = await db.Database.SqlQuery<SearchRow>($"""
            SELECT f.id AS fragrance_id,
                   GREATEST(
                       word_similarity({q}, f.name),
                       word_similarity({q}, b.name),
                       word_similarity({q}, b.name || ' ' || f.name),
                       CASE WHEN (b.name || ' ' || f.name) ILIKE {like} THEN 1.0 ELSE 0 END)::float8 AS score
            FROM catalog.fragrances f
            JOIN catalog.brands b ON b.id = f.brand_id
            WHERE f.status = 'Published'
              AND EXISTS (SELECT 1 FROM catalog.fragrance_variants v WHERE v.fragrance_id = f.id AND v.is_active)
            ORDER BY score DESC, f.name
            LIMIT {limit}
            """).ToListAsync(cancellationToken);

        return [.. rows.Where(r => r.Score >= minScore).Select(r => new SearchHit(r.FragranceId, r.Score))];
    }

    public async Task<IReadOnlyList<Guid>> SearchBrandsAsync(string query, int limit, CancellationToken cancellationToken)
    {
        var q = Normalize(query);
        if (q.Length < 2)
        {
            return [];
        }

        var rows = await db.Database.SqlQuery<BrandRow>($"""
            SELECT b.id AS brand_id, word_similarity({q}, b.name)::float8 AS score
            FROM catalog.brands b
            ORDER BY score DESC, b.name
            LIMIT {limit}
            """).ToListAsync(cancellationToken);
        return [.. rows.Where(r => r.Score >= MinScore).Select(r => r.BrandId)];
    }

    private sealed record SearchRow(Guid FragranceId, double Score);

    private sealed record BrandRow(Guid BrandId, double Score);
}
