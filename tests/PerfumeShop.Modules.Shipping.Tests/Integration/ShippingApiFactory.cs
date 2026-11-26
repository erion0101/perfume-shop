using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PerfumeShop.Modules.Catalog.Contracts;
using PerfumeShop.Modules.Inventory.Contracts;
using PerfumeShop.Modules.Notifications.Contracts;
using PerfumeShop.Modules.Orders.Contracts;
using PerfumeShop.Modules.Payments.Contracts;
using PerfumeShop.Modules.Shipping.Contracts;
using PerfumeShop.Modules.Shipping.Features.Shared;
using PerfumeShop.Shared.Infrastructure.Events;
using PerfumeShop.Testing;

[assembly: AssemblyFixture(typeof(PerfumeShop.Modules.Shipping.Tests.Integration.ShippingApiFactory))]

namespace PerfumeShop.Modules.Shipping.Tests.Integration;

/// <summary>Real API + PostgreSQL with the real Orders, Inventory and Customers modules; fake Catalog and SMS.</summary>
public sealed class ShippingApiFactory : ApiFactory, IAsyncLifetime
{
    private static int _phone = 300_000;

    internal FakeCatalogApi Catalog { get; } = new();

    internal FakeSmsSender Sms { get; } = new();

    internal RecordingShippingEvents Events { get; } = new();

    /// <summary>Sale receipts count as issued unless a test says otherwise (the fiscal provider is not under test here).</summary>
    internal FakePaymentsApi Payments { get; } = new();

    public ValueTask InitializeAsync() => new(StartAsync());

    internal AsyncServiceScope CreateScope() => Services.CreateAsyncScope();

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<ICatalogApi>();
        services.AddSingleton<ICatalogApi>(Catalog);
        services.RemoveAll<IPaymentsApi>();
        services.AddSingleton<IPaymentsApi>(Payments);
        services.RemoveAll<ISmsSender>();
        services.AddSingleton<ISmsSender>(Sms);
        services.AddSingleton<IIntegrationEventHandler<ShipmentCreatedIntegrationEvent>>(Events);
        services.AddSingleton<IIntegrationEventHandler<ShipmentHandedOverIntegrationEvent>>(Events);
        services.AddSingleton<IIntegrationEventHandler<ShipmentDeliveredIntegrationEvent>>(Events);
        services.AddSingleton<IIntegrationEventHandler<ShipmentRefusedIntegrationEvent>>(Events);
        services.AddSingleton<IIntegrationEventHandler<CodSettledIntegrationEvent>>(Events);
    }

    /// <summary>A courier created by Admin through the API.</summary>
    internal async Task<CourierDto> CourierAsync(bool flammable = true, string[]? countries = null, int remittanceDays = 7)
    {
        var response = await CreateAdminClient().PostJson("/api/admin/shipping/couriers", new
        {
            name = $"Posta {Guid.NewGuid():N}"[..20],
            contactName = "Arta",
            contactPhone = "+383 38 000 000",
            enabledCountries = countries ?? ["XK"],
            deliveryFee = 2.50m,
            codFeeFixed = 1.00m,
            codFeePercent = 1.00m,
            acceptsFlammableGoods = flammable,
            remittanceSchedule = "Weekly",
            remittanceDays,
            isEnabled = true,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Read<CourierDto>();
    }

    /// <summary>
    /// A customer orders <paramref name="quantity"/> × a 39.90 € bottle (+2.00 € delivery = 41.90 € for one) from a variant
    /// with <paramref name="stock"/> units; staff confirms it unless <paramref name="confirm"/> is false.
    /// </summary>
    internal async Task<TestOrder> OrderAsync(int quantity = 1, int stock = 5, bool confirm = true)
    {
        var variantId = Catalog.Add(39.90m);
        (await CreateAdminClient().PostJson("/api/admin/inventory/receipts", new
        {
            supplierName = "Lattafa Distribution FZE",
            supplierInvoiceNumber = $"INV-{Guid.NewGuid():N}"[..16],
            supplierInvoiceDate = new DateOnly(2026, 11, 1),
            receivedOn = new DateOnly(2026, 11, 2),
            lines = new[] { new { variantId, batchCode = "B2391", quantity = stock, unitCost = 15m } },
        })).EnsureSuccessStatusCode();

        var phone = $"+38347{Interlocked.Increment(ref _phone):D6}";
        var anonymous = CreateClient();
        await anonymous.PostJson("/api/auth/otp/request", new { phone });
        var login = await (await anonymous.PostJson("/api/auth/otp/verify", new { phone, code = Sms.LastCodeFor(phone) })).Read<LoginBody>();
        var customer = CreateClientWithToken(login.AccessToken);
        await customer.PutJson("/api/customers/me", new { fullName = "Arbër Krasniqi" });
        await customer.PostJson("/api/customers/me/addresses", new { city = "Lipjan", street = "Rr. Adem Jashari 12", notes = "kati 2" });

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(new { items = new[] { new { variantId, quantity } } }, options: Json),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var placed = await customer.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, placed.StatusCode);
        var order = await placed.Read<OrderBody>();

        if (confirm)
        {
            Assert.Equal(HttpStatusCode.OK, (await CreateAdminClient().PostJson($"/api/admin/orders/{order.Id}/confirm")).StatusCode);
        }

        return new TestOrder(order.Id, order.Number, order.Total, variantId, login.CustomerId);
    }

    internal async Task<OrderSummaryDto> OrderStateAsync(Guid orderId)
    {
        await using var scope = CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<IOrdersApi>().GetOrderAsync(orderId, TestContext.Current.CancellationToken))!;
    }

    internal async Task<int> AvailableAsync(Guid variantId)
    {
        await using var scope = CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<IInventoryApi>().GetAvailabilityAsync(variantId, TestContext.Current.CancellationToken)).Available;
    }

    internal static string Tracking() => $"PK{Guid.NewGuid():N}"[..14].ToUpperInvariant() + "XK";

    private sealed record LoginBody(string AccessToken, Guid CustomerId);

    private sealed record OrderBody(Guid Id, string Number, decimal Total);
}

