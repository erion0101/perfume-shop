using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PerfumeShop.Modules.Catalog.Contracts;
using PerfumeShop.Modules.Inventory.Contracts;
using PerfumeShop.Modules.Notifications.Contracts;
using PerfumeShop.Modules.Orders.Contracts;
using PerfumeShop.Shared.Infrastructure.Events;
using PerfumeShop.Testing;

[assembly: AssemblyFixture(typeof(PerfumeShop.Modules.Orders.Tests.Integration.OrdersApiFactory))]

namespace PerfumeShop.Modules.Orders.Tests.Integration;

/// <summary>
/// Real API + PostgreSQL with the real Customers (OTP login, addresses) and Inventory (stock) modules.
/// Catalog is a fake with controllable prices; SMS codes are captured; integration events are recorded.
/// </summary>
public sealed class OrdersApiFactory : ApiFactory, IAsyncLifetime
{
    private static int _phone = 200_000;

    internal FakeCatalogApi Catalog { get; } = new();

    internal FakeSmsSender Sms { get; } = new();

    internal RecordingOrderEvents Events { get; } = new();

    public ValueTask InitializeAsync() => new(StartAsync());

    internal AsyncServiceScope CreateScope() => Services.CreateAsyncScope();

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<ICatalogApi>();
        services.AddSingleton<ICatalogApi>(Catalog);
        services.RemoveAll<ISmsSender>();
        services.AddSingleton<ISmsSender>(Sms);
        services.AddSingleton<IIntegrationEventHandler<OrderPlacedIntegrationEvent>>(Events);
        services.AddSingleton<IIntegrationEventHandler<OrderConfirmedIntegrationEvent>>(Events);
        services.AddSingleton<IIntegrationEventHandler<OrderShippedIntegrationEvent>>(Events);
        services.AddSingleton<IIntegrationEventHandler<OrderDeliveredIntegrationEvent>>(Events);
        services.AddSingleton<IIntegrationEventHandler<OrderCashCollectedIntegrationEvent>>(Events);
        services.AddSingleton<IIntegrationEventHandler<OrderCancelledIntegrationEvent>>(Events);
        services.AddSingleton<IIntegrationEventHandler<OrderRefusedIntegrationEvent>>(Events);
        services.AddSingleton<IIntegrationEventHandler<OrderExpiredIntegrationEvent>>(Events);
    }

    /// <summary>A signed-in customer (real OTP flow) with a default address in Kosovo.</summary>
    internal async Task<TestCustomer> CustomerAsync(string? fullName = "Arbër Krasniqi", bool withAddress = true)
    {
        var phone = $"+38348{Interlocked.Increment(ref _phone):D6}";
        var anonymous = CreateClient();
        Assert.Equal(HttpStatusCode.Accepted, (await anonymous.PostJson("/api/auth/otp/request", new { phone })).StatusCode);
        var verified = await anonymous.PostJson("/api/auth/otp/verify", new { phone, code = Sms.LastCodeFor(phone) });
        var login = await verified.Read<LoginBody>();
        var client = CreateClientWithToken(login.AccessToken);

        if (fullName is not null)
        {
            await client.PutJson("/api/customers/me", new { fullName });
        }

        Guid? addressId = null;
        if (withAddress)
        {
            var address = await client.PostJson("/api/customers/me/addresses", new { city = "Lipjan", street = "Rr. Adem Jashari 12", notes = "kati 2" });
            addressId = (await address.Read<IdBody>()).Id;
        }

        return new TestCustomer(login.CustomerId, phone, addressId, client);
    }

    /// <summary>A variant sold at <paramref name="price"/> with stock received in batches (oldest first).</summary>
    internal async Task<Guid> VariantAsync(decimal price = 39.90m, params int[] batches)
    {
        var variantId = Catalog.Add(price);
        var day = new DateOnly(2026, 11, 1);
        foreach (var quantity in batches.Length == 0 ? [10] : batches)
        {
            var receipt = await CreateAdminClient().PostJson("/api/admin/inventory/receipts", new
            {
                supplierName = "Lattafa Distribution FZE",
                supplierInvoiceNumber = $"INV-{Guid.NewGuid():N}"[..16],
                supplierInvoiceDate = day,
                receivedOn = day,
                lines = new[] { new { variantId, batchCode = $"B{day:MMdd}-{Guid.NewGuid():N}"[..12], quantity, unitCost = 15m } },
            });
            receipt.EnsureSuccessStatusCode();
            day = day.AddDays(1);
        }

        return variantId;
    }

    /// <summary>The Shipping-driven steps (ship, delivered, refused, cash collected) go through IOrdersApi.</summary>
    internal async Task<PerfumeShop.Shared.Kernel.Result<OrderSummaryDto>> OrdersApiAsync(Func<IOrdersApi, Task<PerfumeShop.Shared.Kernel.Result<OrderSummaryDto>>> call)
    {
        await using var scope = CreateScope();
        return await call(scope.ServiceProvider.GetRequiredService<IOrdersApi>());
    }

    internal async Task<StockAvailabilityDto> StockAsync(Guid variantId)
    {
        await using var scope = CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IInventoryApi>().GetAvailabilityAsync(variantId, TestContext.Current.CancellationToken);
    }

    private sealed record LoginBody(string AccessToken, Guid CustomerId);

    private sealed record IdBody(Guid Id);
}

