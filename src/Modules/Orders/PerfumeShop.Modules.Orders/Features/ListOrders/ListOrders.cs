using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Orders.Domain;
using PerfumeShop.Modules.Orders.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;

namespace PerfumeShop.Modules.Orders.Features.ListOrders;

/// <param name="Phone">Exact E.164 ("+38344123456") or the last digits ("123456").</param>
/// <param name="From">Placed on or after this date (UTC).</param>
/// <param name="To">Placed on or before this date (UTC, whole day).</param>
/// <param name="Courier">Part of the courier name (orders that shipped with it).</param>
/// <param name="Sort">-placed (default, newest first) or placed (oldest first, e.g. the confirmation queue).</param>
internal sealed record ListOrdersQuery(
    string? Status, DateOnly? From, DateOnly? To, string? Phone, string? Number, string? Courier, string? Sort, Guid? CustomerId = null, int Page = 1, int PageSize = 50);

/// <summary>Same filters as the list, without status: the numbers on the status tabs.</summary>
internal sealed record OrderCountsQuery(DateOnly? From, DateOnly? To, string? Phone, string? Number, string? Courier);

/// <param name="Counts">Per status (every status is present, 0 when none).</param>
internal sealed record OrderCountsDto(int Total, IReadOnlyDictionary<string, int> Counts);

internal sealed record OrderListItemDto(
    Guid Id,
    string Number,
    string Status,
    string CustomerName,
    string CustomerPhone,
    string City,
    decimal Total,
    string Currency,
    int ItemCount,
    DateTime PlacedAtUtc,
    DateTime? ConfirmEndsAtUtc,
    string? TrackingNumber,
    string? CourierName,
    Guid CustomerId,
    int CustomerOrderCount,
    int CustomerRefusedCount);

internal sealed class ListOrdersValidator : AbstractValidator<ListOrdersQuery>
{
    public ListOrdersValidator()
    {
        RuleFor(x => x.Status).IsEnumName(typeof(OrderStatus), caseSensitive: false).When(x => x.Status is not null);
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).When(x => x.From is not null && x.To is not null);
        RuleFor(x => x.Phone).MaximumLength(20);
        RuleFor(x => x.Number).MaximumLength(20);
        RuleFor(x => x.Courier).MaximumLength(100);
        RuleFor(x => x.Sort).Must(s => s is "placed" or "-placed").When(x => x.Sort is not null).WithMessage("Sort must be placed or -placed.");
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}

/// <summary>Staff order list, newest first, with each customer's order and refusal counts (risk).</summary>
internal sealed class ListOrdersHandler(OrdersDbContext db) : IHandler
{
    public async Task<OrderCountsDto> CountsAsync(OrderCountsQuery query, CancellationToken cancellationToken)
    {
        var counts = await Filter(db.Orders.AsNoTracking(), query.From, query.To, query.Phone, query.Number, query.Courier)
            .GroupBy(o => o.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        var all = Enum.GetValues<OrderStatus>().ToDictionary(s => s.ToString(), s => counts.GetValueOrDefault(s));
        return new OrderCountsDto(all.Values.Sum(), all);
    }

    public async Task<PagedResponse<OrderListItemDto>> HandleAsync(ListOrdersQuery query, CancellationToken cancellationToken)
    {
        var orders = Filter(db.Orders.AsNoTracking(), query.From, query.To, query.Phone, query.Number, query.Courier);

        if (Enum.TryParse<OrderStatus>(query.Status, ignoreCase: true, out var status))
        {
            orders = orders.Where(o => o.Status == status);
        }

        if (query.CustomerId is { } customerId)
        {
            orders = orders.Where(o => o.CustomerId == customerId);
        }

        var total = await orders.CountAsync(cancellationToken);
        var sorted = query.Sort == "placed" ? orders.OrderBy(o => o.PlacedAtUtc) : orders.OrderByDescending(o => o.PlacedAtUtc);
        var page = await sorted
            .ThenBy(o => o.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(o => new OrderListItemDto(
                o.Id, o.Number, o.Status.ToString(), o.Customer.FullName, o.Customer.Phone, o.Address.City, o.Total, Order.Currency,
                o.Lines.Sum(l => l.Quantity), o.PlacedAtUtc, o.Status == OrderStatus.Pending ? o.ExpiresAtUtc : null, o.TrackingNumber,
                o.CourierName, o.CustomerId,
                db.Orders.Count(x => x.CustomerId == o.CustomerId),
                db.Orders.Count(x => x.CustomerId == o.CustomerId && x.Status == OrderStatus.Refused)))
            .ToListAsync(cancellationToken);

        return new PagedResponse<OrderListItemDto>(page, query.Page, query.PageSize, total);
    }

    private static IQueryable<Order> Filter(IQueryable<Order> orders, DateOnly? fromDate, DateOnly? toDate, string? phone, string? numberFilter, string? courier)
    {
        if (fromDate is { } from)
        {
            var start = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            orders = orders.Where(o => o.PlacedAtUtc >= start);
        }

        if (toDate is { } to)
        {
            var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            orders = orders.Where(o => o.PlacedAtUtc < end);
        }

        if (!string.IsNullOrWhiteSpace(phone))
        {
            var digits = new string([.. phone.Where(char.IsAsciiDigit)]);
            orders = orders.Where(o => o.Customer.Phone.EndsWith(digits));
        }

        if (!string.IsNullOrWhiteSpace(numberFilter))
        {
            var number = numberFilter.Trim().ToUpperInvariant();
            orders = orders.Where(o => o.Number.Contains(number));
        }

        if (!string.IsNullOrWhiteSpace(courier))
        {
            var name = courier.Trim().ToLower();
            orders = orders.Where(o => o.CourierName != null && o.CourierName.ToLower().Contains(name));
        }

        return orders;
    }
}

internal static class ListOrdersEndpoint
{
    public static void Map(RouteGroupBuilder staff)
    {
        staff.MapGet("/", async ([AsParameters] ListOrdersQuery query, ListOrdersHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(query, ct)))
            .WithValidation<ListOrdersQuery>()
            .WithName("ListOrders");

        staff.MapGet("/counts", async ([AsParameters] OrderCountsQuery query, ListOrdersHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.CountsAsync(query, ct)))
            .WithName("CountOrdersByStatus")
            .WithDescription("Orders per status for the same filters as the list (status tabs).");
    }
}
