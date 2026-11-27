using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Catalog.Domain.Brands;
using PerfumeShop.Modules.Catalog.Domain.Common;
using PerfumeShop.Modules.Catalog.Domain.Fragrances;
using PerfumeShop.Modules.Catalog.Features.Shared;
using PerfumeShop.Modules.Catalog.Infrastructure;
using PerfumeShop.Modules.Catalog.Infrastructure.Search;
using PerfumeShop.Modules.Inventory.Contracts;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;

namespace PerfumeShop.Modules.Catalog.Features.ListFragrances;

/// <param name="Brand">Brand slug, e.g. "lattafa".</param>
/// <param name="Mood">Oud, Sweet, Fresh, Spicy or Floral (the shop's "Çfarë të tërheq më shumë?").</param>
/// <param name="Note">A note name as imported (English, any case), e.g. "vanilla".</param>
/// <param name="InStock">true: only fragrances with at least one active variant in stock.</param>
/// <param name="Q">Typo-tolerant search over brand and name; results are ranked by relevance unless Sort is given.</param>
/// <param name="Sort">relevance (default with Q), newest (default), price_asc, price_desc, name.</param>
internal sealed record ListFragrancesQuery(
    string? Brand,
    string? Gender,
    string? Family,
    decimal? MinPrice,
    decimal? MaxPrice,
    string? Sort,
    string? Mood = null,
    string? Note = null,
    bool? InStock = null,
    string? Q = null,
    int Page = 1,
    int PageSize = 24);

/// <summary>The variant a card's "Shto" button adds: cheapest one in stock, else the cheapest.</summary>
internal sealed record CardVariantDto(Guid Id, string Type, SizeDto? Size, MoneyDto Price, MoneyDto? CompareAtPrice, bool InStock);

/// <param name="FromPrice">Cheapest active variant, EUR incl. VAT.</param>
/// <param name="InStock">At least one active variant has stock available (from Inventory).</param>
/// <param name="VariantCount">Active variants; with more than one the card links to the product page to choose.</param>
internal sealed record FragranceListItemDto(
    Guid Id,
    string Slug,
    string Name,
    string BrandName,
    string BrandSlug,
    string Concentration,
    string Gender,
    string? OlfactiveFamily,
    MoneyDto FromPrice,
    IReadOnlyList<AccordDto> TopAccords,
    ImageDto? Image,
    bool InStock,
    string? Mood,
    CardVariantDto DefaultVariant,
    int VariantCount);

internal sealed class ListFragrancesValidator : AbstractValidator<ListFragrancesQuery>
{
    public static readonly string[] Sorts = ["relevance", "newest", "price_asc", "price_desc", "name"];

    /// <summary>How many search hits are ranked at most (the catalog has a few hundred fragrances).</summary>
    public const int MaxSearchHits = 200;

    public ListFragrancesValidator()
    {
        RuleFor(x => x.Gender).IsEnumName(typeof(Gender), caseSensitive: false).When(x => x.Gender is not null);
        RuleFor(x => x.Family).IsEnumName(typeof(OlfactiveFamily), caseSensitive: false).When(x => x.Family is not null);
        RuleFor(x => x.Mood).IsEnumName(typeof(Mood), caseSensitive: false).When(x => x.Mood is not null);
        RuleFor(x => x.Note).MaximumLength(Note.NameMaxLength);
        RuleFor(x => x.Q).MaximumLength(PostgresTrigramCatalogSearch.MaxQueryLength);
        RuleFor(x => x.MinPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MaxPrice).GreaterThanOrEqualTo(x => x.MinPrice ?? 0);
        RuleFor(x => x.Sort).Must(s => Sorts.Contains(s, StringComparer.OrdinalIgnoreCase))
            .When(x => x.Sort is not null)
            .WithMessage($"Sort must be one of: {string.Join(", ", Sorts)}.");
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 48);
    }
}

/// <summary>A listed fragrance with its brand, composable in further EF queries.</summary>
internal sealed class ShopRow
{
    public required Fragrance F { get; init; }

