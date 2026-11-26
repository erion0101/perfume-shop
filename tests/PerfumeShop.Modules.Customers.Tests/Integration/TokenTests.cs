using System.Net;
using PerfumeShop.Modules.Customers.Features.Shared;

namespace PerfumeShop.Modules.Customers.Tests.Integration;

public sealed class TokenTests(CustomersApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private Task<HttpResponseMessage> Refresh(string token) => _client.PostJson("/api/auth/refresh", new { refreshToken = token });

    [Fact]
    public async Task Refresh_rotates_both_tokens()
    {
        var login = await factory.LoginCustomerAsync(TestHelpers.UniquePhone());

        var response = await Refresh(login.RefreshToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rotated = await response.Read<LoginResponse>();
        Assert.NotEqual(login.RefreshToken, rotated.RefreshToken);
        Assert.NotEqual(login.AccessToken, rotated.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClientWithToken(rotated.AccessToken).Get("/api/customers/me")).StatusCode);
    }

    [Fact]
    public async Task Reusing_a_rotated_token_revokes_the_whole_session()
    {
        var login = await factory.LoginCustomerAsync(TestHelpers.UniquePhone());
        var rotated = await (await Refresh(login.RefreshToken)).Read<LoginResponse>();

        var replay = await Refresh(login.RefreshToken);         // attacker replays the old token
        var victim = await Refresh(rotated.RefreshToken);       // the newer token is now dead too

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal("Customers.Auth.InvalidRefreshToken", await replay.ErrorCode());
        Assert.Equal(HttpStatusCode.Unauthorized, victim.StatusCode);
    }

    [Fact]
    public async Task Other_sessions_survive_a_replay_in_one_session()
    {
        var phone = TestHelpers.UniquePhone();
        var phoneA = await factory.LoginCustomerAsync(phone);
        var phoneB = await factory.LoginCustomerAsync(phone);
        await Refresh(phoneA.RefreshToken);
        await Refresh(phoneA.RefreshToken); // replay in session A

        Assert.Equal(HttpStatusCode.OK, (await Refresh(phoneB.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task Concurrent_refreshes_with_one_token_let_only_one_win()
    {
        var login = await factory.LoginCustomerAsync(TestHelpers.UniquePhone());

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => factory.CreateClient().PostJson("/api/auth/refresh", new { refreshToken = login.RefreshToken })));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var login = await factory.LoginCustomerAsync(TestHelpers.UniquePhone());

        var logout = await _client.PostJson("/api/auth/logout", new { refreshToken = login.RefreshToken });

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(login.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task Unknown_refresh_token_is_rejected()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh("not-a-real-token")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostJson("/api/auth/logout", new { refreshToken = "not-a-real-token" })).StatusCode);
    }
}
