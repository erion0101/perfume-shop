using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Catalog.Domain.Common;
using PerfumeShop.Modules.Catalog.Domain.Fragrances;
using PerfumeShop.Modules.Catalog.Features.Shared;
using PerfumeShop.Modules.Catalog.Infrastructure;
using PerfumeShop.Modules.Inventory.Contracts;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;

namespace PerfumeShop.Modules.Catalog.Features.ListFragrances;

/// <param name="Brand">Brand slug, e.g. "lattafa".</param>
/// <param name="Sort">newest (default), price_asc, price_desc, name.</param>
internal sealed record ListFragrancesQuery(
    string? Brand,
    string? Gender,
    string? Family,
    decimal? MinPrice,
    decimal? MaxPrice,
    string? Sort,
    int Page = 1,
    int PageSize = 24);

/// <param name="FromPrice">Cheapest active variant, EUR incl. VAT.</param>
/// <param name="InStock">At least one active variant has stock available (from Inventory).</param>
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
    bool InStock);

internal sealed class ListFragrancesValidator : AbstractValidator<ListFragrancesQuery>
{
    public static readonly string[] Sorts = ["newest", "price_asc", "price_desc", "name"];

    public ListFragrancesValidator()
    {
        RuleFor(x => x.Gender).IsEnumName(typeof(Gender), caseSensitive: false).When(x => x.Gender is not null);
        RuleFor(x => x.Family).IsEnumName(typeof(OlfactiveFamily), caseSensitive: false).When(x => x.Family is not null);
        RuleFor(x => x.MinPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MaxPrice).GreaterThanOrEqualTo(x => x.MinPrice ?? 0);
        RuleFor(x => x.Sort).Must(s => Sorts.Contains(s, StringComparer.OrdinalIgnoreCase))
            .When(x => x.Sort is not null)
            .WithMessage($"Sort must be one of: {string.Join(", ", Sorts)}.");
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 48);
    }
}

/// <summary>Only published fragrances with at least one active variant are listed.</summary>
internal sealed class ListFragrancesHandler(CatalogDbContext db, IInventoryApi inventory) : IHandler
{
    public async Task<PagedResponse<FragranceListItemDto>> HandleAsync(ListFragrancesQuery query, CancellationToken cancellationToken)
    {
        var rows = from f in db.Fragrances.AsNoTracking()
                   join b in db.Brands on f.BrandId equals b.Id
                   where f.Status == FragranceStatus.Published && f.Variants.Any(v => v.IsActive)
                   select new { f, b };

        if (!string.IsNullOrWhiteSpace(query.Brand))
        {
            var brandSlug = Slug.Create(query.Brand);
            if (brandSlug.IsFailure)
            {
                return new PagedResponse<FragranceListItemDto>([], query.Page, query.PageSize, 0);
            }

            rows = rows.Where(x => x.b.Slug == brandSlug.Value);
        }

        if (Enum.TryParse<Gender>(query.Gender, ignoreCase: true, out var gender))
        {
            rows = rows.Where(x => x.f.Gender == gender);
        }

        if (Enum.TryParse<OlfactiveFamily>(query.Family, ignoreCase: true, out var family))
        {
            rows = rows.Where(x => x.f.OlfactiveFamily == family);
        }

        if (query.MinPrice is not null || query.MaxPrice is not null)
        {
            var min = query.MinPrice ?? 0;
            var max = query.MaxPrice ?? decimal.MaxValue;
            rows = rows.Where(x => x.f.Variants.Any(v => v.IsActive && v.Price!.Amount >= min && v.Price.Amount <= max));
        }

        var total = await rows.CountAsync(cancellationToken);

        rows = query.Sort?.ToLowerInvariant() switch
        {
            "price_asc" => rows.OrderBy(x => x.f.Variants.Where(v => v.IsActive).Min(v => v.Price!.Amount)).ThenBy(x => x.f.Name),
            "price_desc" => rows.OrderByDescending(x => x.f.Variants.Where(v => v.IsActive).Min(v => v.Price!.Amount)).ThenBy(x => x.f.Name),
            "name" => rows.OrderBy(x => x.f.Name),
            _ => rows.OrderByDescending(x => x.f.CreatedAtUtc).ThenBy(x => x.f.Name),
        };

        var page = await rows
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new
            {
                x.f.Id,
                x.f.Slug,
                x.f.Name,
                BrandName = x.b.Name,
                BrandSlug = x.b.Slug,
                x.f.Concentration,
                x.f.Gender,
                x.f.OlfactiveFamily,
                x.f.Accords,
                Variants = x.f.Variants
                    .Where(v => v.IsActive)
                    .OrderBy(v => v.Price!.Amount)
                    .Select(v => new { v.Price, v.Images })
                    .Take(1)
                    .ToList(),
                ActiveVariantIds = x.f.Variants.Where(v => v.IsActive).Select(v => v.Id).ToList(),
            })
            .ToListAsync(cancellationToken);

        // One Inventory call for the whole page.
        var stock = await inventory.GetAvailabilityAsync([.. page.SelectMany(r => r.ActiveVariantIds)], cancellationToken);

        return new PagedResponse<FragranceListItemDto>(
            [.. page.Select(r =>
            {
                var cheapest = r.Variants[0];
                var image = cheapest.Images.FirstOrDefault();
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
                    r.ActiveVariantIds.Any(id => stock.TryGetValue(id, out var s) && s.InStock));
            })],
            query.Page,
            query.PageSize,
            total);
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
