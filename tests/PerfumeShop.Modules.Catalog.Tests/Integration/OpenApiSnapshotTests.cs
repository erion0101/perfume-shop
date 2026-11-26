using System.Text.Json;
using System.Text.Json.Nodes;
using PerfumeShop.Testing;

namespace PerfumeShop.Modules.Catalog.Tests.Integration;

/// <summary>
/// web/packages/api-client is generated from web/packages/api-client/openapi.json. This test fails when the API
/// changed and the snapshot was not regenerated. Regenerate (no running API needed):
///   UPDATE_OPENAPI_SNAPSHOT=1 dotnet test --project tests/PerfumeShop.Modules.Catalog.Tests --filter-class "*OpenApiSnapshotTests"
///   pnpm --filter @perfumeshop/api-client generate
/// </summary>
public sealed class OpenApiSnapshotTests(CatalogApiFactory factory)
{
    private sealed class DevelopmentHost(string connectionString) : ApiFactory(connectionString)
    {
        protected override string EnvironmentName => "Development"; // the document is only mapped in Development
    }

    [Fact]
    public async Task Committed_openapi_snapshot_matches_the_api()
    {
        await using var host = new DevelopmentHost(factory.ConnectionString);
        var json = await host.CreateClient().GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var current = JsonNode.Parse(json)!.AsObject();
        current.Remove("servers"); // host-specific
        var formatted = current.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";

        var path = Path.Combine(RepositoryRoot(), "web", "packages", "api-client", "openapi.json");
        if (Environment.GetEnvironmentVariable("UPDATE_OPENAPI_SNAPSHOT") == "1")
        {
            await File.WriteAllTextAsync(path, formatted, TestContext.Current.CancellationToken);
        }

        Assert.True(File.Exists(path), $"Missing {path}. Run with UPDATE_OPENAPI_SNAPSHOT=1.");
        var committed = JsonNode.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.True(JsonNode.DeepEquals(current, committed),
            "The API's OpenAPI document changed. Regenerate the client: UPDATE_OPENAPI_SNAPSHOT=1 dotnet test --project tests/PerfumeShop.Modules.Catalog.Tests "
            + "--filter-class \"*OpenApiSnapshotTests\" && pnpm --filter @perfumeshop/api-client generate");
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PerfumeShop.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("PerfumeShop.sln not found above the test output folder.");
    }
}
