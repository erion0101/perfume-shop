using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PerfumeShop.Modules.Customers.Features.Shared;
using PerfumeShop.Modules.Customers.Infrastructure.Identity;
using PerfumeShop.Modules.Notifications.Contracts;
using PerfumeShop.Testing;

[assembly: AssemblyFixture(typeof(PerfumeShop.Modules.Customers.Tests.Integration.CustomersApiFactory))]

namespace PerfumeShop.Modules.Customers.Tests.Integration;

/// <summary>Real API + PostgreSQL; SMS goes to <see cref="FakeSmsSender"/> so tests can read the codes.</summary>
public sealed class CustomersApiFactory : ApiFactory, IAsyncLifetime
{
    internal FakeSmsSender Sms { get; } = new();

    public ValueTask InitializeAsync() => new(StartAsync());

    internal AsyncServiceScope CreateScope() => Services.CreateAsyncScope();

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<ISmsSender>();
        services.AddSingleton<ISmsSender>(Sms);
    }

    /// <summary>Requests and verifies a code for <paramref name="phone"/> through the public endpoints.</summary>
    internal async Task<LoginResponse> LoginCustomerAsync(string phone)
    {
        var client = CreateClient();
        var requested = await client.PostJson("/api/auth/otp/request", new { phone });
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);

        var verified = await client.PostJson("/api/auth/otp/verify", new { phone, code = Sms.LastCodeFor(phone) });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        return await verified.Read<LoginResponse>();
    }

    internal async Task<HttpClient> CustomerClientAsync(string? phone = null) =>
        CreateClientWithToken((await LoginCustomerAsync(phone ?? TestHelpers.UniquePhone())).AccessToken);

    /// <summary>An Admin or Staff account with a password (the seeded admin must not get locked out by tests).</summary>
    internal async Task<(string Email, string Password)> CreateStaffAccountAsync(string role)
    {
        await using var scope = CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var email = $"staff-{Guid.NewGuid():N}@perfumeshop.test";
        const string password = "Staff-Password-123!";
        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true, CreatedAtUtc = DateTime.UtcNow };

        Assert.True((await users.CreateAsync(user, password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        return (email, password);
    }
}

/// <summary>Captures SMS instead of sending them. <see cref="FailSends"/> simulates a provider outage.</summary>
internal sealed partial class FakeSmsSender : ISmsSender
{
    private readonly ConcurrentQueue<(string Phone, string Message)> _sent = new();

    public bool FailSends { get; set; }

    public IReadOnlyList<(string Phone, string Message)> Sent => [.. _sent];

    public Task<SmsSendResult> SendAsync(string phoneE164, string message, CancellationToken cancellationToken = default)
    {
        if (FailSends)
        {
            return Task.FromResult(SmsSendResult.Failed("provider down"));
        }

        _sent.Enqueue((phoneE164, message));
        return Task.FromResult(SmsSendResult.Sent());
    }

    /// <summary>The 6-digit code in the latest SMS to this number (any input format).</summary>
    public string LastCodeFor(string phone)
    {
        var e164 = PerfumeShop.Modules.Customers.Domain.Common.PhoneNumber.Create(phone).Value.Value;
        var message = _sent.Where(s => s.Phone == e164).Select(s => s.Message).LastOrDefault()
            ?? throw new InvalidOperationException($"No SMS was sent to {e164}.");
        return SixDigits().Match(message).Value;
    }

    [GeneratedRegex(@"\b\d{6}\b")]
    private static partial Regex SixDigits();
}

internal static class Http
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static Task<HttpResponseMessage> PostJson(this HttpClient client, string url, object? body = null) =>
        client.PostAsJsonAsync(url, body ?? new { }, ApiFactory.Json, Ct);

    public static Task<HttpResponseMessage> PutJson(this HttpClient client, string url, object body) =>
        client.PutAsJsonAsync(url, body, ApiFactory.Json, Ct);

    public static Task<HttpResponseMessage> Get(this HttpClient client, string url) => client.GetAsync(url, Ct);

    public static Task<HttpResponseMessage> Delete(this HttpClient client, string url) => client.DeleteAsync(url, Ct);

    public static async Task<T> Read<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(ApiFactory.Json, Ct))!;

    public static async Task<string?> ErrorCode(this HttpResponseMessage response)
    {
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
