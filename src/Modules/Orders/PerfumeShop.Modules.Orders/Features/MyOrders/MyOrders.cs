using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Inventory.Contracts;
using PerfumeShop.Modules.Orders.Domain;
using PerfumeShop.Modules.Orders.Features.Shared;
using PerfumeShop.Modules.Orders.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Auth;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Orders.Features.MyOrders;

internal sealed record MyOrdersQuery(int Page = 1, int PageSize = 20);

internal sealed record MyOrderListItemDto(Guid Id, string Number, string Status, decimal Total, string Currency, int ItemCount, DateTime PlacedAtUtc);

internal sealed record CancelMyOrderRequest(string? Reason);

internal sealed class MyOrdersQueryValidator : AbstractValidator<MyOrdersQuery>
{
    public MyOrdersQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 50);
    }
}

/// <summary>The signed-in customer's own orders. Other customers' orders are reported as not found.</summary>
internal sealed class MyOrdersHandler(OrdersDbContext db, IInventoryApi inventory, TimeProvider timeProvider) : IHandler
{
    public async Task<PagedResponse<MyOrderListItemDto>> ListAsync(Guid customerId, MyOrdersQuery query, CancellationToken cancellationToken)
    {
        var orders = db.Orders.AsNoTracking().Where(o => o.CustomerId == customerId);
        var total = await orders.CountAsync(cancellationToken);
        var page = await orders
            .OrderByDescending(o => o.PlacedAtUtc)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(o => new MyOrderListItemDto(o.Id, o.Number, o.Status.ToString(), o.Total, Order.Currency, o.Lines.Sum(l => l.Quantity), o.PlacedAtUtc))
            .ToListAsync(cancellationToken);
        return new PagedResponse<MyOrderListItemDto>(page, query.Page, query.PageSize, total);
    }

    public async Task<Result<OrderDto>> GetAsync(Guid customerId, Guid orderId, CancellationToken cancellationToken)
    {
        var order = await db.LoadOrderAsync(orderId, cancellationToken, customerId);
        return order.IsSuccess ? order.Value.ToDto(forCustomer: true) : order.Error;
    }

    /// <summary>Only while Pending; the reserved stock is released.</summary>
    public async Task<Result<OrderDto>> CancelAsync(Guid customerId, Guid orderId, CancelMyOrderRequest request, CancellationToken cancellationToken)
    {
        var order = await db.LoadOrderAsync(orderId, cancellationToken, customerId);
        if (order.IsFailure)
        {
            return order.Error;
        }

        var cancelled = order.Value.Cancel(OrderActor.Customer(customerId), request.Reason, timeProvider.GetUtcNow().UtcDateTime);
        if (cancelled.IsFailure)
        {
            return cancelled.Error;
        }

        var released = await inventory.ReleaseAsync([.. order.Value.Lines.Select(l => l.Id)], "Cancelled by the customer", cancellationToken);
        if (released.IsFailure)
        {
            return released.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return order.Value.ToDto(forCustomer: true);
    }
}

internal static class MyOrdersEndpoint
{
    public static void Map(RouteGroupBuilder customer)
    {
        customer.MapGet("/", async ([AsParameters] MyOrdersQuery query, ClaimsPrincipal user, MyOrdersHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.ListAsync(user.GetCustomerId()!.Value, query, ct)))
            .WithValidation<MyOrdersQuery>()
            .WithName("GetMyOrders");

        customer.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, MyOrdersHandler handler, CancellationToken ct) =>
                (await handler.GetAsync(user.GetCustomerId()!.Value, id, ct)).ToOk())
            .WithName("GetMyOrder");

        customer.MapPost("/{id:guid}/cancel", async (Guid id, CancelMyOrderRequest request, ClaimsPrincipal user, MyOrdersHandler handler, CancellationToken ct) =>
                (await handler.CancelAsync(user.GetCustomerId()!.Value, id, request, ct)).ToOk())
            .WithName("CancelMyOrder");
    }
}