internal sealed record TestOrder(Guid Id, string Number, decimal Total, Guid VariantId, Guid CustomerId);

internal sealed class FakeCatalogApi : ICatalogApi
{
    private readonly ConcurrentDictionary<Guid, VariantSummaryDto> _variants = new();

    public Guid Add(decimal price)
    {
        var id = Guid.NewGuid();
        _variants[id] = new VariantSummaryDto(id, Guid.NewGuid(), $"LAT-{id.ToString("N")[..6].ToUpperInvariant()}", null, "Lattafa", "Khamrah",
            "lattafa-khamrah-edp", "EauDeParfum", "FullBottle", 100, "Ml", price, "EUR", true, true, null);
        return id;
    }

    public Task<VariantPriceDto?> GetVariantPriceAsync(Guid variantId, CancellationToken cancellationToken = default) => Task.FromResult<VariantPriceDto?>(null);

    public Task<VariantSummaryDto?> GetVariantSummaryAsync(Guid variantId, CancellationToken cancellationToken = default) => Task.FromResult(_variants.GetValueOrDefault(variantId));

    public Task<IReadOnlyDictionary<Guid, VariantSummaryDto>> GetVariantSummariesAsync(IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, VariantSummaryDto>>(variantIds.Where(_variants.ContainsKey).Distinct().ToDictionary(id => id, id => _variants[id]));
}

internal sealed class FakePaymentsApi : IPaymentsApi
{
    private readonly ConcurrentDictionary<Guid, string> _states = new();

    /// <summary>Sets the sale receipt state of an order ("Failed", "Pending", "Missing", "Issued").</summary>
    public void SetReceipt(Guid orderId, string state) => _states[orderId] = state;

    public Task<PaymentSummaryDto?> GetPaymentForOrderAsync(Guid orderId, CancellationToken cancellationToken = default) => Task.FromResult<PaymentSummaryDto?>(null);

    public Task<IReadOnlyList<ReceiptSummaryDto>> GetReceiptsForOrderAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ReceiptSummaryDto>>([]);

    public Task<IReadOnlyDictionary<Guid, ReceiptSummaryDto>> GetSaleReceiptsAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, ReceiptSummaryDto>>(orderIds
            .Select(id => (id, state: _states.GetValueOrDefault(id, "Issued")))
            .Where(x => x.state != "Missing")
            .ToDictionary(x => x.id, x => new ReceiptSummaryDto(Guid.NewGuid(), "Sale", x.state, "PS-R-TEST", null, false, 41.90m, null)));
}

internal sealed partial class FakeSmsSender : ISmsSender
{
    private readonly ConcurrentQueue<(string Phone, string Message)> _sent = new();

    public Task<SmsSendResult> SendAsync(string phoneE164, string message, CancellationToken cancellationToken = default)
    {
        _sent.Enqueue((phoneE164, message));
        return Task.FromResult(SmsSendResult.Sent());
    }

    public string LastCodeFor(string phone) => SixDigits().Match(_sent.Last(s => s.Phone == phone).Message).Value;

    [GeneratedRegex(@"\b\d{6}\b")]
    private static partial Regex SixDigits();
}

internal sealed class RecordingShippingEvents :
    IIntegrationEventHandler<ShipmentCreatedIntegrationEvent>,
    IIntegrationEventHandler<ShipmentHandedOverIntegrationEvent>,
    IIntegrationEventHandler<ShipmentDeliveredIntegrationEvent>,
    IIntegrationEventHandler<ShipmentRefusedIntegrationEvent>,
    IIntegrationEventHandler<CodSettledIntegrationEvent>
{
    private readonly ConcurrentQueue<object> _received = new();

    public IReadOnlyList<object> Received => [.. _received];

    public Task HandleAsync(ShipmentCreatedIntegrationEvent e, CancellationToken ct = default) => Record(e);
    public Task HandleAsync(ShipmentHandedOverIntegrationEvent e, CancellationToken ct = default) => Record(e);
    public Task HandleAsync(ShipmentDeliveredIntegrationEvent e, CancellationToken ct = default) => Record(e);
    public Task HandleAsync(ShipmentRefusedIntegrationEvent e, CancellationToken ct = default) => Record(e);
    public Task HandleAsync(CodSettledIntegrationEvent e, CancellationToken ct = default) => Record(e);

    private Task Record(object e)
    {
        _received.Enqueue(e);
        return Task.CompletedTask;
    }
}

internal static class Http
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static Task<HttpResponseMessage> PostJson(this HttpClient client, string url, object? body = null) =>
        client.PostAsJsonAsync(url, body ?? new { }, ApiFactory.Json, Ct);

    public static Task<HttpResponseMessage> PutJson(this HttpClient client, string url, object body) =>
        client.PutAsJsonAsync(url, body, ApiFactory.Json, Ct);

    public static Task<HttpResponseMessage> PostCsv(this HttpClient client, string url, string csv) =>
        client.PostAsync(url, new StringContent(csv, Encoding.UTF8, "text/csv"), Ct);

    public static Task<HttpResponseMessage> Get(this HttpClient client, string url) => client.GetAsync(url, Ct);

    public static async Task<T> Read<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(ApiFactory.Json, Ct))!;

    public static Task<string> Text(this HttpResponseMessage response) => response.Content.ReadAsStringAsync(Ct);

    public static async Task<string?> ErrorCode(this HttpResponseMessage response)
    {
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
