using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Catalog.Domain.Common;
using PerfumeShop.Modules.Catalog.Domain.Fragrances;
using PerfumeShop.Modules.Catalog.Features.ListFragrances;
using PerfumeShop.Modules.Catalog.Features.Shared;
using PerfumeShop.Modules.Catalog.Infrastructure;
using PerfumeShop.Modules.Catalog.Infrastructure.Search;
using PerfumeShop.Modules.Inventory.Contracts;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;

namespace PerfumeShop.Modules.Catalog.Features.Shop;

/// <summary>A brand on the shop's brand list: only brands with at least one listed fragrance.</summary>
internal sealed record ShopBrandDto(string Name, string Slug, string? CountryCode, string? LogoUrl, int FragranceCount);

/// <summary>Brand page (/markat/{slug}).</summary>
internal sealed record ShopBrandDetailsDto(string Name, string Slug, string? CountryCode, string? LogoUrl, string? Description, int FragranceCount, DateTime UpdatedAtUtc);

internal sealed record FacetDto(string Value, string Label, int Count);

/// <summary>Filter options for the listing, with how many listed fragrances each one has.</summary>
/// <param name="Notes">The most common notes (lower-case keys as imported, English).</param>
internal sealed record ShopFacetsDto(
    IReadOnlyList<FacetDto> Brands,
    IReadOnlyList<FacetDto> Moods,
    IReadOnlyList<FacetDto> Genders,
    IReadOnlyList<FacetDto> Families,
    IReadOnlyList<FacetDto> Notes,
    decimal? MinPrice,
    decimal? MaxPrice,
    int Total);

internal sealed record SearchSuggestionsDto(string Query, IReadOnlyList<FragranceListItemDto> Fragrances, IReadOnlyList<ShopBrandDto> Brands);

internal sealed class ShopCatalogHandler(CatalogDbContext db, IInventoryApi inventory, ICatalogSearch search) : IHandler
{
    public const int MaxNoteFacets = 40;
    public const int SuggestedFragrances = 6;
    public const int SuggestedBrands = 3;
    public const int MaxSimilar = 12;

