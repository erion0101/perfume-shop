using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PerfumeShop.Modules.Catalog.Contracts;
using PerfumeShop.Modules.Notifications.Contracts;
using PerfumeShop.Modules.Notifications.Domain;
using PerfumeShop.Modules.Notifications.Infrastructure;
using PerfumeShop.Modules.Shipping.Contracts;
using PerfumeShop.Shared.Infrastructure.Events;
using PerfumeShop.Shared.Infrastructure.Jobs;
using PerfumeShop.Shared.Infrastructure.Outbox;
using PerfumeShop.Shared.Kernel;
using PerfumeShop.Testing;

[assembly: AssemblyFixture(typeof(PerfumeShop.Modules.Notifications.Tests.Integration.NotificationsApiFactory))]

namespace PerfumeShop.Modules.Notifications.Tests.Integration;

/// <summary>Real API + PostgreSQL with every module; fake Catalog, SMS, email and overdue-COD list; quiet hours switchable per test.</summary>
public sealed class NotificationsApiFactory : ApiFactory, IAsyncLifetime
{
    public static readonly string[] StaffRecipients = ["owner@example.com", "staff@example.com"];

    private static readonly TimeZoneInfo Kosovo = TimeZoneInfo.FindSystemTimeZoneById("Europe/Belgrade");

    /// <summary>Start = End: never quiet. Tests that need quiet hours set a window around "now".</summary>
    internal static readonly QuietHours NeverQuiet = new(TimeOnly.MinValue, TimeOnly.MinValue, Kosovo);

    private static int _phone = 700_000;

    internal FakeCatalogApi Catalog { get; } = new();

    internal FakeSmsSender Sms { get; } = new();

    internal FakeEmailSender Email { get; } = new();

    internal FakeShippingApi Shipping { get; } = new();

    internal QuietHours Quiet { get; set; } = NeverQuiet;

    internal RecordingJobRegistry Jobs { get; } = new();

    /// <summary>Makes an Orders event handler throw, so its outbox message eventually becomes a dead letter.</summary>
    internal ThrowingConfirmedHandler Throwing { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await StartAsync();
        var response = await CreateAdminClient().PutJson("/api/admin/notifications/settings/staff-alerts", new { recipients = StaffRecipients });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    internal AsyncServiceScope CreateScope() => Services.CreateAsyncScope();

    /// <summary>Quiet hours from one hour ago until two hours from now (Kosovo time).</summary>
    internal static QuietHours QuietNow()
    {
        var local = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Kosovo));
        var start = local.AddHours(-1);
        var end = local.AddHours(2);
        return new QuietHours(new TimeOnly(start.Hour, start.Minute), new TimeOnly(end.Hour, end.Minute), Kosovo);
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<ICatalogApi>();
        services.AddSingleton<ICatalogApi>(Catalog);
        services.RemoveAll<ISmsSender>();
        services.AddSingleton<ISmsSender>(Sms);
        services.RemoveAll<IEmailSender>();
        services.AddSingleton<IEmailSender>(Email);
        services.RemoveAll<IShippingApi>();
        services.AddSingleton<IShippingApi>(Shipping);
        services.RemoveAll<IRecurringJobRegistry>();
        services.AddSingleton<IRecurringJobRegistry>(Jobs);
        services.AddSingleton<IIntegrationEventHandler<PerfumeShop.Modules.Orders.Contracts.OrderConfirmedIntegrationEvent>>(Throwing);
        services.RemoveAll<QuietHours>();
        services.AddScoped(_ => Quiet);
    }

    /// <summary>
    /// A customer orders <paramref name="quantity"/> × a 39.90 € bottle (+2.00 € delivery = 41.90 € for one) from a variant
    /// with <paramref name="stock"/> units; staff confirms it when <paramref name="confirm"/> is true.
    /// </summary>
    internal async Task<TestOrder> OrderAsync(int quantity = 1, int stock = 5, bool confirm = false)
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
        var customer = CreateClientWithToken(await SignInAsync(phone));
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

        return new TestOrder(order.Id, order.Number, order.Total, variantId, phone);
    }

    /// <summary>OTP sign-in; the code is read from the fake SMS sender (OTP SMS bypass the notification queue).</summary>
    internal async Task<string> SignInAsync(string phone)
    {
        var anonymous = CreateClient();
        Assert.Equal(HttpStatusCode.Accepted, (await anonymous.PostJson("/api/auth/otp/request", new { phone })).StatusCode);
        var login = await (await anonymous.PostJson("/api/auth/otp/verify", new { phone, code = Sms.LastCodeFor(phone) })).Read<LoginBody>();
        return login.AccessToken;
    }

    /// <summary>Runs every module's outbox until nothing is left (Hangfire is off in tests).</summary>
    internal async Task DispatchOutboxAsync()
    {
        for (var round = 0; round < 10; round++)
        {
            var delivered = 0;
            await using var scope = CreateScope();
            foreach (var processor in scope.ServiceProvider.GetServices<IOutboxProcessor>())
            {
                delivered += await processor.ProcessAsync(TestContext.Current.CancellationToken);
            }

            if (delivered == 0)
            {
                return;
            }
        }

        Assert.Fail("Outbox still had messages after 10 rounds.");
    }

    /// <summary>One run of the "notifications-dispatch" job.</summary>
    internal async Task<int> SendAsync()
    {
        await using var scope = CreateScope();
        return await scope.ServiceProvider.GetRequiredService<NotificationDispatcher>().DispatchAsync(TestContext.Current.CancellationToken);
    }

    internal async Task PublishAsync<TEvent>(TEvent e)
        where TEvent : IIntegrationEvent
    {
        await using var scope = CreateScope();
        await scope.ServiceProvider.GetRequiredService<IEventBus>().PublishAsync(e, TestContext.Current.CancellationToken);
    }

    /// <summary>Messages (with attempts) from the log endpoint, oldest first.</summary>
    internal async Task<IReadOnlyList<MessageBody>> MessagesAsync(string? orderNumber = null, string? templateKey = null)
    {
        var query = $"pageSize=200&orderNumber={orderNumber}&templateKey={templateKey}";
        var response = await CreateAdminClient().Get($"/api/admin/notifications/messages?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return [.. (await response.Read<PageBody<MessageBody>>()).Items.OrderBy(m => m.CreatedAtUtc).ThenBy(m => m.Id)];
    }

    /// <summary>"The clock reached the retry time / 08:00": the message is due now.</summary>
    internal async Task MakeDueAsync(Guid messageId)
    {
        await using var scope = CreateScope();
        var past = DateTime.UtcNow.AddSeconds(-1);
        await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Messages
            .Where(m => m.Id == messageId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.NextAttemptAtUtc, past), TestContext.Current.CancellationToken);
    }

    private sealed record LoginBody(string AccessToken, Guid CustomerId);

    private sealed record OrderBody(Guid Id, string Number, decimal Total);
}

