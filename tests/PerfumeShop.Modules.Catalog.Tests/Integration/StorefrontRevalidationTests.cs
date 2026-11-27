using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using PerfumeShop.Modules.Catalog.Infrastructure.Storefront;
using PerfumeShop.Shared.Infrastructure.Jobs;
using PerfumeShop.Testing;
using static PerfumeShop.Modules.Catalog.Tests.Integration.CatalogScenario;

namespace PerfumeShop.Modules.Catalog.Tests.Integration;

/// <summary>Catalog changes queue a storefront revalidation; the job posts the tags with the secret header.</summary>
public sealed class StorefrontRevalidationTests(CatalogApiFactory shared)
{
    private const string Secret = "storefront-revalidate-secret-at-least-32-chars";

    /// <summary>Own host (own job recorder and fake storefront), same database as the other Catalog tests.</summary>
    private sealed class Host(string connectionString, FakeStorefront storefront, string? url = "http://storefront.test/api/revalidate") : ApiFactory(connectionString)
    {
        protected override void ConfigureSettings(IDictionary<string, string?> settings)
        {
            settings["Storefront:RevalidateUrl"] = url;
            settings["Storefront:RevalidateSecret"] = Secret;
        }

        protected override void ConfigureServices(IServiceCollection services) =>
            services.Configure<HttpClientFactoryOptions>(StorefrontRevalidationJob.HttpClientName, o =>
                o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = storefront));
    }

    private sealed class FakeStorefront : HttpMessageHandler
    {
        public List<(Uri Url, string? Secret, string Body)> Requests { get; } = [];

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!, request.Headers.TryGetValues(StorefrontOptions.SecretHeader, out var v) ? v.Single() : null, await request.Content!.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(Status);
        }
    }

    [Fact]
    public async Task Catalog_changes_queue_a_revalidation_and_reads_do_not()
    {
        var storefront = new FakeStorefront();
        await using var host = new Host(shared.ConnectionString, storefront);
        await host.StartAsync();
        var admin = host.CreateAdminClient();
        var jobs = host.Services.GetRequiredService<RecordingBackgroundJobQueue>();

        await admin.GetTestAsync($"{Admin}/brands");
        Assert.Empty(jobs.For<StorefrontRevalidationJob>());

        var created = await admin.PostJsonAsync($"{Admin}/brands", new { name = $"Brand {HttpTestExtensions.Unique()}" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var failed = await admin.PostJsonAsync($"{Admin}/brands", new { name = "" });
        Assert.Equal(HttpStatusCode.BadRequest, failed.StatusCode);
        var queued = Assert.Single(jobs.For<StorefrontRevalidationJob>());

        // Run the queued call the way the job server would.
        await using var scope = host.Services.CreateAsyncScope();
        await queued.Compile()(scope.ServiceProvider.GetRequiredService<StorefrontRevalidationJob>());

        var sent = Assert.Single(storefront.Requests);
        Assert.Equal(("http://storefront.test/api/revalidate", Secret), (sent.Url.ToString(), sent.Secret));
        Assert.Equal(["catalog"], JsonDocument.Parse(sent.Body).RootElement.GetProperty("tags").EnumerateArray().Select(t => t.GetString()));
    }

    [Fact]
    public async Task A_failing_storefront_makes_the_job_throw_so_it_is_retried()
    {
        var storefront = new FakeStorefront { Status = HttpStatusCode.ServiceUnavailable };
        await using var host = new Host(shared.ConnectionString, storefront);
        await using var scope = host.Services.CreateAsyncScope();

        await Assert.ThrowsAsync<HttpRequestException>(() => scope.ServiceProvider.GetRequiredService<StorefrontRevalidationJob>().SendAsync(["catalog"]));
    }

    [Fact]
    public async Task Without_a_url_nothing_is_queued()
    {
        await using var host = new Host(shared.ConnectionString, new FakeStorefront(), url: null);
        await host.StartAsync();

        await host.CreateAdminClient().PostJsonAsync($"{Admin}/brands", new { name = $"Brand {HttpTestExtensions.Unique()}" });

        Assert.Empty(host.Services.GetRequiredService<RecordingBackgroundJobQueue>().For<StorefrontRevalidationJob>());
    }
}
