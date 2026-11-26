using System.Globalization;
using System.Net;

namespace PerfumeShop.Modules.Catalog.Infrastructure.Fragella;

internal sealed record FragellaHttpResponse(HttpStatusCode StatusCode, string Body)
{
    public bool IsSuccess => (int)StatusCode is >= 200 and < 300;
}

/// <summary>Raw HTTP access to Fragella. Budget and caching live in <see cref="FragellaSearchService"/>.</summary>
internal interface IFragellaClient
{
    Task<FragellaHttpResponse> SearchAsync(string query, int limit, CancellationToken cancellationToken);
}

/// <summary>Typed HttpClient; base address, timeout and the x-api-key header are set at registration.</summary>
internal sealed class FragellaClient(HttpClient http) : IFragellaClient
{
    public const string ApiKeyHeader = "x-api-key";

    public async Task<FragellaHttpResponse> SearchAsync(string query, int limit, CancellationToken cancellationToken)
    {
        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"fragrances?search={Uri.EscapeDataString(query)}&limit={limit}");

        using var response = await http.GetAsync(new Uri(path, UriKind.Relative), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new FragellaHttpResponse(response.StatusCode, body);
    }
}
