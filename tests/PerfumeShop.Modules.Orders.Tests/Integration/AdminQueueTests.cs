using System.Net;
using Microsoft.Extensions.DependencyInjection;
using PerfumeShop.Modules.Orders.Contracts;
using PerfumeShop.Modules.Orders.Features.CustomerHistory;
using PerfumeShop.Modules.Orders.Features.ListOrders;
using PerfumeShop.Modules.Orders.Features.Shared;
using PerfumeShop.Shared.Infrastructure.Http;

namespace PerfumeShop.Modules.Orders.Tests.Integration;

/// <summary>API support for the admin confirmation queue: oldest first, risk counts, status tabs, courier filter, history.</summary>
public sealed class AdminQueueTests(OrdersApiFactory factory)
{
    private const string Staff = "/api/admin/orders";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly HttpClient _staff = factory.CreateAdminClient();

    private async Task<Guid> PlaceAsync(TestCustomer customer, Guid variantId)
    {
        var placed = await customer.PlaceAsync(new[] { new { variantId, quantity = 1 } });
        Assert.Equal(HttpStatusCode.Created, placed.StatusCode);
        return (await placed.Read<OrderDto>()).Id;
    }

    [Fact]
    public async Task Queue_is_oldest_first_and_shows_the_customers_refusals()
    {
        var variantId = await factory.VariantAsync();
        var risky = await factory.CustomerAsync();
        var refused = await PlaceAsync(risky, variantId);
        await _staff.PostJson($"{Staff}/{refused}/confirm");
        Assert.True((await factory.OrdersApiAsync(api => api.MarkShippedAsync(refused, "Posta", $"PK{Guid.NewGuid():N}"[..12], "staff", Ct))).IsSuccess);
        Assert.True((await factory.OrdersApiAsync(api => api.MarkRefusedAsync(refused, "Refuzoi", "staff", Ct))).IsSuccess);
        var first = await PlaceAsync(risky, variantId);
        var second = await PlaceAsync(await factory.CustomerAsync(), variantId);

        var queue = (await (await _staff.Get($"{Staff}?status=Pending&sort=placed&pageSize=200")).Read<PagedResponse<OrderListItemDto>>()).Items;

        var ids = queue.Select(o => o.Id).ToList();
        Assert.True(ids.IndexOf(first) < ids.IndexOf(second));
        var riskyRow = queue.Single(o => o.Id == first);
        Assert.Equal((risky.CustomerId, 2, 1), (riskyRow.CustomerId, riskyRow.CustomerOrderCount, riskyRow.CustomerRefusedCount));
        Assert.Equal(0, queue.Single(o => o.Id == second).CustomerRefusedCount);
        Assert.Equal(HttpStatusCode.BadRequest, (await _staff.Get($"{Staff}?sort=total")).StatusCode);

        var history = await (await _staff.Get($"{Staff}/customers/{risky.CustomerId}/history")).Read<CustomerOrderHistoryDto>();
        Assert.Equal((2, 0, 1, 0, 1), (history.Total, history.Completed, history.Refused, history.Cancelled, history.Open));
        Assert.Equal([first, refused], history.Recent.Select(r => r.Id));
    }

    [Fact]
    public async Task Status_counts_follow_the_filters_and_courier_filter_works()
    {
        var variantId = await factory.VariantAsync();
        var customer = await factory.CustomerAsync();
        var shipped = await PlaceAsync(customer, variantId);
        var pending = await PlaceAsync(customer, variantId);
        await _staff.PostJson($"{Staff}/{shipped}/confirm");
        var courier = $"Courier {Guid.NewGuid():N}"[..20];
        Assert.True((await factory.OrdersApiAsync(api => api.MarkShippedAsync(shipped, courier, $"PK{Guid.NewGuid():N}"[..12], "staff", Ct))).IsSuccess);

        var counts = await (await _staff.Get($"{Staff}/counts?phone={customer.Phone[^6..]}")).Read<OrderCountsDto>();
        Assert.Equal((2, 1, 1, 0), (counts.Total, counts.Counts["Pending"], counts.Counts["Shipped"], counts.Counts["Refused"]));
        Assert.Equal(8, counts.Counts.Count);

        var byCourier = (await (await _staff.Get($"{Staff}?courier={Uri.EscapeDataString(courier.ToLowerInvariant())}")).Read<PagedResponse<OrderListItemDto>>()).Items;
        Assert.Equal(shipped, Assert.Single(byCourier).Id);
        Assert.Equal(courier, byCourier[0].CourierName);
        Assert.DoesNotContain(byCourier, o => o.Id == pending);

        // Inventory's batch trace names the order by its number (through IOrdersApi).
        await using var scope = factory.CreateScope();
        var order = await scope.ServiceProvider.GetRequiredService<IOrdersApi>().GetOrderAsync(shipped, Ct);
        var batch = order!.Lines.Single().Batches.Single();
        var code = batch[..batch.LastIndexOf('×')];
        var trace = await (await _staff.Get($"/api/admin/inventory/batches?code={Uri.EscapeDataString(code)}")).Read<List<TraceBody>>();
        Assert.Contains(trace.SelectMany(t => t.ShippedTo), s => s.OrderId == shipped && s.OrderNumber == byCourier[0].Number);
    }

    private sealed record TraceShipment(Guid OrderId, string? OrderNumber);

    private sealed record TraceBody(IReadOnlyList<TraceShipment> ShippedTo);
}
