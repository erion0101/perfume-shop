using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PerfumeShop.Modules.Catalog.Infrastructure.Fragella;
using PerfumeShop.Modules.Catalog.Tests.Fragella;
using PerfumeShop.Testing;

[assembly: AssemblyFixture(typeof(PerfumeShop.Modules.Catalog.Tests.Integration.CatalogApiFactory))]

namespace PerfumeShop.Modules.Catalog.Tests.Integration;

/// <summary>
/// The real API on a throw-away PostgreSQL with real authentication (see <see cref="ApiFactory"/>).
/// Fragella is replaced by <see cref="FakeFragellaClient"/>: no real API calls in tests.
/// </summary>
public sealed class CatalogApiFactory : ApiFactory, IAsyncLifetime
{
    internal FakeFragellaClient Fragella { get; } = new();

    public ValueTask InitializeAsync() => new(StartAsync());

    internal AsyncServiceScope CreateScope() => Services.CreateAsyncScope();

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<IFragellaClient>();
        services.AddSingleton<IFragellaClient>(Fragella);
    }
}

internal static class HttpTestExtensions
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(ApiFactory.Json, Ct))!;

    public static Task<HttpResponseMessage> GetTestAsync(this HttpClient client, string url) => client.GetAsync(url, Ct);

    public static Task<HttpResponseMessage> DeleteTestAsync(this HttpClient client, string url) => client.DeleteAsync(url, Ct);

    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string url, object? body = null) =>
        client.PostAsJsonAsync(url, body ?? new { }, ApiFactory.Json, Ct);

    public static Task<HttpResponseMessage> PutJsonAsync(this HttpClient client, string url, object body) =>
        client.PutAsJsonAsync(url, body, ApiFactory.Json, Ct);

    /// <summary>Unique suffix so tests sharing one database never collide.</summary>
    public static string Unique() => Guid.NewGuid().ToString("N")[..8];
}