internal sealed record TestOrder(Guid Id, string Number, decimal Total, Guid VariantId, string Phone);

internal sealed record PageBody<T>(IReadOnlyList<T> Items, int TotalCount);

internal sealed record AttemptBody(DateTime AttemptedAtUtc, string Provider, bool Succeeded, string? ProviderMessageId, string? Response, string? Error);

internal sealed record MessageBody(
    Guid Id, string Channel, string Audience, string Recipient, string TemplateKey, string? Subject, string Body, string Status, string? OrderNumber,
    string SourceId, string? Encoding, int? Segments, bool HeldForQuietHours, DateTime SendAfterUtc, DateTime NextAttemptAtUtc, int Attempts,
    string? Provider, string? LastError, DateTime CreatedAtUtc, DateTime? SentAtUtc, string? ResentBy, IReadOnlyList<AttemptBody> AttemptLog);

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

    public VariantSummaryDto Get(Guid id) => _variants[id];

    public Task<VariantPriceDto?> GetVariantPriceAsync(Guid variantId, CancellationToken cancellationToken = default) => Task.FromResult<VariantPriceDto?>(null);

    public Task<VariantSummaryDto?> GetVariantSummaryAsync(Guid variantId, CancellationToken cancellationToken = default) => Task.FromResult(_variants.GetValueOrDefault(variantId));

    public Task<IReadOnlyDictionary<Guid, VariantSummaryDto>> GetVariantSummariesAsync(IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, VariantSummaryDto>>(variantIds.Where(_variants.ContainsKey).Distinct().ToDictionary(id => id, id => _variants[id]));
}

internal sealed partial class FakeSmsSender : ISmsSender
{
    private readonly ConcurrentQueue<(string Phone, string Message)> _sent = new();

    public volatile bool Fail;

    public string Name => "fake-sms";

    public IReadOnlyList<(string Phone, string Message)> Sent => [.. _sent];

    public Task<SmsSendResult> SendAsync(string phoneE164, string message, CancellationToken cancellationToken = default)
    {
        if (Fail)
        {
            return Task.FromResult(SmsSendResult.Failed("Gateway timeout", "{\"status\":504}"));
        }

        _sent.Enqueue((phoneE164, message));
        return Task.FromResult(SmsSendResult.Sent($"sms-{Guid.NewGuid():N}", "{\"status\":\"accepted\"}"));
    }

    public string LastCodeFor(string phone) => SixDigits().Match(_sent.Last(s => s.Phone == phone).Message).Value;

    [GeneratedRegex(@"\b\d{6}\b")]
    private static partial Regex SixDigits();
}

internal sealed class RecordingJobRegistry : IRecurringJobRegistry
{
    private readonly ConcurrentDictionary<string, (string Cron, TimeZoneInfo? TimeZone)> _jobs = new();

    public IReadOnlyDictionary<string, (string Cron, TimeZoneInfo? TimeZone)> Registered => _jobs;

    public void AddOrUpdate<TJob>(string id, string cron, TimeZoneInfo? timeZone = null)
        where TJob : class, IRecurringJob => _jobs[id] = (cron, timeZone);
}

internal sealed class ThrowingConfirmedHandler : IIntegrationEventHandler<PerfumeShop.Modules.Orders.Contracts.OrderConfirmedIntegrationEvent>
{
    public volatile bool Throw;

    public Task HandleAsync(PerfumeShop.Modules.Orders.Contracts.OrderConfirmedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default) =>
        Throw ? throw new InvalidOperationException("Test handler is down") : Task.CompletedTask;
}

internal sealed class FakeEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public string Name => "fake-email";

    public IReadOnlyList<EmailMessage> Sent => [.. _sent];

    public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        _sent.Enqueue(message);
        return Task.FromResult(EmailSendResult.Sent($"mail-{Guid.NewGuid():N}", "{\"accepted\":true}"));
    }
}

internal sealed class FakeShippingApi : IShippingApi
{
    public IReadOnlyList<OverdueCodDto> Overdue { get; set; } = [];

    public Task<ShipmentSummaryDto?> GetShipmentForOrderAsync(Guid orderId, CancellationToken cancellationToken = default) => Task.FromResult<ShipmentSummaryDto?>(null);

    public Task<ShipmentSummaryDto?> GetShipmentByTrackingNumberAsync(string trackingNumber, CancellationToken cancellationToken = default) =>
        Task.FromResult<ShipmentSummaryDto?>(null);

    public Task<IReadOnlyList<OverdueCodDto>> GetOverdueCodAsync(CancellationToken cancellationToken = default) => Task.FromResult(Overdue);
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
}
