using System.Net;
using PerfumeShop.Modules.Orders.Features.Dashboard;
using PerfumeShop.Modules.Orders.Features.ListOrders;
using PerfumeShop.Modules.Orders.Features.Shared;
using PerfumeShop.Shared.Infrastructure.Http;

namespace PerfumeShop.Modules.Orders.Tests.Integration;

/// <summary>Numbers behind the admin dashboard and the customer list (counts come from Orders).</summary>
public sealed class AdminDashboardTests(OrdersApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly HttpClient _staff = factory.CreateAdminClient();

    private sealed record CustomerRow(Guid Id, string Phone, int OrderCount, int RefusedCount);

    private async Task<Guid> PlaceAsync(TestCustomer customer, Guid variantId, int quantity = 1)
    {
        var placed = await customer.PlaceAsync(new[] { new { variantId, quantity } });
        Assert.Equal(HttpStatusCode.Created, placed.StatusCode);
        return (await placed.Read<OrderDto>()).Id;
    }

    private async Task RefuseAsync(Guid orderId)
    {
        await _staff.PostJson($"/api/admin/orders/{orderId}/confirm");
        Assert.True((await factory.OrdersApiAsync(api => api.MarkShippedAsync(orderId, "Posta", $"PK{Guid.NewGuid():N}"[..12], "staff", Ct))).IsSuccess);
        Assert.True((await factory.OrdersApiAsync(api => api.MarkRefusedAsync(orderId, "Refuzoi", "staff", Ct))).IsSuccess);
    }

    [Fact]
    public async Task Customer_list_shows_order_and_refusal_counts_and_filters_refusers()
    {
        var variantId = await factory.VariantAsync();
        var refuser = await factory.CustomerAsync();
        var good = await factory.CustomerAsync();
        await RefuseAsync(await PlaceAsync(refuser, variantId));
        await PlaceAsync(refuser, variantId);
        await PlaceAsync(good, variantId);

        var refusers = (await (await _staff.Get("/api/admin/customers?hasRefusals=true&pageSize=100")).Read<PagedResponse<CustomerRow>>()).Items;
        var row = Assert.Single(refusers, c => c.Id == refuser.CustomerId);
        Assert.Equal((2, 1), (row.OrderCount, row.RefusedCount));
        Assert.DoesNotContain(refusers, c => c.Id == good.CustomerId);

        var all = (await (await _staff.Get($"/api/admin/customers?search={Uri.EscapeDataString(good.Phone)}")).Read<PagedResponse<CustomerRow>>()).Items;
        Assert.Equal((1, 0), (Assert.Single(all).OrderCount, all[0].RefusedCount));

        var orders = (await (await _staff.Get($"/api/admin/orders?customerId={refuser.CustomerId}")).Read<PagedResponse<OrderListItemDto>>()).Items;
        Assert.Equal(2, orders.Count);
    }

    [Fact]
    public async Task Dashboard_counts_today_top_sellers_and_refusal_rate()
    {
        var before = await (await _staff.Get("/api/admin/orders/dashboard")).Read<OrdersDashboardDto>();
        var variantId = await factory.VariantAsync();
        var customer = await factory.CustomerAsync();
        var refused = await PlaceAsync(customer, variantId, quantity: 2);
        await RefuseAsync(refused);
        await PlaceAsync(await factory.CustomerAsync(), variantId, quantity: 3);

        var after = await (await _staff.Get("/api/admin/orders/dashboard")).Read<OrdersDashboardDto>();

        Assert.Equal(before.Today.Placed + 2, after.Today.Placed);
        Assert.Equal(before.Today.Shipped + 1, after.Today.Shipped);
        Assert.Equal(before.Today.Refused + 1, after.Today.Refused);
        Assert.Equal(before.PendingNow + 1, after.PendingNow);
        Assert.Equal(14, after.Last14Days.Count);
        Assert.Equal(after.Today.Date, after.Last14Days[^1].Date);
        Assert.Equal(before.Refusals30Days.Refused + 1, after.Refusals30Days.Refused);
        Assert.InRange(after.Refusals30Days.Rate, 0m, 1m);
        Assert.True(after.TopSelling30Days.Count <= 10);
        Assert.True(after.TopSelling30Days.Select(t => t.Quantity).SequenceEqual(after.TopSelling30Days.Select(t => t.Quantity).OrderDescending()));
    }
}