    public async Task<IReadOnlyList<ShopBrandDto>> ListBrandsAsync(CancellationToken cancellationToken)
    {
        var counts = await ListFragrancesHandler.Listed(db)
            .GroupBy(x => x.F.BrandId)
            .Select(g => new { BrandId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.BrandId, x => x.Count, cancellationToken);

        var brands = await db.Brands.AsNoTracking().Where(b => counts.Keys.Contains(b.Id)).OrderBy(b => b.Name).ToListAsync(cancellationToken);
        return [.. brands.Select(b => new ShopBrandDto(b.Name, b.Slug.Value, b.CountryCode, b.LogoUrl, counts[b.Id]))];
    }

    public async Task<ShopBrandDetailsDto?> GetBrandAsync(string slugText, CancellationToken cancellationToken)
    {
        var slug = Slug.Create(slugText);
        if (slug.IsFailure || slug.Value.Value != slugText)
        {
            return null;
        }

        var brand = await db.Brands.AsNoTracking().FirstOrDefaultAsync(b => b.Slug == slug.Value, cancellationToken);
        if (brand is null)
        {
            return null;
        }

        var count = await ListFragrancesHandler.Listed(db).CountAsync(x => x.F.BrandId == brand.Id, cancellationToken);
        return count == 0
            ? null // no empty brand pages in the shop (or in search engines)
            : new ShopBrandDetailsDto(brand.Name, brand.Slug.Value, brand.CountryCode, brand.LogoUrl, brand.Description, count, brand.UpdatedAtUtc);
    }

    public async Task<ShopFacetsDto> GetFacetsAsync(CancellationToken cancellationToken)
    {
        var rows = await ListFragrancesHandler.Listed(db)
            .Select(x => new
            {
                BrandName = x.B.Name,
                BrandSlug = x.B.Slug,
                x.F.Mood,
                x.F.Gender,
                x.F.OlfactiveFamily,
                Notes = EF.Property<List<string>>(x.F, "_noteKeys"),
                MinPrice = x.F.Variants.Where(v => v.IsActive).Min(v => v.Price!.Amount),
                MaxPrice = x.F.Variants.Where(v => v.IsActive).Max(v => v.Price!.Amount),
            })
            .ToListAsync(cancellationToken);

        static IReadOnlyList<FacetDto> Facets<T>(IEnumerable<T> values, Func<T, string> value, Func<T, string> label) =>
            [.. values.GroupBy(value).Select(g => new FacetDto(g.Key, label(g.First()), g.Count())).OrderByDescending(f => f.Count).ThenBy(f => f.Label)];

        return new ShopFacetsDto(
            [.. Facets(rows, r => r.BrandSlug.Value, r => r.BrandName).OrderBy(f => f.Label)],
            Facets(rows.Where(r => r.Mood is not null), r => r.Mood!.Value.ToString(), r => r.Mood!.Value.ToString()),
            Facets(rows, r => r.Gender.ToString(), r => r.Gender.ToString()),
            Facets(rows.Where(r => r.OlfactiveFamily is not null), r => r.OlfactiveFamily!.Value.ToString(), r => r.OlfactiveFamily!.Value.ToString()),
            [.. Facets(rows.SelectMany(r => r.Notes), n => n, n => n).Take(MaxNoteFacets)],
            rows.Count == 0 ? null : rows.Min(r => r.MinPrice),
            rows.Count == 0 ? null : rows.Max(r => r.MaxPrice),
            rows.Count);
    }

    public async Task<SearchSuggestionsDto> SuggestAsync(string? query, CancellationToken cancellationToken)
    {
        var q = PostgresTrigramCatalogSearch.Normalize(query ?? string.Empty);
        if (q.Length < 2)
        {
            return new SearchSuggestionsDto(q, [], []);
        }

        var hits = await search.SearchAsync(q, SuggestedFragrances, cancellationToken);
        List<Guid> ids = [.. hits.Select(h => h.FragranceId)];
        var rank = ids.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        var cards = ids.Count == 0
            ? []
            : await ListFragrancesHandler.ToCardsAsync(ListFragrancesHandler.Listed(db).Where(x => ids.Contains(x.F.Id)), inventory, cancellationToken);

        var brandIds = await search.SearchBrandsAsync(q, SuggestedBrands * 2, cancellationToken);
        var brands = (await ListBrandsAsync(cancellationToken)).ToDictionary(b => b.Slug);
        var brandRows = await db.Brands.AsNoTracking().Where(b => brandIds.Contains(b.Id)).Select(b => new { b.Id, b.Slug }).ToListAsync(cancellationToken);
        List<ShopBrandDto> matchedBrands = [.. brandIds
            .Select(id => brandRows.FirstOrDefault(b => b.Id == id))
            .Where(b => b is not null && brands.ContainsKey(b.Slug.Value))
            .Select(b => brands[b!.Slug.Value])
            .Take(SuggestedBrands)];

        return new SearchSuggestionsDto(q, [.. cards.OrderBy(c => rank[c.Id])], matchedBrands);
    }

    /// <summary>
    /// "Aroma të ngjashme": first the fragrance's own "smells like" links that are in our catalog, then others with
    /// the same mood ranked by shared accords (weighted by strength). Never the fragrance itself; null if not listed.
    /// </summary>
    public async Task<IReadOnlyList<FragranceListItemDto>?> SimilarAsync(string slugText, int limit, CancellationToken cancellationToken)
    {
        var slug = Slug.Create(slugText);
        if (slug.IsFailure)
        {
            return null;
        }

        var source = await ListFragrancesHandler.Listed(db).Where(x => x.F.Slug == slug.Value).Select(x => x.F).FirstOrDefaultAsync(cancellationToken);
        if (source is null)
        {
            return null;
        }

        limit = Math.Clamp(limit, 1, MaxSimilar);
        var candidates = await ListFragrancesHandler.Listed(db)
            .Where(x => x.F.Id != source.Id)
            .Select(x => new { x.F.Id, x.F.Mood, x.F.Accords, x.F.Gender })
            .ToListAsync(cancellationToken);

        var linked = source.SimilarTo.Where(r => r.FragranceId is not null).Select(r => r.FragranceId!.Value).ToHashSet();
        var sourceAccords = source.Accords.ToDictionary(a => a.Name, a => a.Percentage);
        List<Guid> ordered = [.. candidates
            .Select(c => new
            {
                c.Id,
                Score = (linked.Contains(c.Id) ? 10_000 : 0)
                    + (c.Mood is not null && c.Mood == source.Mood ? 150 : 0)
                    + c.Accords.Where(a => sourceAccords.ContainsKey(a.Name)).Sum(a => Math.Min(a.Percentage, sourceAccords[a.Name]))
                    + (c.Gender == source.Gender || c.Gender == Gender.Unisex ? 20 : 0),
            })
            .Where(c => c.Score > 20)
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Id)
            .Take(limit)
            .Select(c => c.Id)];

        var rank = ordered.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        var cards = ordered.Count == 0
            ? []
            : await ListFragrancesHandler.ToCardsAsync(ListFragrancesHandler.Listed(db).Where(x => ordered.Contains(x.F.Id)), inventory, cancellationToken);
        return [.. cards.OrderBy(c => rank[c.Id])];
    }
}

internal static class ShopCatalogEndpoints
{
    public static void Map(RouteGroupBuilder shop)
    {
        shop.MapGet("/brands", async (ShopCatalogHandler handler, CancellationToken ct) => TypedResults.Ok(await handler.ListBrandsAsync(ct)))
            .WithName("ListShopBrands");

        shop.MapGet("/brands/{slug}", async Task<Results<Ok<ShopBrandDetailsDto>, ProblemHttpResult>> (string slug, ShopCatalogHandler handler, CancellationToken ct) =>
                await handler.GetBrandAsync(slug, ct) is { } brand ? TypedResults.Ok(brand) : CatalogFeatureErrors.BrandNotFound.ToProblem())
            .WithName("GetShopBrand");

        shop.MapGet("/facets", async (ShopCatalogHandler handler, CancellationToken ct) => TypedResults.Ok(await handler.GetFacetsAsync(ct)))
            .WithName("GetShopFacets");

        shop.MapGet("/search/suggestions", async (string? q, ShopCatalogHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.SuggestAsync(q, ct)))
            .WithName("SearchSuggestions");

        shop.MapGet("/fragrances/{slug}/similar", async Task<Results<Ok<IReadOnlyList<FragranceListItemDto>>, ProblemHttpResult>> (string slug, int? limit, ShopCatalogHandler handler, CancellationToken ct) =>
                await handler.SimilarAsync(slug, limit ?? 8, ct) is { } similar ? TypedResults.Ok(similar) : CatalogFeatureErrors.FragranceNotFound.ToProblem())
            .WithName("GetSimilarFragrances");
    }
}
