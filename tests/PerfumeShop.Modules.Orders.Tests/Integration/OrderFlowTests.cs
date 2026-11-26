using System.Net;
using PerfumeShop.Modules.Orders.Contracts;
using PerfumeShop.Modules.Orders.Features.ListOrders;
using PerfumeShop.Modules.Orders.Features.Shared;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Kernel;
using static PerfumeShop.Modules.Orders.Tests.Integration.Http;

namespace PerfumeShop.Modules.Orders.Tests.Integration;

/// <summary>
/// Whole lifecycles across Orders, Inventory and Customers. Staff confirm/cancel over HTTP; the steps
/// Shipping owns (ship, delivered, refused, cash collected) go through IOrdersApi, exactly as Shipping calls them.
/// </summary>
public sealed class OrderFlowTests(OrdersApiFactory factory)
{
    private const string Staff = "/api/admin/orders";
    private const string StaffUser = "staff-user-1";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly HttpClient _staff = factory.CreateAdminClient();

    private async Task<OrderAdminDto> Step(Guid orderId, string action, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await _staff.PostJson($"{Staff}/{orderId}/{action}", body);
        Assert.True(response.StatusCode == expected, $"{action}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        return expected == HttpStatusCode.OK ? await response.Read<OrderAdminDto>() : null!;
    }

    private Task<Result<OrderSummaryDto>> Ship(Guid id, string courier = "Posta e Kosovës", string tracking = "PK1") =>
        factory.OrdersApiAsync(api => api.MarkShippedAsync(id, courier, tracking, StaffUser, Ct));

    private Task<Result<OrderSummaryDto>> Deliver(Guid id) => factory.OrdersApiAsync(api => api.MarkDeliveredAsync(id, StaffUser, Ct));

    private Task<Result<OrderSummaryDto>> Refuse(Guid id) => factory.OrdersApiAsync(api => api.MarkRefusedAsync(id, "Klienti refuzoi pakon", StaffUser, Ct));

    private Task<Result<OrderSummaryDto>> Collect(Guid id, decimal amount, string? note = null) =>
        factory.OrdersApiAsync(api => api.MarkCashCollectedAsync(id, amount, note, StaffUser, Ct));

    private async Task<OrderAdminDto> AdminView(Guid id) => await (await _staff.Get($"{Staff}/{id}")).Read<OrderAdminDto>();

    [Fact]
    public async Task Place_confirm_ship_deliver_cash_collected()
    {
        var customer = await factory.CustomerAsync();
        var khamrah = await factory.VariantAsync(19.90m, 1, 5); // two batches: oldest has 1 unit
        var order = await (await customer.PlaceAsync(Items((khamrah, 2)))).Read<OrderDto>();
        Assert.Equal(4, (await factory.StockAsync(khamrah)).Available); // 6 received, 2 reserved

        await Step(order.Id, "confirm");
        var shipped = (await Ship(order.Id, tracking: "PK123456789")).Ok();
        (await Deliver(order.Id)).Ok();
        var collected = (await Collect(order.Id, order.Total)).Ok();

        Assert.Equal(2, shipped.Lines.Single().Batches.Count); // FIFO: 1 from the older batch, 1 from the newer
        Assert.All(shipped.Lines.Single().Batches, b => Assert.EndsWith("×1", b, StringComparison.Ordinal));
        Assert.Equal("CashCollected", collected.Status);
        var admin = await AdminView(order.Id);
        Assert.Equal(order.Total, admin.CashCollectedAmount);
        Assert.Equal(["Pending", "Confirmed", "Shipped", "Delivered", "CashCollected"], admin.Order.Timeline.Select(t => t.To));
        Assert.All(admin.Order.Timeline.Skip(1), t => Assert.Equal("Staff", t.ActorKind));
        Assert.Equal(4, (await factory.StockAsync(khamrah)).Available); // 6 received − 2 shipped

        var customerView = await (await customer.Client.Get($"/api/orders/{order.Id}")).Read<OrderDto>();
        Assert.All(customerView.Timeline.Skip(1), t => Assert.Equal("staff", t.ActorId)); // no staff ids for customers
    }

    [Fact]
    public async Task Shipping_steps_are_idempotent_for_retries()
    {
        var customer = await factory.CustomerAsync();
        var khamrah = await factory.VariantAsync(39.90m, 3);
        var order = await (await customer.PlaceAsync(Items((khamrah, 1)))).Read<OrderDto>();
        await Step(order.Id, "confirm");

        (await Ship(order.Id, tracking: "PK-RETRY")).Ok();
        (await Ship(order.Id, tracking: "PK-RETRY")).Ok();
        (await Deliver(order.Id)).Ok();
        (await Deliver(order.Id)).Ok();
        (await Collect(order.Id, order.Total)).Ok();
        (await Collect(order.Id, order.Total)).Ok();

        Assert.Equal(5, (await AdminView(order.Id)).Order.Timeline.Count);
        Assert.Equal(2, (await factory.StockAsync(khamrah)).Available); // committed once
    }

    [Fact]
    public async Task The_removed_staff_endpoints_are_gone()
    {
        var customer = await factory.CustomerAsync();
        var khamrah = await factory.VariantAsync();
        var order = await (await customer.PlaceAsync(Items((khamrah, 1)))).Read<OrderDto>();
        await Step(order.Id, "confirm");

        foreach (var action in new[] { "ship", "delivered", "refused", "cash-collected" })
        {
            var response = await _staff.PostJson($"{Staff}/{order.Id}/{action}", new { courierName = "X", trackingNumber = "Y", amount = 1m });
            Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, $"{action} still answers {(int)response.StatusCode}");
        }

        Assert.Equal("Confirmed", (await AdminView(order.Id)).Order.Status);
    }

