using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace PerfumeShop.Shared.Infrastructure.Auth;

/// <summary>Section "Auth:Staff".</summary>
public sealed class StaffSecurityOptions
{
    public const string SectionName = "Auth:Staff";

    /// <summary>
    /// Admin endpoints need a token issued after a second factor (authenticator app). Admins without 2FA can still
    /// sign in, but only reach staff-level pages and "My account" until they enable it.
    /// </summary>
    public bool RequireTwoFactorForAdmins { get; set; } = true;
}

/// <summary>Part of the Admin policy: the "mfa" claim when 2FA is required for admins.</summary>
public sealed class AdminTwoFactorRequirement : IAuthorizationRequirement;

internal sealed class AdminTwoFactorHandler(IOptionsMonitor<StaffSecurityOptions> options) : AuthorizationHandler<AdminTwoFactorRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminTwoFactorRequirement requirement)
    {
        if (!options.CurrentValue.RequireTwoFactorForAdmins || context.User.HasClaim(PlatformClaims.TwoFactor, "true"))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
