using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PerfumeShop.Modules.Orders.Contracts;
using PerfumeShop.Modules.Payments.Contracts;
using PerfumeShop.Modules.Payments.Infrastructure;
using PerfumeShop.Modules.Payments.Infrastructure.Fiscal;
using PerfumeShop.Modules.Shipping.Contracts;
using PerfumeShop.Shared.Infrastructure.Events;
using PerfumeShop.Testing;

namespace PerfumeShop.Modules.Payments.Tests.Integration;

/// <summary>Order → Shipping → Payments end to end, through the outbox, on a real database.</summary>
public sealed class PaymentFlowTests(PaymentsApiFactory factory)
{
    private const string Payments = "/api/admin/payments";

    [Fact]
    public async Task Placed_order_gets_an_expected_cash_on_delivery_payment()
    {
        var order = await factory.OrderAsync(confirm: false);
        await factory.DispatchAsync();

        var view = await factory.PaymentAsync(order.Id);

        Assert.Equal(("Expected", "CashOnDelivery", 41.90m, order.Number), (view.Payment.Status, view.Payment.Method, view.Payment.ExpectedAmount, view.Payment.OrderNumber));
        Assert.Empty(view.Receipts);
    }

    [Fact]
    public async Task Full_flow_shipment_receipt_delivery_and_settlement()
    {
        var order = await factory.OrderAsync(quantity: 2);
        var (courier, shipmentId, tracking) = await factory.ShipAsync(order);
        var admin = factory.CreateAdminClient();

        // Shipped: sale receipt issued (development = NON-FISCAL), with VAT breakdown and batch codes.
        var shipped = await factory.PaymentAsync(order.Id);
        Assert.Equal(("Expected", courier.Name, tracking), (shipped.Payment.Status, shipped.Payment.CourierName, shipped.Payment.TrackingNumber));
        var sale = shipped.Sale;
        Assert.Equal(("Issued", false, "NONFISCAL-" + sale.InternalNumber), (sale.Status, sale.IsFiscal, sale.FiscalNumber));
        Assert.Matches(@"^PS-R-\d{4}-\d{6}$", sale.InternalNumber);
        Assert.Equal(order.Number, sale.OrderNumber);
        Assert.Equal(order.Total, sale.Total);                       // 2 × 39.90 = 79.80, free delivery from 40 €
        Assert.Equal(12.17m, sale.VatTotal);
        var line = Assert.Single(sale.Lines);
        Assert.Equal((2m, 39.90m, 79.80m, "B2391×2"), (line.Quantity, line.UnitPrice, line.LineTotal, line.Batches));
        var vat = Assert.Single(sale.VatBreakdown);
        Assert.Equal((0.18m, 67.63m, 12.17m, 79.80m), (vat.Rate, vat.Net, vat.Vat, vat.Gross));
        var attempt = Assert.Single(sale.Attempts);
        Assert.True(attempt.Succeeded);
        Assert.Contains(order.Number, attempt.Request, StringComparison.Ordinal);
        Assert.Contains("NOT A FISCAL RECEIPT", attempt.Response, StringComparison.Ordinal);

        // Printable receipt says it is not fiscal.
        var print = await admin.Get($"{Payments}/receipts/{sale.Id}/print");
        Assert.Equal("text/html", print.Content.Headers.ContentType?.MediaType);
        Assert.Contains("NON-FISCAL", WebUtility.HtmlDecode(await print.Text()), StringComparison.Ordinal);

        // Delivered, then the courier statement: Collected with fee, net and settlement link.
        Assert.Equal(HttpStatusCode.OK, (await admin.PostJson($"/api/admin/shipping/shipments/{shipmentId}/handed-over")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostJson($"/api/admin/shipping/shipments/{shipmentId}/delivered")).StatusCode);
        var settlement = await admin.PostCsv($"/api/admin/shipping/couriers/{courier.Id}/settlements?reference=W48-{tracking}",
            $"tracking_number;collected_amount;fee\n{tracking};79.80;1.80\n");
        Assert.Equal(HttpStatusCode.OK, settlement.StatusCode);
        var settlementId = (await settlement.Read<IdOnly>()).Id;
        await factory.DispatchAsync();

        var paid = (await factory.PaymentAsync(order.Id)).Payment;
        Assert.Equal(("Collected", 79.80m, 1.80m, 78.00m, settlementId, $"W48-{tracking}"),
            (paid.Status, paid.CollectedAmount!.Value, paid.CourierFee!.Value, paid.NetReceived!.Value, paid.SettlementId!.Value, paid.SettlementReference));

        // Lists and the public contract agree.
        var list = await (await admin.Get($"{Payments}?status=Collected&orderNumber={order.Number}")).Read<Page<PaymentBody>>();
        Assert.Equal(paid.Id, Assert.Single(list.Items).Id);
        var byCourier = await (await admin.Get($"{Payments}?courier={Uri.EscapeDataString(courier.Name)}")).Read<Page<PaymentBody>>();
        Assert.Contains(byCourier.Items, p => p.OrderId == order.Id);

        await using var scope = factory.CreateScope();
        var api = scope.ServiceProvider.GetRequiredService<IPaymentsApi>();
        var summary = await api.GetPaymentForOrderAsync(order.Id, TestContext.Current.CancellationToken);
        Assert.Equal(("Collected", 78.00m), (summary!.Status, summary.NetReceived!.Value));
        Assert.Equal("Sale", Assert.Single(await api.GetReceiptsForOrderAsync(order.Id, TestContext.Current.CancellationToken)).Kind);
    }

