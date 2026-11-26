using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using PerfumeShop.Modules.Catalog.Infrastructure.Fragella;

namespace PerfumeShop.Modules.Catalog.Tests.Fragella;

/// <summary>
/// Stands in for Fragella. By default answers every search with the saved sample, with ids and brand
/// names made unique per query (so tests importing "the same" fragrance do not collide).
/// </summary>
internal sealed class FakeFragellaClient : IFragellaClient
{
    public static string SamplePath => Path.Combine(AppContext.BaseDirectory, "Fragella", "Samples", "fragella-search-khamrah.json");

    public List<string> Queries { get; } = [];

    public Func<string, FragellaHttpResponse>? Respond { get; set; }

    public static string Sample() => File.ReadAllText(SamplePath);

    /// <summary>Sample with "_id" → "{id}-{token}" and "Brand" → "{brand} {token}".</summary>
    public static string SampleFor(string token)
    {
        var items = JsonNode.Parse(Sample())!.AsArray();
        foreach (var item in items)
        {
            if (item?["_id"] is { } id)
            {
                item["_id"] = $"{id.GetValue<string>()}-{token}";
                item["Brand"] = $"{item["Brand"]!.GetValue<string>()} {token}";
            }
        }

        return items.ToJsonString();
    }

    public Task<FragellaHttpResponse> SearchAsync(string query, int limit, CancellationToken cancellationToken)
    {
        lock (Queries)
        {
            Queries.Add(query);
        }

        var response = Respond?.Invoke(query)
            ?? new FragellaHttpResponse(HttpStatusCode.OK, SampleFor(TokenOf(query)));
        return Task.FromResult(response);
    }

    public static string TokenOf(string query) => query.Split(' ').Last();

    public int CallsFor(string query)
    {
        lock (Queries)
        {
            return Queries.Count(q => q == query);
        }
    }

    public static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