    public required Brand B { get; init; }
}

/// <summary>Only published fragrances with at least one active variant are listed.</summary>
internal sealed class ListFragrancesHandler(CatalogDbContext db, IInventoryApi inventory, ICatalogSearch search) : IHandler
{
    /// <summary>Published fragrances with at least one active variant: what the shop may show.</summary>
    public static IQueryable<ShopRow> Listed(CatalogDbContext db) =>
        from f in db.Fragrances.AsNoTracking()
        join b in db.Brands.AsNoTracking() on f.BrandId equals b.Id
        where f.Status == FragranceStatus.Published && f.Variants.Any(v => v.IsActive)
        select new ShopRow { F = f, B = b };

    public async Task<PagedResponse<FragranceListItemDto>> HandleAsync(ListFragrancesQuery query, CancellationToken cancellationToken)
    {
        var empty = new PagedResponse<FragranceListItemDto>([], query.Page, query.PageSize, 0);
        var rows = Listed(db);

        if (!string.IsNullOrWhiteSpace(query.Brand))
        {
            var brandSlug = Slug.Create(query.Brand);
            if (brandSlug.IsFailure)
            {
                return empty;
            }

            rows = rows.Where(x => x.B.Slug == brandSlug.Value);
        }

        if (Enum.TryParse<Gender>(query.Gender, ignoreCase: true, out var gender))
        {
            rows = rows.Where(x => x.F.Gender == gender);
        }

        if (Enum.TryParse<OlfactiveFamily>(query.Family, ignoreCase: true, out var family))
        {
            rows = rows.Where(x => x.F.OlfactiveFamily == family);
        }

        if (Enum.TryParse<Mood>(query.Mood, ignoreCase: true, out var mood))
        {
            rows = rows.Where(x => x.F.Mood == mood);
        }

        if (!string.IsNullOrWhiteSpace(query.Note))
        {
            var note = query.Note.Trim().ToLowerInvariant();
            rows = rows.Where(x => EF.Property<List<string>>(x.F, "_noteKeys").Contains(note));
        }

        if (query.MinPrice is not null || query.MaxPrice is not null)
        {
            var min = query.MinPrice ?? 0;
            var max = query.MaxPrice ?? decimal.MaxValue;
            rows = rows.Where(x => x.F.Variants.Any(v => v.IsActive && v.Price!.Amount >= min && v.Price.Amount <= max));
        }

        IReadOnlyList<SearchHit>? hits = null;
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            hits = await search.SearchAsync(query.Q, ListFragrancesValidator.MaxSearchHits, cancellationToken);
            if (hits.Count == 0)
            {
                return empty;
            }

            List<Guid> hitIds = [.. hits.Select(h => h.FragranceId)];
            rows = rows.Where(x => hitIds.Contains(x.F.Id));
        }

        if (query.InStock == true)
        {
            // Stock lives in Inventory: ask once for every active variant of the candidates.
            var candidates = await rows
                .SelectMany(x => x.F.Variants.Where(v => v.IsActive).Select(v => new { FragranceId = x.F.Id, VariantId = v.Id }))
                .ToListAsync(cancellationToken);
            var available = await inventory.GetAvailabilityAsync([.. candidates.Select(c => c.VariantId)], cancellationToken);
            List<Guid> inStockIds = [.. candidates
                .Where(c => available.TryGetValue(c.VariantId, out var a) && a.InStock)
                .Select(c => c.FragranceId)
                .Distinct()];
            rows = rows.Where(x => inStockIds.Contains(x.F.Id));
        }

        var total = await rows.CountAsync(cancellationToken);
        var sort = query.Sort?.ToLowerInvariant() ?? (hits is null ? "newest" : "relevance");
        var skip = (query.Page - 1) * query.PageSize;