internal sealed record TestCustomer(Guid CustomerId, string Phone, Guid? AddressId, HttpClient Client)
{
    public Task<HttpResponseMessage> PlaceAsync(object items, string? key = null, Guid? addressId = null, string? fullName = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(new { items, addressId = addressId ?? AddressId, fullName, customerNote = "Thirrni para se të vini" }, options: ApiFactory.Json),
        };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        return Client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}

internal sealed class FakeCatalogApi : ICatalogApi
{
    private readonly ConcurrentDictionary<Guid, VariantSummaryDto> _variants = new();

    public Guid Add(decimal price, bool purchasable = true)
    {
        var id = Guid.NewGuid();
        _variants[id] = new VariantSummaryDto(
            id, Guid.NewGuid(), $"LAT-{id.ToString("N")[..6].ToUpperInvariant()}", null, "Lattafa", "Khamrah", "lattafa-khamrah-edp",
            "EauDeParfum", "FullBottle", 100, "Ml", price, "EUR", purchasable, purchasable, "https://cdn.example.com/khamrah.webp");
        return id;
    }

    public void SetPrice(Guid id, decimal price) => _variants[id] = _variants[id] with { PriceAmount = price };

    public Task<VariantPriceDto?> GetVariantPriceAsync(Guid variantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<VariantPriceDto?>(null);

    public Task<VariantSummaryDto?> GetVariantSummaryAsync(Guid variantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_variants.GetValueOrDefault(variantId));

    public Task<IReadOnlyDictionary<Guid, VariantSummaryDto>> GetVariantSummariesAsync(IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, VariantSummaryDto>>(variantIds.Where(_variants.ContainsKey).Distinct().ToDictionary(id => id, id => _variants[id]));
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

/// <summary>Receives every order integration event the outbox delivers; can be told to fail for an order.</summary>
internal sealed class RecordingOrderEvents :
    IIntegrationEventHandler<OrderPlacedIntegrationEvent>,
    IIntegrationEventHandler<OrderConfirmedIntegrationEvent>,
    IIntegrationEventHandler<OrderShippedIntegrationEvent>,
    IIntegrationEventHandler<OrderDeliveredIntegrationEvent>,
    IIntegrationEventHandler<OrderCashCollectedIntegrationEvent>,
    IIntegrationEventHandler<OrderCancelledIntegrationEvent>,
    IIntegrationEventHandler<OrderRefusedIntegrationEvent>,
    IIntegrationEventHandler<OrderExpiredIntegrationEvent>
{
    private readonly ConcurrentQueue<OrderIntegrationEvent> _received = new();

    public ConcurrentDictionary<Guid, bool> FailFor { get; } = new();

    public IReadOnlyList<OrderIntegrationEvent> For(Guid orderId) => [.. _received.Where(e => e.OrderId == orderId)];

    public Task HandleAsync(OrderPlacedIntegrationEvent e, CancellationToken ct = default) => Record(e);
    public Task HandleAsync(OrderConfirmedIntegrationEvent e, CancellationToken ct = default) => Record(e);
    public Task HandleAsync(OrderShippedIntegrationEvent e, CancellationToken ct = default) => Record(e);
    public Task HandleAsync(OrderDeliveredIntegrationEvent e, CancellationToken ct = default) => Record(e);
    public Task HandleAsync(OrderCashCollectedIntegrationEvent e, CancellationToken ct = default) => Record(e);
    public Task HandleAsync(OrderCancelledIntegrationEvent e, CancellationToken ct = default) => Record(e);
    public Task HandleAsync(OrderRefusedIntegrationEvent e, CancellationToken ct = default) => Record(e);
    public Task HandleAsync(OrderExpiredIntegrationEvent e, CancellationToken ct = default) => Record(e);

    private Task Record(OrderIntegrationEvent e)
    {
        if (FailFor.ContainsKey(e.OrderId))
        {
            throw new InvalidOperationException($"Handler failure for {e.OrderNumber}");
        }

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

    public static Task<HttpResponseMessage> Get(this HttpClient client, string url) => client.GetAsync(url, Ct);

    public static async Task<T> Read<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(ApiFactory.Json, Ct))!;

    public static async Task<string?> ErrorCode(this HttpResponseMessage response)
    {
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    public static object Items(params (Guid VariantId, int Quantity)[] items) =>
        items.Select(i => new { variantId = i.VariantId, quantity = i.Quantity }).ToArray();
}
