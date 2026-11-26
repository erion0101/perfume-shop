using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PerfumeShop.Modules.Orders.Contracts;
using PerfumeShop.Modules.Orders.Features.Shared;
using PerfumeShop.Modules.Orders.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Outbox;
using static PerfumeShop.Modules.Orders.Tests.Integration.Http;

namespace PerfumeShop.Modules.Orders.Tests.Integration;

/// <summary>The recurring jobs (expiry, outbox), run directly instead of through Hangfire.</summary>
public sealed class BackgroundJobTests(OrdersApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<int> DispatchOutboxAsync()
    {
        await using var scope = factory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<OutboxProcessor<OrdersDbContext>>().ProcessAsync(Ct);
    }

    [Fact]
    public async Task Unconfirmed_orders_expire_and_release_their_stock()
    {
        var customer = await factory.CustomerAsync();
        var khamrah = await factory.VariantAsync(39.90m, 2);
        var order = await (await customer.PlaceAsync(Items((khamrah, 2)))).Read<OrderDto>();
        Assert.Equal(0, (await factory.StockAsync(khamrah)).Available);

        await using (var scope = factory.CreateScope())
        {
            var job = ActivatorUtilities.CreateInstance<OrderExpiryJob>(scope.ServiceProvider, new FixedTimeProvider(DateTimeOffset.UtcNow.AddHours(49)));
            Assert.True(await job.ExpireDueOrdersAsync(Ct) >= 1);
        }

        var expired = await (await customer.Client.Get($"/api/orders/{order.Id}")).Read<OrderDto>();
        Assert.Equal("Expired", expired.Status);
        Assert.Equal("System", expired.Timeline.Last().ActorKind);
        Assert.Equal(2, (await factory.StockAsync(khamrah)).Available);

        await DispatchOutboxAsync();
        Assert.IsType<OrderExpiredIntegrationEvent>(factory.Events.For(order.Id).Last());
    }

    [Fact]
    public async Task Every_status_change_reaches_the_handlers_in_order_exactly_once()
    {
        var customer = await factory.CustomerAsync();
        var khamrah = await factory.VariantAsync();
        var order = await (await customer.PlaceAsync(Items((khamrah, 1)))).Read<OrderDto>();
        await factory.CreateAdminClient().PostJson($"/api/admin/orders/{order.Id}/confirm");
        Assert.True((await factory.OrdersApiAsync(api => api.MarkShippedAsync(order.Id, "Posta e Kosovës", "PK777", "staff-user-1", Ct))).IsSuccess);

        await DispatchOutboxAsync();
        await DispatchOutboxAsync(); // nothing is delivered twice

        var events = factory.Events.For(order.Id);
        Assert.Equal([typeof(OrderPlacedIntegrationEvent), typeof(OrderConfirmedIntegrationEvent), typeof(OrderShippedIntegrationEvent)], events.Select(e => e.GetType()));
        var shipped = Assert.IsType<OrderShippedIntegrationEvent>(events[2]);
        Assert.Equal(("PK777", order.Number, order.Total), (shipped.TrackingNumber, shipped.OrderNumber, shipped.Total));

        await using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var ids = events.Select(e => e.Id).ToList();
        Assert.Equal(3, await db.OutboxMessages.CountAsync(m => ids.Contains(m.Id) && m.ProcessedOnUtc != null && m.Attempts == 1, Ct));
    }

    [Fact]
    public async Task A_failing_handler_is_retried_until_it_succeeds()
    {
        var customer = await factory.CustomerAsync();
        var khamrah = await factory.VariantAsync();
        var order = await (await customer.PlaceAsync(Items((khamrah, 1)))).Read<OrderDto>();
        factory.Events.FailFor[order.Id] = true;

        await DispatchOutboxAsync();

        await using (var scope = factory.CreateScope())
        {
            var unprocessed = await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().OutboxMessages.AsNoTracking()
                .Where(m => m.ProcessedOnUtc == null).ToListAsync(Ct);
            var message = unprocessed.Single(m => m.Content.Contains(order.Number, StringComparison.Ordinal));
            Assert.Null(message.ProcessedOnUtc);
            Assert.Equal(1, message.Attempts);
            Assert.Contains("Handler failure", message.Error, StringComparison.Ordinal);
        }

        factory.Events.FailFor.TryRemove(order.Id, out _);
        await DispatchOutboxAsync();

        Assert.IsType<OrderPlacedIntegrationEvent>(Assert.Single(factory.Events.For(order.Id)));
    }

    [Fact]
    public async Task Orders_api_returns_the_snapshot_for_other_modules()
    {
        var customer = await factory.CustomerAsync();
        var khamrah = await factory.VariantAsync(44.00m);
        var order = await (await customer.PlaceAsync(Items((khamrah, 1)))).Read<OrderDto>();

        await using var scope = factory.CreateScope();
        var api = scope.ServiceProvider.GetRequiredService<IOrdersApi>();
        var byId = await api.GetOrderAsync(order.Id, Ct);
        var byNumber = await api.GetOrderByNumberAsync(order.Number.ToLowerInvariant(), Ct);

        Assert.Equal((order.Number, "Pending", 44.00m, 0m, "CashOnDelivery"), (byId!.OrderNumber, byId.Status, byId.Subtotal, byId.DeliveryFee, byId.PaymentMethod));
        Assert.Equal(byId.OrderId, byNumber!.OrderId);
        Assert.Equal("Lipjan", byId.DeliveryAddress.City);
    }
}