    [Fact]
    public async Task Refused_parcel_returns_the_units_to_stock()
    {
        var customer = await factory.CustomerAsync();
        var khamrah = await factory.VariantAsync(39.90m, 3);
        var order = await (await customer.PlaceAsync(Items((khamrah, 2)))).Read<OrderDto>();
        await Step(order.Id, "confirm");
        (await Ship(order.Id)).Ok();
        Assert.Equal(1, (await factory.StockAsync(khamrah)).Available);

        var refused = (await Refuse(order.Id)).Ok();

        Assert.Equal("Refused", refused.Status);
        Assert.Equal(3, (await factory.StockAsync(khamrah)).Available);
    }

    [Fact]
    public async Task Customer_cancels_a_pending_order_and_the_stock_is_released()
    {
        var customer = await factory.CustomerAsync();
        var khamrah = await factory.VariantAsync(39.90m, 2);
        var order = await (await customer.PlaceAsync(Items((khamrah, 2)))).Read<OrderDto>();

        var cancelled = await customer.Client.PostJson($"/api/orders/{order.Id}/cancel", new { reason = "Ndryshova mendje" });

        Assert.Equal("Cancelled", (await cancelled.Read<OrderDto>()).Status);
        Assert.Equal(2, (await factory.StockAsync(khamrah)).Available);
    }

    [Fact]
    public async Task Once_confirmed_only_staff_can_cancel_and_they_need_a_reason()
    {
        var customer = await factory.CustomerAsync();
        var khamrah = await factory.VariantAsync(39.90m, 2);
        var order = await (await customer.PlaceAsync(Items((khamrah, 1)))).Read<OrderDto>();
        await Step(order.Id, "confirm");

        var byCustomer = await customer.Client.PostJson($"/api/orders/{order.Id}/cancel", new { reason = "late" });
        await Step(order.Id, "cancel", new { reason = "" }, HttpStatusCode.BadRequest);
        await Step(order.Id, "cancel", new { reason = "Klienti nuk përgjigjet në telefon" });

        Assert.Equal(HttpStatusCode.Conflict, byCustomer.StatusCode);
        Assert.Equal(2, (await factory.StockAsync(khamrah)).Available);
    }

