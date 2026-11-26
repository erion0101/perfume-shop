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

internal sealed record StaffLoginRequest(string Email, string Password);

internal sealed class StaffLoginValidator : AbstractValidator<StaffLoginRequest>
{
    public StaffLoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(254);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(200);
    }
}

/// <summary>
/// Email + password for Admin and Staff (customers have no password). Locks the account for
/// 15 minutes after 5 wrong passwords. TODO: optional 2FA (TOTP) for staff.
/// </summary>
internal sealed class StaffLoginHandler(UserManager<AppUser> users, TokenService tokens) : IHandler
{
    public async Task<Result<LoginResponse>> HandleAsync(StaffLoginRequest request, string? ipAddress, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            return CustomerFeatureErrors.InvalidCredentials;
        }

        if (await users.IsLockedOutAsync(user))
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
            return await users.IsLockedOutAsync(user) ? CustomerFeatureErrors.LockedOut : CustomerFeatureErrors.InvalidCredentials;
        }

        await users.ResetAccessFailedCountAsync(user);
        return LoginResponse.From(await tokens.IssueAsync(user, ipAddress, cancellationToken));
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