        if (sort == "relevance" && hits is not null)
        {
            // The hit list is already ordered by score: rank in memory, load only the page.
            var rank = hits.Select((h, i) => (h.FragranceId, i)).ToDictionary(x => x.FragranceId, x => x.i);
            var matching = await rows.Select(x => x.F.Id).ToListAsync(cancellationToken);
            List<Guid> pageIds = [.. matching.OrderBy(id => rank[id]).Skip(skip).Take(query.PageSize)];
            var items = await ToCardsAsync(rows.Where(x => pageIds.Contains(x.F.Id)), inventory, cancellationToken);
            return new PagedResponse<FragranceListItemDto>([.. items.OrderBy(i => rank[i.Id])], query.Page, query.PageSize, total);
        }

        rows = sort switch
        {
            "price_asc" => rows.OrderBy(x => x.F.Variants.Where(v => v.IsActive).Min(v => v.Price!.Amount)).ThenBy(x => x.F.Name),
            "price_desc" => rows.OrderByDescending(x => x.F.Variants.Where(v => v.IsActive).Min(v => v.Price!.Amount)).ThenBy(x => x.F.Name),
            "name" => rows.OrderBy(x => x.F.Name),
            _ => rows.OrderByDescending(x => x.F.CreatedAtUtc).ThenBy(x => x.F.Name),
        };

        var page = await ToCardsAsync(rows.Skip(skip).Take(query.PageSize), inventory, cancellationToken);
        return new PagedResponse<FragranceListItemDto>(page, query.Page, query.PageSize, total);
    }

    /// <summary>Card data for the rows, in their order, with one Inventory call for all of them.</summary>
    public static async Task<IReadOnlyList<FragranceListItemDto>> ToCardsAsync(IQueryable<ShopRow> rows, IInventoryApi inventory, CancellationToken cancellationToken)
    {
        var page = await rows
            .Select(x => new
            {
                x.F.Id,
                x.F.Slug,
                x.F.Name,
                BrandName = x.B.Name,
                BrandSlug = x.B.Slug,
                x.F.Concentration,
                x.F.Gender,
                x.F.OlfactiveFamily,
                x.F.Mood,
                x.F.Accords,
                Variants = x.F.Variants
                    .Where(v => v.IsActive)
                    .OrderBy(v => v.Price!.Amount)
                    .Select(v => new { v.Id, v.Type, v.Size, v.Price, v.CompareAtPrice, v.Images })
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        var stock = await inventory.GetAvailabilityAsync([.. page.SelectMany(r => r.Variants.Select(v => v.Id))], cancellationToken);
        bool InStock(Guid id) => stock.TryGetValue(id, out var s) && s.InStock;

        return [.. page.Select(r =>
        {
            var cheapest = r.Variants[0];
            var chosen = r.Variants.FirstOrDefault(v => InStock(v.Id)) ?? cheapest;
            var image = r.Variants.SelectMany(v => v.Images).OrderBy(i => i.SortOrder).FirstOrDefault();
            return new FragranceListItemDto(
                r.Id,
                r.Slug.Value,
                r.Name,
                r.BrandName,
                r.BrandSlug.Value,
                r.Concentration.ToString(),
                r.Gender.ToString(),
                r.OlfactiveFamily?.ToString(),
                cheapest.Price.ToDto()!,
                [.. r.Accords.Take(3).Select(a => new AccordDto(a.Name, a.Percentage))],
                image is null ? null : new ImageDto(image.Url, image.AltText, image.SortOrder),
                r.Variants.Any(v => InStock(v.Id)),
                r.Mood?.ToString(),
                new CardVariantDto(
                    chosen.Id,
                    chosen.Type.ToString(),
                    chosen.Size is null ? null : new SizeDto(chosen.Size.Value, chosen.Size.Unit.ToString()),
                    chosen.Price.ToDto()!,
                    chosen.CompareAtPrice.ToDto(),
                    InStock(chosen.Id)),
                r.Variants.Count);
        })];
    }
}

internal static class ListFragrancesEndpoint
{
    public static void Map(RouteGroupBuilder shop) =>
        shop.MapGet("/fragrances", async ([AsParameters] ListFragrancesQuery query, ListFragrancesHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(query, ct)))
            .WithValidation<ListFragrancesQuery>()
            .WithName("ListFragrances");
}
