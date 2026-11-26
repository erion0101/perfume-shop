using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using PerfumeShop.Modules.Customers.Features.Shared;
using PerfumeShop.Modules.Customers.Infrastructure.Identity;
using PerfumeShop.Shared.Infrastructure.Auth;
using PerfumeShop.Testing;

namespace PerfumeShop.Modules.Customers.Tests.Integration;

public sealed class StaffLoginTests(CustomersApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private Task<HttpResponseMessage> Login(string email, string password) =>
        _client.PostJson("/api/auth/staff/login", new { email, password });

    [Fact]
    public async Task Seeded_admin_gets_an_admin_token_with_a_short_refresh()
    {
        var response = await Login(ApiFactory.AdminEmail, ApiFactory.AdminPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var login = await response.Read<LoginResponse>();
        Assert.Equal("Admin", new JsonWebToken(login.AccessToken).GetClaim("role").Value);
        Assert.InRange(login.RefreshTokenExpiresAtUtc, DateTime.UtcNow.AddHours(11), DateTime.UtcNow.AddHours(13));
    }

    [Fact]
    public async Task Staff_can_sign_in()
    {
        var (email, password) = await factory.CreateStaffAccountAsync(Roles.Staff);

        Assert.Equal(HttpStatusCode.OK, (await Login(email.ToUpperInvariant(), password)).StatusCode);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_look_the_same()
    {
        var (email, _) = await factory.CreateStaffAccountAsync(Roles.Staff);

        var wrong = await Login(email, "Not-The-Password-1!");
        var unknown = await Login("nobody@perfumeshop.test", "Whatever-Password-1!");

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal("Customers.Auth.InvalidCredentials", await wrong.ErrorCode());
        Assert.Equal("Customers.Auth.InvalidCredentials", await unknown.ErrorCode());
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account()
    {
        var (email, password) = await factory.CreateStaffAccountAsync(Roles.Staff);
        for (var i = 0; i < 4; i++)
        {
            await Login(email, "Not-The-Password-1!");
        }

        var fifth = await Login(email, "Not-The-Password-1!");
        var correct = await Login(email, password);

        Assert.Equal(HttpStatusCode.TooManyRequests, fifth.StatusCode);
        Assert.Equal("Customers.Auth.LockedOut", await correct.ErrorCode());
    }

    [Fact]
    public async Task Accounts_without_a_back_office_role_cannot_use_password_login()
    {
        await using var scope = factory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var email = $"customer-{Guid.NewGuid():N}@perfumeshop.test";
        var user = new AppUser { UserName = email, Email = email, CreatedAtUtc = DateTime.UtcNow };
        await users.CreateAsync(user, "Customer-Password-1!");
        await users.AddToRoleAsync(user, Roles.Customer);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(email, "Customer-Password-1!")).StatusCode);
    }

    [Fact]
    public async Task Seeding_is_idempotent_and_creates_all_roles()
    {
        await using var scope = factory.CreateScope();

        await IdentitySeeder.SeedAsync(scope.ServiceProvider, TestContext.Current.CancellationToken);
        await IdentitySeeder.SeedAsync(scope.ServiceProvider, TestContext.Current.CancellationToken);

        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        foreach (var role in Roles.All)
        {
            Assert.True(await roles.RoleExistsAsync(role));
        }

        var admin = await users.FindByEmailAsync(ApiFactory.AdminEmail);
        Assert.True(await users.IsInRoleAsync(admin!, Roles.Admin));
    }
}
