using System.Net;
using PerfumeShop.Shared.Infrastructure.Auth;
using PerfumeShop.Testing;

namespace PerfumeShop.Modules.Catalog.Tests.Integration;

public sealed class AuthTests(CatalogApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Admin_endpoints_require_authentication()
    {
        var response = await factory.CreateClient().GetAsync($"{CatalogScenario.Admin}/brands", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Staff_can_read_the_catalog_but_only_admin_can_change_it()
    {
        var staff = factory.CreateClientWithToken(ApiFactory.CreateTestToken([Roles.Staff]));
        var customer = factory.CreateClientWithToken(ApiFactory.CreateTestToken([Roles.Customer], customerId: Guid.NewGuid()));
        var fragrance = await CatalogScenario.ImportKhamrahAsync(factory.CreateAdminClient());

        // Read: needed to pick the variant when receiving stock.
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync($"{CatalogScenario.Admin}/brands", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync($"{CatalogScenario.Admin}/fragrances", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync($"{CatalogScenario.Admin}/fragrances/{fragrance.Id}", Ct)).StatusCode);

        // Everything else is Admin-only.
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync($"{CatalogScenario.Admin}/fragella/usage", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostJsonAsync($"{CatalogScenario.Admin}/brands", new { name = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostJsonAsync($"{CatalogScenario.Admin}/fragrances/{fragrance.Id}/publish")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutJsonAsync($"{CatalogScenario.Admin}/brands/{fragrance.Brand.Id}", new { name = "X" })).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync($"{CatalogScenario.Admin}/brands", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync($"{CatalogScenario.Admin}/fragrances", Ct)).StatusCode);
    }

    [Fact]
    public async Task Seeded_admin_signs_in_and_reaches_admin_endpoints()
    {
        var response = await factory.CreateAdminClient().GetAsync($"{CatalogScenario.Admin}/brands", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_old_dev_role_header_grants_nothing()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-Roles", "Admin");

        var response = await client.GetAsync($"{CatalogScenario.Admin}/brands", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Tokens_signed_with_another_key_or_expired_are_rejected()
    {
        var forged = factory.CreateClientWithToken(ApiFactory.CreateTestToken([Roles.Admin], signingKey: "an-attackers-own-key-that-is-long-enough-0123456789"));
        var expired = factory.CreateClientWithToken(ApiFactory.CreateTestToken([Roles.Admin], expiresAtUtc: DateTime.UtcNow.AddMinutes(-5)));

        Assert.Equal(HttpStatusCode.Unauthorized, (await forged.GetAsync($"{CatalogScenario.Admin}/brands", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await expired.GetAsync($"{CatalogScenario.Admin}/brands", Ct)).StatusCode);
    }

    [Fact]
    public async Task Storefront_endpoints_are_public()
    {
        var response = await factory.CreateClient().GetAsync($"{CatalogScenario.Shop}/fragrances", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