    [Fact]
    public async Task Invalid_steps_are_rejected_and_shipping_needs_courier_data()
    {
        var customer = await factory.CustomerAsync();
        var khamrah = await factory.VariantAsync();
        var order = await (await customer.PlaceAsync(Items((khamrah, 1)))).Read<OrderDto>();

        (await Deliver(order.Id)).Fails("Orders.Order.InvalidTransition");
        (await Ship(order.Id)).Fails("Orders.Order.InvalidTransition");
        await Step(order.Id, "confirm");
        (await Ship(order.Id, courier: "", tracking: "")).Fails("Orders.Order.CourierRequired");

        Assert.Equal(9, (await factory.StockAsync(khamrah)).Available); // nothing was committed
    }

    [Fact]
    public async Task Collected_cash_different_from_the_total_needs_a_note()
    {
        var customer = await factory.CustomerAsync();
        var khamrah = await factory.VariantAsync();
        var order = await (await customer.PlaceAsync(Items((khamrah, 1)))).Read<OrderDto>();
        await Step(order.Id, "confirm");
        (await Ship(order.Id)).Ok();
        (await Deliver(order.Id)).Ok();

        (await Collect(order.Id, 40.00m)).Fails("Orders.Order.CollectionNoteRequired");
        (await Collect(order.Id, 40.00m, "Korrieri ktheu 1.90 € më pak")).Ok();

        Assert.Equal(40.00m, (await AdminView(order.Id)).CashCollectedAmount);
    }

    [Fact]
    public async Task Staff_notes_list_filters_and_customer_privacy()
    {
        var customer = await factory.CustomerAsync();
        var other = await factory.CustomerAsync();
        var khamrah = await factory.VariantAsync();
        var order = await (await customer.PlaceAsync(Items((khamrah, 1)))).Read<OrderDto>();

        var noted = await _staff.PostJson($"{Staff}/{order.Id}/notes", new { text = "Klienti kërkoi dorëzim pas orës 17" });
        var byNumber = await (await _staff.Get($"{Staff}?number={order.Number}")).Read<PagedResponse<OrderListItemDto>>();
        var byPhone = await (await _staff.Get($"{Staff}?phone={customer.Phone[^6..]}&status=Pending")).Read<PagedResponse<OrderListItemDto>>();
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var byDate = await (await _staff.Get($"{Staff}?from={today}&to={today}&pageSize=200")).Read<PagedResponse<OrderListItemDto>>();
        var foreign = await other.Client.Get($"/api/orders/{order.Id}");

        Assert.Single((await noted.Read<OrderAdminDto>()).Notes);
        Assert.Equal(order.Id, Assert.Single(byNumber.Items).Id);
        Assert.Contains(byPhone.Items, i => i.Id == order.Id);
        Assert.Contains(byDate.Items, i => i.Id == order.Id);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.DoesNotContain("Notes", await (await customer.Client.Get($"/api/orders/{order.Id}")).Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Too_many_refused_parcels_block_new_orders()
    {
        var admin = factory.CreateAdminClient();
        await admin.PutJson($"{Staff}/settings", new { maxOpenOrdersPerCustomer = 2, maxRefusedOrders = 0, pendingExpiryHours = 48 });
        try
        {
            var customer = await factory.CustomerAsync();
            var khamrah = await factory.VariantAsync();
            var first = await (await customer.PlaceAsync(Items((khamrah, 1)))).Read<OrderDto>();
            await Step(first.Id, "confirm");
            (await Ship(first.Id)).Ok();
            (await Refuse(first.Id)).Ok();

            var next = await customer.PlaceAsync(Items((khamrah, 1)));

            Assert.Equal(HttpStatusCode.Forbidden, next.StatusCode);
            Assert.Equal("Orders.Customer.TooManyRefusals", await next.ErrorCode());
        }
        finally
        {
            await admin.PutJson($"{Staff}/settings", new { maxOpenOrdersPerCustomer = 2, maxRefusedOrders = 2, pendingExpiryHours = 48 });
        }
    }
}
