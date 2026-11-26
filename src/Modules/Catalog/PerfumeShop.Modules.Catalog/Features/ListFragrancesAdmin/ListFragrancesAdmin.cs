using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Catalog.Domain.Fragrances;
using PerfumeShop.Modules.Catalog.Infrastructure;
using PerfumeShop.Modules.Inventory.Contracts;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;

namespace PerfumeShop.Modules.Catalog.Features.ListFragrancesAdmin;

/// <param name="InStock">true: at least one variant has stock available; false: none has.</param>
/// <param name="Sort">-updated (default), updated, name, -name, brand, -brand.</param>
internal sealed record ListFragrancesAdminQuery(string? Search, Guid? BrandId, string? Status, bool? InStock, string? Sort, int Page = 1, int PageSize = 25);

internal sealed record FragranceAdminListItemDto(
    Guid Id,
    string Name,
    string Slug,
    string BrandName,
    string Concentration,
    string Gender,
    string Status,
    int VariantCount,
    int ActiveVariantCount,
    string? FragellaId,
    bool InStock,
    DateTime UpdatedAtUtc);

internal sealed class ListFragrancesAdminValidator : AbstractValidator<ListFragrancesAdminQuery>
{
    public ListFragrancesAdminValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Status).IsEnumName(typeof(FragranceStatus), caseSensitive: false).When(x => x.Status is not null);
        RuleFor(x => x.Sort).Must(s => ListFragrancesAdminHandler.Sorts.Contains(s!)).When(x => x.Sort is not null)
            .WithMessage($"Sort must be one of: {string.Join(", ", ListFragrancesAdminHandler.Sorts)}.");
    }
}

internal sealed class ListFragrancesAdminHandler(CatalogDbContext db, IInventoryApi inventory) : IHandler
{
    public static readonly string[] Sorts = ["-updated", "updated", "name", "-name", "brand", "-brand"];

    public async Task<PagedResponse<FragranceAdminListItemDto>> HandleAsync(ListFragrancesAdminQuery query, CancellationToken cancellationToken)
    {
        var rows = from f in db.Fragrances.AsNoTracking()
                   join b in db.Brands on f.BrandId equals b.Id
                   select new { f, b };

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            rows = rows.Where(x => x.f.Name.ToLower().Contains(term) || x.b.Name.ToLower().Contains(term));
        }

        if (query.BrandId is { } brandId)
        {
            rows = rows.Where(x => x.f.BrandId == brandId);
        }

        if (Enum.TryParse<FragranceStatus>(query.Status, ignoreCase: true, out var status))
        {
            rows = rows.Where(x => x.f.Status == status);
        }

        var inStock = (await inventory.GetVariantIdsInStockAsync(cancellationToken)).ToList();
        if (query.InStock is { } wanted)
        {
            rows = wanted
                ? rows.Where(x => x.f.Variants.Any(v => inStock.Contains(v.Id)))
                : rows.Where(x => !x.f.Variants.Any(v => inStock.Contains(v.Id)));
        }

        var total = await rows.CountAsync(cancellationToken);
        var sorted = query.Sort switch
        {
            "updated" => rows.OrderBy(x => x.f.UpdatedAtUtc),
            "name" => rows.OrderBy(x => x.f.Name),
            "-name" => rows.OrderByDescending(x => x.f.Name),
            "brand" => rows.OrderBy(x => x.b.Name).ThenBy(x => x.f.Name),
            "-brand" => rows.OrderByDescending(x => x.b.Name).ThenBy(x => x.f.Name),
            _ => rows.OrderByDescending(x => x.f.UpdatedAtUtc),
        };
        var page = await sorted
            .ThenBy(x => x.f.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new
            {
                x.f.Id,
                x.f.Name,
                x.f.Slug,
                BrandName = x.b.Name,
                x.f.Concentration,
                x.f.Gender,
                x.f.Status,
                VariantCount = x.f.Variants.Count,
                ActiveVariantCount = x.f.Variants.Count(v => v.IsActive),
                x.f.FragellaId,
                VariantIds = x.f.Variants.Select(v => v.Id).ToList(),
                x.f.UpdatedAtUtc,
            })
            .ToListAsync(cancellationToken);

        return new PagedResponse<FragranceAdminListItemDto>(
            [.. page.Select(r => new FragranceAdminListItemDto(
                r.Id, r.Name, r.Slug.Value, r.BrandName, r.Concentration.ToString(), r.Gender.ToString(),
                r.Status.ToString(), r.VariantCount, r.ActiveVariantCount, r.FragellaId, r.VariantIds.Any(inStock.Contains), r.UpdatedAtUtc))],
            query.Page,
            query.PageSize,
            total);
    }
}

internal static class ListFragrancesAdminEndpoint
{
    public static void Map(RouteGroupBuilder admin) =>
        admin.MapGet("/fragrances", async ([AsParameters] ListFragrancesAdminQuery query, ListFragrancesAdminHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(query, ct)))
            .WithValidation<ListFragrancesAdminQuery>()
            .WithName("ListFragrancesAdmin");
}
