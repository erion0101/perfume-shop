using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PerfumeShop.Modules.Catalog.Infrastructure.Fragella;

namespace PerfumeShop.Modules.Catalog.Tests.Fragella;

/// <summary>Checks the real typed HttpClient registration: base URL, query string, x-api-key.</summary>
public sealed class FragellaClientTests
{
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
        }
    }

    [Fact]
    public async Task Sends_search_to_the_configured_base_url_with_the_api_key()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = "Host=unused",
                ["Fragella:ApiKey"] = "secret-from-user-secrets",
            })
            .Build();
        var handler = new RecordingHandler();
        var services = new ServiceCollection().AddLogging().AddSingleton<IConfiguration>(configuration);
        new CatalogModule().Register(services, configuration, new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = "Testing" });
        services.AddHttpClient(nameof(IFragellaClient)).ConfigurePrimaryHttpMessageHandler(() => handler);

        await using var provider = services.BuildServiceProvider();
        var response = await provider.GetRequiredService<IFragellaClient>()
            .SearchAsync("khamrah qahwa", 10, TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccess);
        Assert.Equal("https://api.fragella.com/api/v1/fragrances?search=khamrah%20qahwa&limit=10", handler.Request!.RequestUri!.AbsoluteUri);
        Assert.Equal("secret-from-user-secrets", handler.Request.Headers.GetValues(FragellaClient.ApiKeyHeader).Single());
    }
}
