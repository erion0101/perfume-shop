using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Orders.Domain;
using PerfumeShop.Modules.Orders.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Modules;

namespace PerfumeShop.Modules.Orders.Features.CustomerHistory;

internal sealed record CustomerOrderDto(Guid Id, string Number, string Status, decimal Total, DateTime PlacedAtUtc, string? CancellationReason);

/// <param name="Completed">Delivered or cash collected.</param>
/// <param name="Refused">Parcels refused at the door (the main fake-order risk).</param>
/// <param name="Cancelled">Cancelled or expired (not confirmed in time).</param>
/// <param name="Open">Pending, confirmed or shipped.</param>
/// <param name="Recent">Last 10 orders, newest first.</param>
internal sealed record CustomerOrderHistoryDto(Guid CustomerId, int Total, int Completed, int Refused, int Cancelled, int Open, IReadOnlyList<CustomerOrderDto> Recent);

/// <summary>What staff see before calling a customer to confirm a COD order.</summary>
internal sealed class CustomerHistoryHandler(OrdersDbContext db) : IHandler
{
    public async Task<CustomerOrderHistoryDto> HandleAsync(Guid customerId, CancellationToken ct)
    {
        var orders = db.Orders.AsNoTracking().Where(o => o.CustomerId == customerId);
        var byStatus = await orders.GroupBy(o => o.Status).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        int Count(params OrderStatus[] statuses) => statuses.Sum(s => byStatus.GetValueOrDefault(s));

        var recent = await orders.OrderByDescending(o => o.PlacedAtUtc).Take(10)
            .Select(o => new CustomerOrderDto(o.Id, o.Number, o.Status.ToString(), o.Total, o.PlacedAtUtc, o.CancellationReason))
            .ToListAsync(ct);

        return new CustomerOrderHistoryDto(
            customerId,
            byStatus.Values.Sum(),
            Count(OrderStatus.Delivered, OrderStatus.CashCollected),
            Count(OrderStatus.Refused),
            Count(OrderStatus.Cancelled, OrderStatus.Expired),
            Count(OrderStatus.Pending, OrderStatus.Confirmed, OrderStatus.Shipped),
            recent);
    }
}

internal static class CustomerHistoryEndpoint
{
    public static void Map(RouteGroupBuilder staff) =>
        staff.MapGet("/customers/{customerId:guid}/history", async (Guid customerId, CustomerHistoryHandler h, CancellationToken ct) =>
                TypedResults.Ok(await h.HandleAsync(customerId, ct)))
            .WithName("GetCustomerOrderHistory")
            .WithDescription("Order counts (completed, refused, cancelled, open) and the last 10 orders of a customer.");
}
