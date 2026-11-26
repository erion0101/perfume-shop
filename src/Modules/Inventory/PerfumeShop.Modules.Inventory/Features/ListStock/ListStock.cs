using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Catalog.Contracts;
using PerfumeShop.Modules.Inventory.Features.Shared;
using PerfumeShop.Modules.Inventory.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;

namespace PerfumeShop.Modules.Inventory.Features.ListStock;

/// <param name="LowStock">Only items with available ≤ their low-stock threshold.</param>
/// <param name="OutOfStock">Only items with nothing available.</param>
/// <param name="Sort">available (default, lowest first), -available, onHand, -onHand, updated, -updated.</param>
internal sealed record ListStockQuery(bool? LowStock, bool? OutOfStock, string? Sort, int Page = 1, int PageSize = 50);

internal sealed class ListStockValidator : AbstractValidator<ListStockQuery>
{
    public ListStockValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
        RuleFor(x => x.Sort).Must(s => ListStockHandler.Sorts.Contains(s!)).When(x => x.Sort is not null)
            .WithMessage($"Sort must be one of: {string.Join(", ", ListStockHandler.Sorts)}.");
    }
}

/// <summary>Stock levels, lowest available first, with SKU and name from Catalog.</summary>
internal sealed class ListStockHandler(InventoryDbContext db, ICatalogApi catalog) : IHandler
{
    public static readonly string[] Sorts = ["available", "-available", "onHand", "-onHand", "updated", "-updated"];

    public async Task<PagedResponse<StockItemDto>> HandleAsync(ListStockQuery query, CancellationToken cancellationToken)
    {
        var items = db.StockItems.AsNoTracking();
        if (query.LowStock == true)
        {
            items = items.Where(i => i.OnHand - i.Reserved <= i.LowStockThreshold);
        }

        if (query.OutOfStock == true)
        {
            items = items.Where(i => i.OnHand - i.Reserved <= 0);
        }

        var total = await items.CountAsync(cancellationToken);
        var sorted = query.Sort switch
        {
            "-available" => items.OrderByDescending(i => i.OnHand - i.Reserved),
            "onHand" => items.OrderBy(i => i.OnHand),
            "-onHand" => items.OrderByDescending(i => i.OnHand),
            "updated" => items.OrderBy(i => i.UpdatedAtUtc),
            "-updated" => items.OrderByDescending(i => i.UpdatedAtUtc),
            _ => items.OrderBy(i => i.OnHand - i.Reserved),
        };
        var page = await sorted
            .ThenBy(i => i.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        var variants = await catalog.GetVariantSummariesAsync([.. page.Select(i => i.Id)], cancellationToken);
        return new PagedResponse<StockItemDto>(
            [.. page.Select(i => i.ToDto(variants.GetValueOrDefault(i.Id)))],
            query.Page,
            query.PageSize,
            total);
    }
}

internal static class ListStockEndpoint
{
    public static void Map(RouteGroupBuilder admin) =>
        admin.MapGet("/items", async ([AsParameters] ListStockQuery query, ListStockHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(query, ct)))
            .WithValidation<ListStockQuery>()
            .WithName("ListStock");
}
