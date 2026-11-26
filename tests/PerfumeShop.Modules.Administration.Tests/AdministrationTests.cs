using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using PerfumeShop.Modules.Administration.Contracts;
using PerfumeShop.Shared.Infrastructure.Auth;
using PerfumeShop.Testing;

[assembly: AssemblyFixture(typeof(PerfumeShop.Modules.Administration.Tests.AdministrationApiFactory))]

namespace PerfumeShop.Modules.Administration.Tests;

public sealed class AdministrationApiFactory : ApiFactory, IAsyncLifetime
{
    public ValueTask InitializeAsync() => new(StartAsync());
}

public sealed class BusinessProfileTests(AdministrationApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string Url = "/api/admin/settings/business";

    private sealed record AuditRow(string Action, string EntityType, string? EntityId, string Summary, string? Changes, string ActorId, string? ActorEmail);

    private sealed record AuditPage(IReadOnlyList<AuditRow> Items, int TotalCount);

    [Fact]
    public async Task Seeded_profile_reports_its_placeholders_and_admin_replaces_them()
    {
        var admin = factory.CreateAdminClient();
        var seeded = (await admin.GetFromJsonAsync<BusinessProfileDto>(Url, ApiFactory.Json, Ct))!;
        Assert.Contains("taxNumber", seeded.PlaceholderFields); // appsettings ships "000000000"

        var response = await admin.PutAsJsonAsync(Url, new { shopName = "Oud & Amber", address = "Rr. Adem Jashari 12, Lipjan", phone = "+383 44 555 666", email = "Info@OudAmber.example", taxNumber = "811234567" }, ApiFactory.Json, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = (await response.Content.ReadFromJsonAsync<BusinessProfileDto>(ApiFactory.Json, Ct))!;
        Assert.Empty(saved.PlaceholderFields);
        Assert.Equal(("Oud & Amber", "info@oudamber.example"), (saved.ShopName, saved.Email));

        // Staff read it (the warning banner), only Admin changes it.
        var staff = factory.CreateClientWithToken(ApiFactory.CreateTestToken([Roles.Staff]));
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync(Url, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync(Url, new { shopName = "X", address = "Lipjan", phone = "+383 44 1", taxNumber = "811234567" }, ApiFactory.Json, Ct)).StatusCode);

        var audit = (await admin.GetFromJsonAsync<AuditPage>("/api/admin/audit?action=Settings.Business.Updated", ApiFactory.Json, Ct))!;
        var row = audit.Items[0];
        Assert.Contains("811234567", row.Changes, StringComparison.Ordinal);
        Assert.Equal(ApiFactory.AdminEmail, row.ActorEmail);

        // Back to placeholders for the other tests (and to check that validation still allows it to be shown as placeholder).
        await admin.PutAsJsonAsync(Url, new { shopName = "PerfumeShop", address = "Lipjan, Kosovë", phone = "+383 44 000 000", taxNumber = "000000000" }, ApiFactory.Json, Ct);
        var back = (await admin.GetFromJsonAsync<BusinessProfileDto>(Url, ApiFactory.Json, Ct))!;
        Assert.Equal(["phone", "taxNumber"], back.PlaceholderFields);
    }

    [Theory]
    [InlineData("Shop", "ab", "+383 44 555 666", "811234567", "Administration.Business.Invalid")]
    [InlineData("Shop", "Lipjan", "+383 44 555 666", "81-123", "Administration.Business.TaxNumberInvalid")]
    public async Task Invalid_details_are_rejected(string shopName, string address, string phone, string taxNumber, string code)
    {
        var response = await factory.CreateAdminClient().PutAsJsonAsync(Url, new { shopName, address, phone, taxNumber }, ApiFactory.Json, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task Audit_log_is_admin_only_and_filters()
    {
        var staff = factory.CreateClientWithToken(ApiFactory.CreateTestToken([Roles.Staff]));
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/admin/audit", Ct)).StatusCode);

        var admin = factory.CreateAdminClient();
        await admin.PutAsJsonAsync("/api/admin/orders/delivery-rates/MK", new { isEnabled = false, fee = 5.5m, freeDeliveryFrom = 80m }, ApiFactory.Json, Ct);

        var byPrefix = (await admin.GetFromJsonAsync<AuditPage>("/api/admin/audit?action=Settings.", ApiFactory.Json, Ct))!;
        var rate = Assert.Single(byPrefix.Items, i => i.Action == "Settings.DeliveryRate.Updated" && i.EntityId == "MK");
        Assert.Contains("5.5", rate.Changes, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/admin/audit?from=2026-12-02&to=2026-12-01", Ct)).StatusCode);
    }

    [Fact]
    public async Task Failed_requests_are_not_audited()
    {
        var admin = factory.CreateAdminClient();
        var before = (await admin.GetFromJsonAsync<AuditPage>("/api/admin/audit?action=Settings.DeliveryRate.Updated&entityId=ZZ", ApiFactory.Json, Ct))!.TotalCount;

        var response = await admin.PutAsJsonAsync("/api/admin/orders/delivery-rates/ZZ", new { isEnabled = true, fee = 1m, freeDeliveryFrom = 10m }, ApiFactory.Json, Ct);

        Assert.False(response.IsSuccessStatusCode);
        Assert.Equal(before, (await admin.GetFromJsonAsync<AuditPage>("/api/admin/audit?action=Settings.DeliveryRate.Updated&entityId=ZZ", ApiFactory.Json, Ct))!.TotalCount);
    }
}

public sealed class RedactionTests
{
    [Fact]
    public void Passwords_codes_and_tokens_never_reach_the_audit_log()
    {
        var node = JsonNode.Parse("""{"email":"a@b.c","temporaryPassword":"S3cret!!","nested":{"newPassword":"x","code":"123456","countryCode":"XK"},"list":[{"refreshToken":"t"}]}""");

        var redacted = PerfumeShop.Shared.Infrastructure.Audit.AuditedEndpoints.Redact(node)!.ToJsonString();

        Assert.DoesNotContain("S3cret", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("123456", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("\"t\"", redacted, StringComparison.Ordinal);
        Assert.Contains("\"countryCode\":\"XK\"", redacted, StringComparison.Ordinal);
        Assert.Contains("a@b.c", redacted, StringComparison.Ordinal);
    }
}
