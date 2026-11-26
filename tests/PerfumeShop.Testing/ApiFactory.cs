using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotNet.Testcontainers.Configurations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;

namespace PerfumeShop.Testing;

/// <summary>
/// The real API host on a throw-away PostgreSQL (same image as docker-compose), with migrations
/// applied on startup, a seeded admin account and real JWT authentication.
/// </summary>
public abstract class ApiFactory : WebApplicationFactory<Program>
{
    public const string JwtSigningKey = "integration-tests-jwt-signing-key-0123456789abcdefghij";
    public const string OtpHashKey = "integration-tests-otp-hash-key-0123456789abcdefghijklmn";
    public const string AdminEmail = "admin@perfumeshop.test";
    public const string AdminPassword = "Integration-Tests-Admin-1!";
    public const string Issuer = "perfumeshop";
    public const string Audience = "perfumeshop-api";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly PostgreSqlContainer? _ownedPostgres;
    private readonly string? _sharedConnectionString;

    /// <param name="connectionString">Reuse another factory's database instead of starting a container.</param>
    protected ApiFactory(string? connectionString = null)
    {
        _sharedConnectionString = connectionString;
        _ownedPostgres = connectionString is null ? CreatePostgres() : null;
    }

    public string ConnectionString => _sharedConnectionString ?? _ownedPostgres!.GetConnectionString();

    /// <summary>Access token of the seeded admin, obtained through the real staff login endpoint.</summary>
    public string AdminToken { get; private set; } = string.Empty;

    protected virtual string EnvironmentName => "Testing";

    public async Task StartAsync()
    {
        if (_ownedPostgres is not null)
        {
            await _ownedPostgres.StartAsync();
        }

        _ = Server; // builds and starts the host: migrations + seeding run here

        var login = await CreateClient().PostAsJsonAsync("/api/auth/staff/login", new { email = AdminEmail, password = AdminPassword }, Json);
        login.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        AdminToken = body.RootElement.GetProperty("accessToken").GetString()!;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_ownedPostgres is not null)
        {
            await _ownedPostgres.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }

    public HttpClient CreateAdminClient() => CreateClientWithToken(AdminToken);

    public HttpClient CreateClientWithToken(string accessToken)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    /// <summary>A token for roles that have no seeded account (e.g. Staff), signed like real ones.</summary>
    public static string CreateTestToken(
        IEnumerable<string> roles,
        Guid? userId = null,
        Guid? customerId = null,
        DateTime? expiresAtUtc = null,
        string signingKey = JwtSigningKey)
    {
        var now = DateTime.UtcNow;
        var expires = expiresAtUtc ?? now.AddMinutes(15);
        var claims = new Dictionary<string, object>
        {
            ["sub"] = (userId ?? Guid.NewGuid()).ToString(),
            ["role"] = roles.ToArray(),
        };
        if (customerId is { } id)
        {
            claims["customer_id"] = id.ToString();
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Claims = claims,
            IssuedAt = expires < now ? expires.AddMinutes(-15) : now,
            NotBefore = expires < now ? expires.AddMinutes(-15) : now,
            Expires = expires,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256),
        });
    }

    /// <summary>Change settings for a derived factory (e.g. remove a secret).</summary>
    protected virtual void ConfigureSettings(IDictionary<string, string?> settings)
    {
    }

    /// <summary>Replace services, e.g. external APIs with fakes.</summary>
    protected virtual void ConfigureServices(IServiceCollection services)
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = ConnectionString,
            ["Database:MigrateOnStartup"] = "true",
            ["Auth:Jwt:SigningKey"] = JwtSigningKey,
            ["Customers:Otp:HashKey"] = OtpHashKey,
            ["Auth:SeedAdmin:Email"] = AdminEmail,
            ["Auth:SeedAdmin:Password"] = AdminPassword,
            ["Fragella:ApiKey"] = "test-key",
            ["BackgroundJobs:Enabled"] = "false", // tests run jobs directly, deterministically
            ["Logging:LogLevel:Default"] = "Warning",
            ["Logging:LogLevel:Microsoft.EntityFrameworkCore"] = "Warning",
        };
        ConfigureSettings(settings);
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(ConfigureServices);
    }

    /// <summary>
    /// PostgreSQL is published on 127.0.0.1 only (random port): never reachable from the network, and
    /// it avoids Docker Desktop setups where binding 0.0.0.0 fails with "address already in use".
    /// Ryuk (Testcontainers' cleanup sidecar) always binds 0.0.0.0, so it only runs in CI; locally the
    /// factory removes its container on dispose.
    /// </summary>
    private static PostgreSqlContainer CreatePostgres()
    {
        if (Environment.GetEnvironmentVariable("CI") is null)
        {
            TestcontainersSettings.ResourceReaperEnabled = false;
        }

        var port = $"{PostgreSqlBuilder.PostgreSqlPort}/tcp";
        return new PostgreSqlBuilder("pgvector/pgvector:pg18")
            .WithCreateParameterModifier(create =>
                create.HostConfig!.PortBindings![port] = [new Docker.DotNet.Models.PortBinding { HostIP = "127.0.0.1", HostPort = string.Empty }])
            .Build();
    }
}
