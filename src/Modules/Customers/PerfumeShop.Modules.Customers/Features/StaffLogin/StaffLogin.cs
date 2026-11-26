using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using PerfumeShop.Modules.Customers.Features.Shared;
using PerfumeShop.Modules.Customers.Infrastructure.Auth;
using PerfumeShop.Modules.Customers.Infrastructure.Identity;
using PerfumeShop.Shared.Infrastructure.Auth;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Customers.Features.StaffLogin;

/// <param name="TwoFactorCode">6-digit authenticator code (when 2FA is on).</param>
/// <param name="RecoveryCode">One-time recovery code instead of the authenticator code.</param>
internal sealed record StaffLoginRequest(string Email, string Password, string? TwoFactorCode = null, string? RecoveryCode = null);

internal sealed class StaffLoginValidator : AbstractValidator<StaffLoginRequest>
{
    public StaffLoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(254);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TwoFactorCode).MaximumLength(10);
        RuleFor(x => x.RecoveryCode).MaximumLength(20);
    }
}

/// <summary>
/// Email + password for Admin and Staff (customers have no password). Locks the account for
/// 15 minutes after 5 wrong passwords or 2FA codes. Order: password → temporary password → second factor.
/// </summary>
internal sealed class StaffLoginHandler(UserManager<AppUser> users, TokenService tokens, TimeProvider timeProvider) : IHandler
{
    /// <summary>Checks email + password (and the second factor) and returns the user, without issuing tokens.</summary>
    public async Task<Result<AppUser>> AuthenticateAsync(StaffLoginRequest request, bool allowTemporaryPassword = false)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            return CustomerFeatureErrors.InvalidCredentials;
        }

        if (await users.IsLockedOutAsync(user) && !user.IsDeactivated)
        {
            return CustomerFeatureErrors.LockedOut;
        }

        var roles = await users.GetRolesAsync(user);
        if (!roles.Contains(Roles.Admin) && !roles.Contains(Roles.Staff))
        {
            return CustomerFeatureErrors.InvalidCredentials;
        }

        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            await users.AccessFailedAsync(user);
            return await users.IsLockedOutAsync(user) && !user.IsDeactivated ? CustomerFeatureErrors.LockedOut : CustomerFeatureErrors.InvalidCredentials;
        }

        if (user.IsDeactivated)
        {
            return CustomerFeatureErrors.Deactivated; // only told after the right password
        }

        if (user.MustChangePassword && !allowTemporaryPassword)
        {
            return CustomerFeatureErrors.PasswordChangeRequired;
        }

        if (user.TwoFactorEnabled)
        {
            var code = request.TwoFactorCode?.Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
            var recovery = request.RecoveryCode?.Trim();
            if (string.IsNullOrEmpty(code) && string.IsNullOrEmpty(recovery))
            {
                return CustomerFeatureErrors.TwoFactorRequired;
            }

            var valid = !string.IsNullOrEmpty(code)
                ? await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, code)
                : (await users.RedeemTwoFactorRecoveryCodeAsync(user, recovery!)).Succeeded;
            if (!valid)
            {
                await users.AccessFailedAsync(user);
                return await users.IsLockedOutAsync(user) ? CustomerFeatureErrors.LockedOut : CustomerFeatureErrors.InvalidTwoFactorCode;
            }
        }

        await users.ResetAccessFailedCountAsync(user);
        return user;
    }

    public async Task<Result<LoginResponse>> HandleAsync(StaffLoginRequest request, string? ipAddress, CancellationToken cancellationToken)
    {
        var user = await AuthenticateAsync(request);
        if (user.IsFailure)
        {
            return user.Error;
        }

        user.Value.LastLoginAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await users.UpdateAsync(user.Value);
        return LoginResponse.From(await tokens.IssueAsync(user.Value, ipAddress, cancellationToken));
    }
}

internal static class StaffLoginEndpoint
{
    public static void Map(RouteGroupBuilder auth) =>
        auth.MapPost("/staff/login", async (StaffLoginRequest request, HttpContext http, StaffLoginHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(request, http.ClientIp(), ct)).ToOk())
            .WithValidation<StaffLoginRequest>()
            .WithName("StaffLogin");
}
