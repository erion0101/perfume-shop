using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.IdentityModel.JsonWebTokens;
using PerfumeShop.Modules.Customers.Features.Shared;
using PerfumeShop.Modules.Customers.Features.StaffLogin;
using PerfumeShop.Modules.Customers.Infrastructure.Auth;
using PerfumeShop.Modules.Customers.Infrastructure.Identity;
using PerfumeShop.Shared.Infrastructure.Auth;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Customers.Features.StaffSession;

/// <summary>What the admin panel keeps in memory. The refresh token is never in the body: it lives in an HttpOnly cookie.</summary>
/// <param name="TwoFactorSetupRequired">An Admin without 2FA while 2FA is required: only staff pages work until it is enabled.</param>
internal sealed record StaffSessionResponse(
    string AccessToken, DateTime AccessTokenExpiresAtUtc, Guid UserId, string Email, string? DisplayName, IReadOnlyList<string> Roles,
    bool TwoFactorEnabled, bool TwoFactorSetupRequired);

/// <summary>
/// Browser sessions for the admin panel. Login and refresh set the rotating refresh token as an HttpOnly,
/// Secure, SameSite=Strict cookie limited to this path, so JavaScript (and XSS) can never read it.
/// Every call needs the <see cref="ClientHeader"/> header, which a cross-site form cannot send (CSRF).
/// </summary>
internal sealed class StaffSessionHandler(StaffLoginHandler login, TokenService tokens, UserManager<AppUser> users, Microsoft.Extensions.Options.IOptionsMonitor<StaffSecurityOptions> security) : IHandler
{
    public const string CookieName = "ps_staff_rt";
    public const string CookiePath = "/api/auth/staff/session";
    public const string ClientHeader = "X-PerfumeShop-Client";

    public static readonly Error MissingClientHeader = Error.Forbidden("Customers.Auth.ClientHeaderMissing", $"Send the {ClientHeader} header.");

    public async Task<Result<StaffSessionResponse>> LoginAsync(StaffLoginRequest request, HttpContext http, CancellationToken ct)
    {
        var signedIn = await login.HandleAsync(request, http.ClientIp(), ct);
        if (signedIn.IsFailure)
        {
            return signedIn.Error;
        }

        return await StartAsync(signedIn.Value.AccessToken, signedIn.Value.AccessTokenExpiresAtUtc, signedIn.Value.RefreshToken, signedIn.Value.RefreshTokenExpiresAtUtc, http, ct);
    }

    /// <summary>Rotates the cookie's refresh token. Re-using an old one revokes the session (theft detection in <see cref="TokenService"/>).</summary>
    public async Task<Result<StaffSessionResponse>> RefreshAsync(HttpContext http, CancellationToken ct)
    {
        if (http.Request.Cookies[CookieName] is not { Length: > 0 and <= 200 } raw)
        {
            return TokenErrors.InvalidRefreshToken;
        }

        var refreshed = await tokens.RefreshAsync(raw, http.ClientIp(), ct);
        if (refreshed.IsFailure)
        {
            ClearCookie(http);
            return refreshed.Error;
        }

        var t = refreshed.Value;
        return await StartAsync(t.AccessToken, t.AccessTokenExpiresAtUtc, t.RefreshToken, t.RefreshTokenExpiresAtUtc, http, ct);
    }

    public async Task LogoutAsync(HttpContext http, CancellationToken ct)
    {
        if (http.Request.Cookies[CookieName] is { Length: > 0 and <= 200 } raw)
        {
            await tokens.RevokeAsync(raw, ct);
        }

        ClearCookie(http);
    }

    private async Task<Result<StaffSessionResponse>> StartAsync(
        string accessToken, DateTime accessExpires, string refreshToken, DateTime refreshExpires, HttpContext http, CancellationToken ct)
    {
        var subject = new JsonWebTokenHandler().ReadJsonWebToken(accessToken).Subject;
        var user = Guid.TryParse(subject, out var userId) ? await users.FindByIdAsync(userId.ToString()) : null;
        var roles = user is null ? [] : await users.GetRolesAsync(user);
        if (user is null || (!roles.Contains(Roles.Admin) && !roles.Contains(Roles.Staff)))
        {
            await tokens.RevokeAsync(refreshToken, ct); // a customer token cannot become a staff session
            ClearCookie(http);
            return TokenErrors.InvalidRefreshToken;
        }

        http.Response.Cookies.Append(CookieName, refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true, // browsers accept Secure cookies on http://localhost too
            SameSite = SameSiteMode.Strict,
            Path = CookiePath,
            Expires = refreshExpires,
            IsEssential = true,
        });
        var setupRequired = roles.Contains(Roles.Admin) && security.CurrentValue.RequireTwoFactorForAdmins && !user.TwoFactorEnabled;
        return new StaffSessionResponse(accessToken, accessExpires, user.Id, user.Email ?? string.Empty, user.DisplayName, [.. roles.Order()], user.TwoFactorEnabled, setupRequired);
    }

    private static void ClearCookie(HttpContext http) =>
        http.Response.Cookies.Delete(CookieName, new CookieOptions { Path = CookiePath, Secure = true, HttpOnly = true, SameSite = SameSiteMode.Strict });

    public static bool HasClientHeader(HttpContext http) => http.Request.Headers.ContainsKey(ClientHeader);
}

internal static class StaffSessionEndpoint
{
    public static void Map(RouteGroupBuilder auth)
    {
        var session = auth.MapGroup("/staff/session").WithTags("Auth (admin panel session)");

        session.MapPost("", async (StaffLoginRequest request, HttpContext http, StaffSessionHandler h, CancellationToken ct) =>
                StaffSessionHandler.HasClientHeader(http) ? (await h.LoginAsync(request, http, ct)).ToOk() : StaffSessionHandler.MissingClientHeader.ToProblem())
            .WithValidation<StaffLoginRequest>()
            .WithName("StartStaffSession")
            .WithDescription("Admin panel sign-in. Sets the refresh token as an HttpOnly cookie; needs the X-PerfumeShop-Client header.");

        session.MapPost("/refresh", async (HttpContext http, StaffSessionHandler h, CancellationToken ct) =>
                StaffSessionHandler.HasClientHeader(http) ? (await h.RefreshAsync(http, ct)).ToOk() : StaffSessionHandler.MissingClientHeader.ToProblem())
            .WithName("RefreshStaffSession")
            .WithDescription("New access token from the refresh cookie (rotated).");

        session.MapDelete("", async Task<Results<NoContent, ProblemHttpResult>> (HttpContext http, StaffSessionHandler h, CancellationToken ct) =>
            {
                if (!StaffSessionHandler.HasClientHeader(http))
                {
                    return StaffSessionHandler.MissingClientHeader.ToProblem();
                }

                await h.LogoutAsync(http, ct);
                return TypedResults.NoContent();
            })
            .WithName("EndStaffSession")
            .WithDescription("Sign out: revokes the session and deletes the cookie.");
    }
}