    [Fact]
    public async Task Daily_summary_per_courier()
    {
        var courier = await factory.CourierAsync();
        var first = await factory.OrderAsync();
        var second = await factory.OrderAsync();
        var (_, firstShipment, firstTracking) = await factory.ShipAsync(first, courier);
        await factory.ShipAsync(second, courier);
        var admin = factory.CreateAdminClient();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostJson($"/api/admin/shipping/shipments/{firstShipment}/handed-over")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostJson($"/api/admin/shipping/shipments/{firstShipment}/delivered")).StatusCode);
        await admin.PostCsv($"/api/admin/shipping/couriers/{courier.Id}/settlements?reference=D-{firstTracking}",
            $"tracking_number;collected_amount;fee\n{firstTracking};41.90;1.42\n");
        await factory.DispatchAsync();

        var response = await admin.Get($"{Payments}/daily-summary");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = await response.Read<DailySummary>();

        var row = Assert.Single(summary.Couriers, c => c.Courier == courier.Name);
        Assert.Equal((2, 83.80m, 1, 41.90m, 1.42m, 40.48m, 41.90m),
            (row.ShippedCount, row.Expected, row.CollectedCount, row.Collected, row.Fees, row.Net, row.OutstandingAtCourier));
        Assert.True(summary.Total.Expected >= row.Expected);

        // Dashboard totals: today's collected cash is part of this week's, the second parcel is still at the courier.
        var totals = await (await admin.Get($"{Payments}/totals")).Read<Totals>();
        Assert.True(totals.CollectedToday >= 41.90m);
        Assert.True(totals.CollectedThisWeek >= totals.CollectedToday);
        Assert.True(totals.OutstandingAtCouriers >= 41.90m);
        Assert.Equal(DayOfWeek.Monday, totals.WeekStart.DayOfWeek);
    }

    private sealed record Totals(DateOnly Today, decimal CollectedToday, decimal CollectedThisWeek, DateOnly WeekStart, decimal OutstandingAtCouriers, int FailedReceipts);

    [Fact]
    public async Task Refused_parcel_fails_the_payment_and_issues_a_linked_storno()
    {
        var order = await factory.OrderAsync();
        var (_, shipmentId, _) = await factory.ShipAsync(order);
        var admin = factory.CreateAdminClient();

        Assert.Equal(HttpStatusCode.OK, (await admin.PostJson($"/api/admin/shipping/shipments/{shipmentId}/handed-over")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostJson($"/api/admin/shipping/shipments/{shipmentId}/refused", new { note = "Klienti refuzoi" })).StatusCode);
        await factory.DispatchAsync();
        Assert.DoesNotContain((await factory.PaymentAsync(order.Id)).Receipts, r => r.Kind == "Storno");   // parcel still with the courier

        Assert.Equal(HttpStatusCode.OK, (await admin.PostJson($"/api/admin/shipping/shipments/{shipmentId}/returned", new { note = "Kthyer" })).StatusCode);
        await factory.DispatchAsync();

        var view = await factory.PaymentAsync(order.Id);
        Assert.Equal("Failed", view.Payment.Status);
        var sale = view.Sale;
        var storno = Assert.Single(view.Receipts, r => r.Kind == "Storno");
        Assert.Equal(("Issued", sale.Id), (storno.Status, storno.OriginalReceiptId!.Value));
        Assert.Equal((-sale.Total, -sale.VatTotal), (storno.Total, storno.VatTotal));
        Assert.All(storno.Lines, l => Assert.True(l.Quantity < 0));
        Assert.Contains("KUPON STORNO", WebUtility.HtmlDecode(await (await admin.Get($"{Payments}/receipts/{storno.Id}/print")).Text()), StringComparison.Ordinal);

        var receipts = await (await admin.Get($"{Payments}/receipts?kind=Storno&orderNumber={order.Number}")).Read<Page<ReceiptBody>>();
        Assert.Equal(storno.Id, Assert.Single(receipts.Items).Id);
    }

    [Fact]
    public async Task Cancelled_order_cancels_the_payment_without_receipts()
    {
        var order = await factory.OrderAsync();
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateAdminClient().PostJson($"/api/admin/orders/{order.Id}/cancel", new { reason = "Nuk përgjigjet" })).StatusCode);
        await factory.DispatchAsync();

        var view = await factory.PaymentAsync(order.Id);

        Assert.Equal("Cancelled", view.Payment.Status);
        Assert.Empty(view.Receipts);
    }

    [Fact]
    public async Task Failed_fiscal_service_is_recorded_and_retried()
    {
        var order = await factory.OrderAsync();
        factory.Fiscal.Fail = true;
        try
        {
            await factory.ShipAsync(order);
        }
        finally
        {
            factory.Fiscal.Fail = false;
        }

        var failed = (await factory.PaymentAsync(order.Id)).Sale;
        Assert.Equal(("Failed", "Fiscal service did not answer (test)."), (failed.Status, failed.LastError));
        var attempt = Assert.Single(failed.Attempts);
        Assert.False(attempt.Succeeded);
        Assert.Equal("timeout", System.Text.Json.JsonDocument.Parse(attempt.Response!).RootElement.GetProperty("error").GetString());
        var admin = factory.CreateAdminClient();
        var listed = await (await admin.Get($"{Payments}/receipts?status=Failed&orderNumber={order.Number}")).Read<Page<ReceiptBody>>();
        Assert.Single(listed.Items);
        Assert.Contains("NUK ËSHTË LËSHUAR", WebUtility.HtmlDecode(await (await admin.Get($"{Payments}/receipts/{failed.Id}/print")).Text()), StringComparison.Ordinal);

        await using (var scope = factory.CreateScope())
        {
            var outbox = await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().OutboxMessages.AsNoTracking()
                .Where(m => m.Type.Contains(nameof(FiscalReceiptFailedIntegrationEvent)))
                .ToListAsync(TestContext.Current.CancellationToken);
            Assert.Single(outbox, m => m.Content.Contains(failed.InternalNumber, StringComparison.Ordinal)); // → staff alert (Notifications)
        }

        var retry = await admin.PostJson($"{Payments}/receipts/{failed.Id}/retry");
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var issued = await retry.Read<ReceiptBody>();
        Assert.Equal(("Issued", 2, failed.InternalNumber), (issued.Status, issued.Attempts.Count, issued.InternalNumber));

        var again = await admin.PostJson($"{Payments}/receipts/{failed.Id}/retry");
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Handlers_apply_each_event_once()
    {
        var order = await factory.OrderAsync();
        var (courier, shipmentId, tracking) = await factory.ShipAsync(order);
        var orderState = await factory.OrderStateAsync(order.Id);
        var now = DateTime.UtcNow;
        var shipped = new OrderShippedIntegrationEvent(Guid.NewGuid(), now, order.Id, order.Number, order.CustomerId, courier.Name, tracking, order.Total);
        var settled = new CodSettledIntegrationEvent(Guid.NewGuid(), now, Guid.NewGuid(), courier.Id, courier.Name, "replay", 1, order.Total, 1.42m, order.Total - 1.42m, 0,
            [new CodSettledLine(shipmentId, order.Id, order.Number, tracking, order.Total, 1.42m)]);

        // The same event delivered twice (outbox at-least-once), and a new event id for the same shipment.
        for (var i = 0; i < 2; i++)
        {
            await PublishAsync(shipped);
            await PublishAsync(settled);
        }

        await PublishAsync(shipped with { Id = Guid.NewGuid() });

        var view = await factory.PaymentAsync(order.Id);
        Assert.Single(view.Receipts);
        Assert.Single(view.Sale.Attempts);
        Assert.Equal(("Collected", 1.42m, settled.SettlementId), (view.Payment.Status, view.Payment.CourierFee!.Value, view.Payment.SettlementId!.Value));
        Assert.Equal("Shipped", orderState.Status);

        await using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        Assert.Equal(1, await db.Payments.CountAsync(p => p.OrderId == order.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Events_arriving_out_of_order_still_create_the_payment()
    {
        var order = await factory.OrderAsync(confirm: false);   // OrderPlaced not dispatched yet

        await PublishAsync(new OrderCancelledIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, order.Id, order.Number, order.CustomerId, "Staff", "test"));
        await factory.DispatchAsync();                           // the late OrderPlaced changes nothing

        Assert.Equal("Cancelled", (await factory.PaymentAsync(order.Id)).Payment.Status);
    }

    [Fact]
    public async Task Unknown_order_and_receipt_return_404()
    {
        var admin = factory.CreateAdminClient();

        Assert.Equal(HttpStatusCode.NotFound, (await admin.Get($"{Payments}/orders/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Get($"{Payments}/receipts/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostJson($"{Payments}/receipts/{Guid.NewGuid()}/retry")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Get($"{Payments}?status=Paid")).StatusCode);
    }

    [Fact]
    public async Task Customers_and_anonymous_users_cannot_see_payments()
    {
        var customer = factory.CreateClientWithToken(ApiFactory.CreateTestToken(["Customer"], customerId: Guid.NewGuid()));
        var staff = factory.CreateClientWithToken(ApiFactory.CreateTestToken(["Staff"]));

        Assert.Equal(HttpStatusCode.Forbidden, (await customer.Get(Payments)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.Get($"{Payments}/receipts")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().Get($"{Payments}/daily-summary")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.Get($"{Payments}/receipts")).StatusCode);
    }

    private async Task PublishAsync<TEvent>(TEvent e)
        where TEvent : PerfumeShop.Shared.Kernel.IIntegrationEvent
    {
        await using var scope = factory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IEventBus>().PublishAsync(e, TestContext.Current.CancellationToken);
    }

    private sealed record IdOnly(Guid Id);

    private sealed record Page<T>(IReadOnlyList<T> Items, int TotalCount);

    private sealed record CourierRow(string Courier, int ShippedCount, decimal Expected, int CollectedCount, decimal Collected, decimal Fees, decimal Net, decimal OutstandingAtCourier);

    private sealed record DailySummary(DateOnly Date, IReadOnlyList<CourierRow> Couriers, CourierRow Total, int FailedReceipts);
}

/// <summary>The NON-FISCAL development provider can only exist in Development; everywhere else issuing fails loudly.</summary>
public sealed class FiscalProviderGuardTests(PaymentsApiFactory shared)
{
    private sealed class HostIn(string connectionString, string environment) : ApiFactory(connectionString)
    {
        protected override string EnvironmentName => environment;
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public async Task Development_fiscal_provider_exists_only_in_development(string environment)
    {
        await using var host = new HostIn(shared.ConnectionString, environment);
        await using var scope = host.Services.CreateAsyncScope();

        var service = scope.ServiceProvider.GetRequiredService<IFiscalReceiptService>();

        Assert.IsType<UnconfiguredFiscalReceiptService>(service);
        Assert.DoesNotContain(scope.ServiceProvider.GetServices<IFiscalReceiptService>(), s => s is DevelopmentFiscalReceiptService);
    }

    [Fact]
    public async Task Development_uses_the_non_fiscal_provider()
    {
        await using var host = new HostIn(shared.ConnectionString, "Development");
        await using var scope = host.Services.CreateAsyncScope();

        Assert.IsType<DevelopmentFiscalReceiptService>(scope.ServiceProvider.GetRequiredService<IFiscalReceiptService>());
    }
}
