using System.Collections.Concurrent;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PerfumeShop.Modules.Catalog.Contracts;
using PerfumeShop.Testing;

[assembly: AssemblyFixture(typeof(PerfumeShop.Modules.Inventory.Tests.Integration.InventoryApiFactory))]

namespace PerfumeShop.Modules.Inventory.Tests.Integration;

/// <summary>Real API + PostgreSQL. Catalog is replaced by <see cref="FakeCatalogApi"/> so variants can be made up.</summary>
public sealed class InventoryApiFactory : ApiFactory, IAsyncLifetime
{
    internal FakeCatalogApi Catalog { get; } = new();

    public ValueTask InitializeAsync() => new(StartAsync());

    internal AsyncServiceScope CreateScope() => Services.CreateAsyncScope();

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<ICatalogApi>();
        services.AddSingleton<ICatalogApi>(Catalog);
    }

    /// <summary>A known variant with <paramref name="quantities"/> received as separate batches (oldest first).</summary>
    internal async Task<Guid> VariantWithStockAsync(params int[] quantities)
    {
        var variantId = Catalog.Add();
        var day = new DateOnly(2026, 11, 1);
        foreach (var quantity in quantities)
        {
            var response = await CreateAdminClient().PostJson("/api/admin/inventory/receipts", new
            {
                supplierName = "Lattafa Distribution FZE",
                supplierInvoiceNumber = $"INV-{Guid.NewGuid():N}"[..20],
                supplierInvoiceDate = day,
                receivedOn = day,
                lines = new[] { new { variantId, batchCode = $"B-{day:MMdd}-{Guid.NewGuid():N}"[..14], quantity, unitCost = 18.50m } },
            });
            response.EnsureSuccessStatusCode();
            day = day.AddDays(1);
        }

        return variantId;
    }
}

internal sealed class FakeCatalogApi : ICatalogApi
{
    private readonly ConcurrentDictionary<Guid, VariantSummaryDto> _variants = new();

    public Guid Add(string? sku = null)
    {
        var id = Guid.NewGuid();
        _variants[id] = new VariantSummaryDto(
            id, Guid.NewGuid(), sku ?? $"LAT-{id.ToString("N")[..6].ToUpperInvariant()}", null, "Lattafa", "Khamrah", "lattafa-khamrah-edp",
            "EauDeParfum", "FullBottle", 100, "Ml", 39.90m, "EUR", true, true, null);
        return id;
    }

    public Task<VariantPriceDto?> GetVariantPriceAsync(Guid variantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<VariantPriceDto?>(null);

    public Task<VariantSummaryDto?> GetVariantSummaryAsync(Guid variantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_variants.GetValueOrDefault(variantId));

    public Task<IReadOnlyDictionary<Guid, VariantSummaryDto>> GetVariantSummariesAsync(IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, VariantSummaryDto>>(
            variantIds.Where(_variants.ContainsKey).Distinct().ToDictionary(id => id, id => _variants[id]));
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
