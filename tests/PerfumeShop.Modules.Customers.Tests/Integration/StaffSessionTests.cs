using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using PerfumeShop.Modules.Customers.Features.StaffSession;
using PerfumeShop.Testing;

namespace PerfumeShop.Modules.Customers.Tests.Integration;

/// <summary>Admin panel sessions: the refresh token only ever travels in an HttpOnly cookie.</summary>
public sealed class StaffSessionTests(CustomersApiFactory factory)
{
    private const string Session = "/api/auth/staff/session";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A browser-like client: keeps cookies, HTTPS (the cookie is Secure), sends the client header.</summary>
    private HttpClient Browser(bool header = true)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        if (header)
        {
            client.DefaultRequestHeaders.Add(StaffSessionHandler.ClientHeader, "admin");
        }

        return client;
    }

    private static Task<HttpResponseMessage> Login(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync(Session, new { email, password }, ApiFactory.Json, Ct);

    [Fact]
    public async Task Login_sets_an_http_only_cookie_and_returns_no_refresh_token()
    {
        var response = await Login(Browser(), ApiFactory.AdminEmail, ApiFactory.AdminPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("refreshToken", body, StringComparison.OrdinalIgnoreCase);
        var session = (await response.Content.ReadFromJsonAsync<StaffSessionResponse>(ApiFactory.Json, Ct))!;
        Assert.Equal(ApiFactory.AdminEmail, session.Email);
        Assert.Equal(["Admin"], session.Roles);

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith($"{StaffSessionHandler.CookieName}=", cookie, StringComparison.Ordinal);
        foreach (var flag in new[] { "httponly", "secure", "samesite=strict", $"path={StaffSessionHandler.CookiePath}" })
        {
            Assert.Contains(flag, cookie, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Refresh_rotates_the_cookie_and_the_old_one_cannot_be_reused()
    {
        var browser = Browser();
        await Login(browser, ApiFactory.AdminEmail, ApiFactory.AdminPassword);
        var stolen = (await Login(Browser(), ApiFactory.AdminEmail, ApiFactory.AdminPassword)).Headers.GetValues("Set-Cookie").Single().Split(';')[0];

        var first = await browser.PostAsync($"{Session}/refresh", null, Ct);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var token = (await first.Content.ReadFromJsonAsync<StaffSessionResponse>(ApiFactory.Json, Ct))!.AccessToken;
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClientWithToken(token).GetAsync("/api/admin/customers", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.PostAsync($"{Session}/refresh", null, Ct)).StatusCode); // the new cookie works

        // Replaying a cookie after it was rotated revokes that session.
        var attacker = Browser();
        var replay = new HttpRequestMessage(HttpMethod.Post, $"{Session}/refresh");
        replay.Headers.Add("Cookie", stolen);
        Assert.Equal(HttpStatusCode.OK, (await attacker.SendAsync(replay, Ct)).StatusCode);
        var again = new HttpRequestMessage(HttpMethod.Post, $"{Session}/refresh");
        again.Headers.Add("Cookie", stolen);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Browser(header: true).SendAsync(again, Ct)).StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_the_session_and_deletes_the_cookie()
    {
        var browser = Browser();
        var cookie = (await Login(browser, ApiFactory.AdminEmail, ApiFactory.AdminPassword)).Headers.GetValues("Set-Cookie").Single().Split(';')[0];

        var logout = await browser.DeleteAsync(Session, Ct);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Contains("expires=Thu, 01 Jan 1970", logout.Headers.GetValues("Set-Cookie").Single(), StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.PostAsync($"{Session}/refresh", null, Ct)).StatusCode);
        var replay = new HttpRequestMessage(HttpMethod.Post, $"{Session}/refresh");
        replay.Headers.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Browser().SendAsync(replay, Ct)).StatusCode);
    }

    [Fact]
    public async Task Requests_without_the_client_header_are_refused()
    {
        var browser = Browser(header: false);

        Assert.Equal(HttpStatusCode.Forbidden, (await Login(browser, ApiFactory.AdminEmail, ApiFactory.AdminPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.PostAsync($"{Session}/refresh", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.DeleteAsync(Session, Ct)).StatusCode);
    }

    [Fact]
    public async Task Wrong_password_and_customers_get_no_session()
    {
        var (email, _) = await factory.CreateStaffAccountAsync("Staff");
        var wrong = await Login(Browser(), email, "wrong-password-123");
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.False(wrong.Headers.Contains("Set-Cookie"));

        // A customer's refresh token placed in the cookie does not become a staff session.
        var customer = await factory.LoginCustomerAsync(TestHelpers.UniquePhone());
        var request = new HttpRequestMessage(HttpMethod.Post, $"{Session}/refresh");
        request.Headers.Add("Cookie", $"{StaffSessionHandler.CookieName}={customer.RefreshToken}");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Browser().SendAsync(request, Ct)).StatusCode);
    }

    [Fact]
    public async Task Staff_role_sessions_carry_their_role()
    {
        var (email, password) = await factory.CreateStaffAccountAsync("Staff");

        var session = (await (await Login(Browser(), email, password)).Content.ReadFromJsonAsync<StaffSessionResponse>(ApiFactory.Json, Ct))!;

        Assert.Equal(["Staff"], session.Roles);
    }
}
