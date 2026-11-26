using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using PerfumeShop.Modules.Customers.Contracts;
using PerfumeShop.Modules.Orders.Features.Shared;
using static PerfumeShop.Modules.Orders.Tests.Integration.Http;

namespace PerfumeShop.Modules.Orders.Tests.Integration;

public sealed class PlaceOrderTests(OrdersApiFactory factory)
{
    [Fact]
    public async Task Placing_an_order_snapshots_prices_reserves_stock_and_numbers_it()
    {
        var customer = await factory.CustomerAsync();
        var khamrah = await factory.VariantAsync(39.90m, 5);

        var response = await customer.PlaceAsync(Items((khamrah, 2)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await response.Read<OrderDto>();
        Assert.Matches(new Regex(@"^PS-\d{4}-\d{6}$"), order.Number);
        Assert.Equal(("Pending", "CashOnDelivery", 79.80m, 0m, 79.80m, 12.17m), (order.Status, order.PaymentMethod, order.Subtotal, order.DeliveryFee, order.Total, order.VatAmount));
        Assert.Equal(("Arbër Krasniqi", customer.Phone, "Lipjan"), (order.CustomerName, order.CustomerPhone, order.DeliveryAddress.City));
        Assert.Equal((39.90m, 2, 79.80m), (order.Lines.Single().UnitPrice, order.Lines.Single().Quantity, order.Lines.Single().LineTotal));
        Assert.True(order.CanCancel);
        Assert.Equal(3, (await factory.StockAsync(khamrah)).Available);
    }

    [Fact]
    public async Task Later_price_changes_do_not_change_the_order()
    {
        var customer = await factory.CustomerAsync();
        var khamrah = await factory.VariantAsync(39.90m);
        var placed = await (await customer.PlaceAsync(Items((khamrah, 1)))).Read<OrderDto>();

        factory.Catalog.SetPrice(khamrah, 59.90m);
        var later = await (await customer.Client.Get($"/api/orders/{placed.Id}")).Read<OrderDto>();

        Assert.Equal(39.90m, later.Lines.Single().UnitPrice);
        Assert.Equal(41.90m, later.Total);
    }

    [Fact]
    public async Task Same_idempotency_key_returns_the_same_order()
    {
        var customer = await factory.CustomerAsync();
        var khamrah = await factory.VariantAsync(39.90m, 5);
        var key = Guid.NewGuid().ToString();

        var first = await customer.PlaceAsync(Items((khamrah, 1)), key);
        var second = await customer.PlaceAsync(Items((khamrah, 1)), key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("true", second.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal((await first.Read<OrderDto>()).Id, (await second.Read<OrderDto>()).Id);
        Assert.Equal(4, (await factory.StockAsync(khamrah)).Available);
    }

    [Fact]
    public async Task A_double_click_creates_one_order_and_reserves_once()
    {
        var customer = await factory.CustomerAsync();
        var khamrah = await factory.VariantAsync(39.90m, 5);
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => customer.PlaceAsync(Items((khamrah, 1)), key)));

        var ids = await Task.WhenAll(responses.Select(async r => (await r.Read<OrderDto>()).Id));
        Assert.Single(ids.Distinct());
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Equal(4, (await factory.StockAsync(khamrah)).Available);
    }

    [Fact]
    public async Task Reusing_a_key_for_a_different_order_is_a_conflict()
    {
        var customer = await factory.CustomerAsync();
        var khamrah = await factory.VariantAsync(39.90m, 5);
        var key = Guid.NewGuid().ToString();
        await customer.PlaceAsync(Items((khamrah, 1)), key);

        var different = await customer.PlaceAsync(Items((khamrah, 2)), key);

        Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);
        Assert.Equal("Orders.Order.IdempotencyKeyReused", await different.ErrorCode());
    }

    [Fact]
    public async Task Idempotency_key_header_is_required()
    {
        var customer = await factory.CustomerAsync();
        var khamrah = await factory.VariantAsync();

        var response = await customer.Client.PostJson("/api/orders", new { items = Items((khamrah, 1)) });

        Assert.Equal("Orders.Order.IdempotencyKeyMissing", await response.ErrorCode());
    }

    [Fact]
    public async Task Full_name_is_required_once_and_saved_on_the_profile()
    {
        var customer = await factory.CustomerAsync(fullName: null);
        var khamrah = await factory.VariantAsync();

        var withoutName = await customer.PlaceAsync(Items((khamrah, 1)));
        var withName = await customer.PlaceAsync(Items((khamrah, 1)), fullName: "Drita Berisha");

        Assert.Equal("Orders.Order.FullNameRequired", await withoutName.ErrorCode());
        Assert.Equal(HttpStatusCode.Created, withName.StatusCode);
        await using var scope = factory.CreateScope();
        var profile = await scope.ServiceProvider.GetRequiredService<ICustomersApi>().GetCustomerSummaryAsync(customer.CustomerId, TestContext.Current.CancellationToken);
        Assert.Equal("Drita Berisha", profile!.FullName);
    }

    [Fact]
    public async Task Not_enough_stock_reserves_nothing()
    {
        var customer = await factory.CustomerAsync();
        var plenty = await factory.VariantAsync(20m, 10);
        var scarce = await factory.VariantAsync(20m, 1);

        var response = await customer.PlaceAsync(Items((plenty, 2), (scarce, 2)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(10, (await factory.StockAsync(plenty)).Available);
    }

    [Fact]
    public async Task An_address_is_required()
    {
        var customer = await factory.CustomerAsync(withAddress: false);
        var khamrah = await factory.VariantAsync();

        var response = await customer.PlaceAsync(Items((khamrah, 1)));

        Assert.Equal("Orders.Order.AddressRequired", await response.ErrorCode());
    }

    [Fact]
    public async Task At_most_two_open_cash_on_delivery_orders()
    {
        var customer = await factory.CustomerAsync();
        var khamrah = await factory.VariantAsync(20m, 10);
        await customer.PlaceAsync(Items((khamrah, 1)));
        await customer.PlaceAsync(Items((khamrah, 1)));

        var third = await customer.PlaceAsync(Items((khamrah, 1)));

        Assert.Equal(HttpStatusCode.Conflict, third.StatusCode);
        Assert.Equal("Orders.Customer.TooManyOpenOrders", await third.ErrorCode());
        Assert.Equal(8, (await factory.StockAsync(khamrah)).Available);
    }

    [Fact]
    public async Task Blocked_customers_cannot_order()
    {
        var customer = await factory.CustomerAsync();
        var khamrah = await factory.VariantAsync();
        await factory.CreateAdminClient().PostJson($"/api/admin/customers/{customer.CustomerId}/block", new { reason = "Porosi të rreme" });

        var response = await customer.PlaceAsync(Items((khamrah, 1)));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Ordering_needs_a_customer_token()
    {
        var staff = factory.CreateAdminClient();

        var response = await staff.PostJson("/api/orders", new { items = Items((Guid.NewGuid(), 1)) });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
