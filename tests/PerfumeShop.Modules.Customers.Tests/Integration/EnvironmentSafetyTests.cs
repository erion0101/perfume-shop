using System.Net;
using Microsoft.Extensions.DependencyInjection;
using PerfumeShop.Modules.Notifications.Contracts;
using PerfumeShop.Testing;

namespace PerfumeShop.Modules.Customers.Tests.Integration;

/// <summary>
/// Proves that development-only behaviour cannot run outside Development:
/// the console SMS sender (prints OTP codes), generated secrets, Swagger/OpenAPI and the old dev auth header.
/// </summary>
public sealed class EnvironmentSafetyTests(CustomersApiFactory shared)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A host in any environment, on the already-migrated test database, without test fakes.</summary>
    private sealed class HostIn(string connectionString, string environment, Action<IDictionary<string, string?>>? settings = null)
        : ApiFactory(connectionString)
    {
        protected override string EnvironmentName => environment;

        protected override void ConfigureSettings(IDictionary<string, string?> values) => settings?.Invoke(values);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public async Task Console_sms_sender_exists_only_in_development(string environment)
    {
        await using var host = new HostIn(shared.ConnectionString, environment);

        var sender = host.Services.GetRequiredService<ISmsSender>();

        Assert.Equal("UnconfiguredSmsSender", sender.GetType().Name);
    }

    [Fact]
    public async Task Development_uses_the_console_sms_sender()
    {
        await using var host = new HostIn(shared.ConnectionString, "Development");

        Assert.Equal("ConsoleSmsSender", host.Services.GetRequiredService<ISmsSender>().GetType().Name);
    }

    [Fact]
    public async Task Production_cannot_send_otp_codes_without_a_real_sms_provider()
    {
        await using var host = new HostIn(shared.ConnectionString, "Production");

        var response = await host.CreateClient().PostJson("/api/auth/otp/request", new { phone = TestHelpers.UniquePhone() });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Production_has_no_swagger_or_openapi_and_ignores_the_dev_header()
    {
        await using var host = new HostIn(shared.ConnectionString, "Production");
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-Roles", "Admin");

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/swagger/index.html", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/openapi/v1.json", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/catalog/brands", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/customers", Ct)).StatusCode);
    }

    [Theory]
    [InlineData("Auth:Jwt:SigningKey")]
    [InlineData("Customers:Otp:HashKey")]
    public async Task Production_refuses_to_start_without_a_secret(string secret)
    {
        await using var host = new HostIn(shared.ConnectionString, "Production", s => s[secret] = "");

        var error = Assert.ThrowsAny<Exception>(() => host.CreateClient());

        Assert.Contains(secret, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Production_refuses_a_too_short_signing_key()
    {
        await using var host = new HostIn(shared.ConnectionString, "Production", s => s["Auth:Jwt:SigningKey"] = "short");

        Assert.ThrowsAny<Exception>(() => host.CreateClient());
    }
}
