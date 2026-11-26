using System.Globalization;
using System.Security.Claims;
using System.Text;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PerfumeShop.Modules.Customers.Features.Shared;
using PerfumeShop.Modules.Customers.Features.StaffLogin;
using PerfumeShop.Modules.Customers.Infrastructure.Auth;
using PerfumeShop.Modules.Customers.Infrastructure.Identity;
using PerfumeShop.Shared.Infrastructure.Audit;
using PerfumeShop.Shared.Infrastructure.Auth;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Customers.Features.StaffAccounts;

internal sealed record StaffAccountDto(
    Guid Id, string Email, string? DisplayName, string Role, bool IsActive, bool MustChangePassword, bool TwoFactorEnabled, bool LockedOut,
    DateTime? LastLoginAtUtc, DateTime CreatedAtUtc);

internal sealed record CreateStaffRequest(string DisplayName, string Email, string Role, string TemporaryPassword);

internal sealed record ResetStaffPasswordRequest(string TemporaryPassword);

/// <summary>First sign-in (or after a reset): the temporary password becomes the user's own.</summary>
internal sealed record InitialPasswordRequest(string Email, string TemporaryPassword, string NewPassword, string? TwoFactorCode = null);

internal sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

internal sealed record StaffMeDto(
    Guid Id, string Email, string? DisplayName, IReadOnlyList<string> Roles, bool TwoFactorEnabled, bool TwoFactorRequired, int RecoveryCodesLeft, DateTime? LastLoginAtUtc);

/// <param name="AuthenticatorUri">otpauth:// link for the QR code (Google Authenticator, Microsoft Authenticator, 1Password, ...).</param>
internal sealed record TwoFactorSetupDto(string SharedKey, string AuthenticatorUri);

internal sealed record TwoFactorCodeRequest(string Code);

/// <summary>Shown once: each code signs in once when the phone is lost.</summary>
internal sealed record RecoveryCodesDto(IReadOnlyList<string> RecoveryCodes);

internal sealed class CreateStaffValidator : AbstractValidator<CreateStaffRequest>
{
    public CreateStaffValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(x => x.Role).Must(r => r is Roles.Admin or Roles.Staff).WithMessage("Role must be Admin or Staff.");
        RuleFor(x => x.TemporaryPassword).NotEmpty().MaximumLength(200);
    }
}

internal sealed class ResetStaffPasswordValidator : AbstractValidator<ResetStaffPasswordRequest>
{
    public ResetStaffPasswordValidator() => RuleFor(x => x.TemporaryPassword).NotEmpty().MaximumLength(200);
}

internal sealed class InitialPasswordValidator : AbstractValidator<InitialPasswordRequest>
{
    public InitialPasswordValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(254);
        RuleFor(x => x.TemporaryPassword).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NewPassword).NotEmpty().MaximumLength(200).NotEqual(x => x.TemporaryPassword).WithMessage("The new password must differ from the temporary one.");
    }
}

internal sealed class ChangePasswordValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NewPassword).NotEmpty().MaximumLength(200).NotEqual(x => x.CurrentPassword).WithMessage("The new password must differ from the current one.");
    }
}

internal sealed class TwoFactorCodeValidator : AbstractValidator<TwoFactorCodeRequest>
{
    public TwoFactorCodeValidator() => RuleFor(x => x.Code).NotEmpty().MaximumLength(10);
}

internal static class StaffErrors
{
    public static readonly Error NotFound = Error.NotFound("Customers.Staff.NotFound", "Staff account not found.");
    public static readonly Error EmailTaken = Error.Conflict("Customers.Staff.EmailTaken", "An account with this email already exists.");
    public static readonly Error CannotChangeSelf = Error.Conflict("Customers.Staff.CannotChangeSelf", "You cannot deactivate your own account.");
    public static readonly Error LastAdmin = Error.Conflict("Customers.Staff.LastAdmin", "At least one active Admin must remain.");
    public static readonly Error WrongPassword = Error.Validation("Customers.Staff.WrongPassword", "The current password is wrong.");
    public static readonly Error TwoFactorAlreadyEnabled = Error.Conflict("Customers.Auth.TwoFactorAlreadyEnabled", "Two-factor sign-in is already on.");
    public static readonly Error TwoFactorNotEnabled = Error.Conflict("Customers.Auth.TwoFactorNotEnabled", "Two-factor sign-in is not on.");
    public static readonly Error TwoFactorRequiredForRole = Error.Conflict("Customers.Auth.TwoFactorRequiredForRole", "Admins must keep two-factor sign-in on.");
    public static readonly Error TwoFactorNotSetUp = Error.Conflict("Customers.Auth.TwoFactorNotSetUp", "Start the authenticator setup first.");

    /// <summary>Identity password rules (length 12, digit, upper, lower, symbol) as one validation error.</summary>
    public static Error PasswordRules(IdentityResult result) => Error.Validation(
        "Customers.Staff.PasswordWeak",
        "Password: " + string.Join(" ", result.Errors.Select(e => e.Description))).WithDetails("rules", result.Errors.Select(e => e.Code).ToList());
}

/// <summary>Admin manages staff accounts. Every change is audited and revokes the user's sessions where it matters.</summary>
internal sealed class StaffAccountsHandler(UserManager<AppUser> users, TokenService tokens, IAuditLog audit, TimeProvider timeProvider) : IHandler
{
    public async Task<IReadOnlyList<StaffAccountDto>> ListAsync(CancellationToken ct)
    {
        var staff = (await users.GetUsersInRoleAsync(Roles.Staff)).Concat(await users.GetUsersInRoleAsync(Roles.Admin)).DistinctBy(u => u.Id).ToList();
        List<StaffAccountDto> result = [];
        foreach (var user in staff.OrderBy(u => u.Email, StringComparer.OrdinalIgnoreCase))
        {
            result.Add(await ToDtoAsync(user));
        }

        return result;
    }

    public async Task<Result<StaffAccountDto>> CreateAsync(CreateStaffRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await users.FindByEmailAsync(email) is not null || await users.FindByNameAsync(email) is not null)
        {
            return StaffErrors.EmailTaken;
        }

        var user = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = request.DisplayName.Trim(),
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            MustChangePassword = true,
        };
        var created = await users.CreateAsync(user, request.TemporaryPassword);
        if (!created.Succeeded)
        {
            return StaffErrors.PasswordRules(created);
        }

        await users.AddToRoleAsync(user, request.Role);
        await audit.RecordAsync(new AuditEntry("Staff.Account.Created", "StaffAccount", user.Id.ToString(), $"Llogari e re {email} ({request.Role})",
            new Dictionary<string, object?> { ["email"] = email, ["role"] = request.Role }), ct);
        return await ToDtoAsync(user);
    }

    public async Task<Result<StaffAccountDto>> SetActiveAsync(Guid id, bool active, Guid actorId, CancellationToken ct)
    {
        var user = await FindStaffAsync(id);
        if (user is null)
        {
            return StaffErrors.NotFound;
        }

        if (!active && id == actorId)
        {
            return StaffErrors.CannotChangeSelf;
        }

        if (!active && await users.IsInRoleAsync(user, Roles.Admin) && (await users.GetUsersInRoleAsync(Roles.Admin)).Count(u => !u.IsDeactivated) <= 1)
        {
            return StaffErrors.LastAdmin;
        }

        user.IsDeactivated = !active;
        await users.UpdateAsync(user);
        await users.SetLockoutEndDateAsync(user, active ? null : DateTimeOffset.MaxValue);
        if (!active)
        {
            await users.UpdateSecurityStampAsync(user);
            await tokens.RevokeAllForUserAsync(user.Id, "deactivated", ct);
        }

        await audit.RecordAsync(new AuditEntry(active ? "Staff.Account.Reactivated" : "Staff.Account.Deactivated", "StaffAccount", user.Id.ToString(),
            $"{(active ? "Riaktivizuar" : "Çaktivizuar")}: {user.Email}"), ct);
        return await ToDtoAsync(user);
    }

    public async Task<Result<StaffAccountDto>> ResetPasswordAsync(Guid id, ResetStaffPasswordRequest request, CancellationToken ct)
    {
        var user = await FindStaffAsync(id);
        if (user is null)
        {
            return StaffErrors.NotFound;
        }

        // Check the rules first, so a weak temporary password never leaves the account without one.
        foreach (var validator in users.PasswordValidators)
        {
            var valid = await validator.ValidateAsync(users, user, request.TemporaryPassword);
            if (!valid.Succeeded)
            {
                return StaffErrors.PasswordRules(valid);
            }
        }

        await users.RemovePasswordAsync(user);
        var reset = await users.AddPasswordAsync(user, request.TemporaryPassword);
        if (!reset.Succeeded)
        {
            return StaffErrors.PasswordRules(reset);
        }

        user.MustChangePassword = true;
        await users.UpdateAsync(user);
        await users.SetLockoutEndDateAsync(user, user.IsDeactivated ? DateTimeOffset.MaxValue : null);
        await users.ResetAccessFailedCountAsync(user);
        await tokens.RevokeAllForUserAsync(user.Id, "password-reset", ct);
        await audit.RecordAsync(new AuditEntry("Staff.Account.PasswordReset", "StaffAccount", user.Id.ToString(), $"Fjalëkalim i përkohshëm për {user.Email}"), ct);
        return await ToDtoAsync(user);
    }

    private async Task<AppUser?> FindStaffAsync(Guid id)
    {
        var user = await users.FindByIdAsync(id.ToString());
        return user is not null && (await users.IsInRoleAsync(user, Roles.Admin) || await users.IsInRoleAsync(user, Roles.Staff)) ? user : null;
    }

    private async Task<StaffAccountDto> ToDtoAsync(AppUser u)
    {
        var roles = await users.GetRolesAsync(u);
        var lockedOut = !u.IsDeactivated && await users.IsLockedOutAsync(u);
        return new StaffAccountDto(u.Id, u.Email ?? "", u.DisplayName, roles.Contains(Roles.Admin) ? Roles.Admin : Roles.Staff, !u.IsDeactivated, u.MustChangePassword,
            u.TwoFactorEnabled, lockedOut, u.LastLoginAtUtc, u.CreatedAtUtc);
    }
}

/// <summary>The signed-in staff member's own account: password and two-factor sign-in.</summary>
internal sealed class MyAccountHandler(UserManager<AppUser> users, StaffLoginHandler login, IOptionsMonitor<StaffSecurityOptions> security, IAuditLog audit) : IHandler
{
    private const string Issuer = "PerfumeShop Admin";

    public async Task<Result<StaffMeDto>> GetAsync(Guid userId)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return StaffErrors.NotFound;
        }

        var roles = await users.GetRolesAsync(user);
        return new StaffMeDto(user.Id, user.Email ?? "", user.DisplayName, [.. roles.Order()], user.TwoFactorEnabled,
            roles.Contains(Roles.Admin) && security.CurrentValue.RequireTwoFactorForAdmins, await users.CountRecoveryCodesAsync(user), user.LastLoginAtUtc);
    }

    /// <summary>No token needed: the temporary password (and the 2FA code if on) proves who it is.</summary>
    public async Task<Result> InitialPasswordAsync(InitialPasswordRequest request, CancellationToken ct)
    {
        var user = await login.AuthenticateAsync(new StaffLoginRequest(request.Email, request.TemporaryPassword, request.TwoFactorCode), allowTemporaryPassword: true);
        if (user.IsFailure)
        {
            return user.Error;
        }

        var changed = await users.ChangePasswordAsync(user.Value, request.TemporaryPassword, request.NewPassword);
        if (!changed.Succeeded)
        {
            return StaffErrors.PasswordRules(changed);
        }

        user.Value.MustChangePassword = false;
        await users.UpdateAsync(user.Value);
        return Result.Success();
    }

    public async Task<Result> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return StaffErrors.NotFound;
        }

        if (!await users.CheckPasswordAsync(user, request.CurrentPassword))
        {
            await users.AccessFailedAsync(user);
            return StaffErrors.WrongPassword;
        }

        var changed = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!changed.Succeeded)
        {
            return StaffErrors.PasswordRules(changed);
        }

        user.MustChangePassword = false;
        await users.UpdateAsync(user);
        await audit.RecordAsync(new AuditEntry("Staff.Account.PasswordChanged", "StaffAccount", user.Id.ToString(), $"Fjalëkalimi u ndryshua nga {user.Email}"), ct);
        return Result.Success();
    }

    /// <summary>New authenticator key (shown as a QR code); 2FA turns on only after <see cref="EnableAsync"/> with a valid code.</summary>
    public async Task<Result<TwoFactorSetupDto>> SetupAsync(Guid userId)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return StaffErrors.NotFound;
        }

        if (user.TwoFactorEnabled)
        {
            return StaffErrors.TwoFactorAlreadyEnabled;
        }

        await users.ResetAuthenticatorKeyAsync(user);
        var key = (await users.GetAuthenticatorKeyAsync(user))!;
        var label = Uri.EscapeDataString($"{Issuer}:{user.Email}");
        return new TwoFactorSetupDto(Group(key), $"otpauth://totp/{label}?secret={key}&issuer={Uri.EscapeDataString(Issuer)}&digits=6&period=30");
    }

    public async Task<Result<RecoveryCodesDto>> EnableAsync(Guid userId, TwoFactorCodeRequest request, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return StaffErrors.NotFound;
        }

        if (user.TwoFactorEnabled)
        {
            return StaffErrors.TwoFactorAlreadyEnabled;
        }

        if (await users.GetAuthenticatorKeyAsync(user) is null)
        {
            return StaffErrors.TwoFactorNotSetUp;
        }

        if (!await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, Clean(request.Code)))
        {
            return CustomerFeatureErrors.InvalidTwoFactorCode;
        }

        await users.SetTwoFactorEnabledAsync(user, true);
        var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        await audit.RecordAsync(new AuditEntry("Staff.TwoFactor.Enabled", "StaffAccount", user.Id.ToString(), $"2FA u aktivizua për {user.Email}"), ct);
        return new RecoveryCodesDto([.. codes!]);
    }

    public async Task<Result<RecoveryCodesDto>> NewRecoveryCodesAsync(Guid userId, TwoFactorCodeRequest request, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return StaffErrors.NotFound;
        }

        if (!user.TwoFactorEnabled)
        {
            return StaffErrors.TwoFactorNotEnabled;
        }

        if (!await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, Clean(request.Code)))
        {
            return CustomerFeatureErrors.InvalidTwoFactorCode;
        }

        var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        await audit.RecordAsync(new AuditEntry("Staff.TwoFactor.RecoveryCodesRenewed", "StaffAccount", user.Id.ToString(), $"Kode të reja rikuperimi për {user.Email}"), ct);
        return new RecoveryCodesDto([.. codes!]);
    }

    public async Task<Result> DisableAsync(Guid userId, TwoFactorCodeRequest request, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return StaffErrors.NotFound;
        }

        if (!user.TwoFactorEnabled)
        {
            return StaffErrors.TwoFactorNotEnabled;
        }

        if (await users.IsInRoleAsync(user, Roles.Admin) && security.CurrentValue.RequireTwoFactorForAdmins)
        {
            return StaffErrors.TwoFactorRequiredForRole;
        }

        if (!await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, Clean(request.Code)))
        {
            return CustomerFeatureErrors.InvalidTwoFactorCode;
        }

        await users.SetTwoFactorEnabledAsync(user, false);
        await users.ResetAuthenticatorKeyAsync(user);
        await audit.RecordAsync(new AuditEntry("Staff.TwoFactor.Disabled", "StaffAccount", user.Id.ToString(), $"2FA u çaktivizua për {user.Email}"), ct);
        return Result.Success();
    }

    private static string Clean(string code) => code.Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);

    /// <summary>"ABCD EFGH ..." is easier to type into an app than one long string.</summary>
    private static string Group(string key)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < key.Length; i += 4)
        {
            builder.Append(CultureInfo.InvariantCulture, $"{(i > 0 ? " " : "")}{key.Substring(i, Math.Min(4, key.Length - i)).ToLowerInvariant()}");
        }

        return builder.ToString();
    }
}

internal static class StaffAccountsEndpoint
{
    public static void Map(RouteGroupBuilder auth, RouteGroupBuilder staffArea, RouteGroupBuilder adminArea)
    {
        auth.MapPost("/staff/password/initial", async Task<Results<NoContent, ProblemHttpResult>> (InitialPasswordRequest request, MyAccountHandler h, CancellationToken ct) =>
                (await h.InitialPasswordAsync(request, ct)).ToNoContent())
            .WithValidation<InitialPasswordRequest>()
            .WithName("SetInitialStaffPassword")
            .WithDescription("Temporary password → own password (first sign-in or after an Admin reset). Then sign in normally.");

        var me = staffArea.MapGroup("/me").WithTags("Staff (my account)");
        me.MapGet("", async (ClaimsPrincipal user, MyAccountHandler h) => (await h.GetAsync(user.GetUserId()!.Value)).ToOk()).WithName("GetMyStaffAccount");
        me.MapPost("/password", async (ChangePasswordRequest request, ClaimsPrincipal user, MyAccountHandler h, CancellationToken ct) =>
                (await h.ChangePasswordAsync(user.GetUserId()!.Value, request, ct)).ToNoContent())
            .WithValidation<ChangePasswordRequest>()
            .WithName("ChangeMyPassword");
        me.MapPost("/2fa/setup", async (ClaimsPrincipal user, MyAccountHandler h) => (await h.SetupAsync(user.GetUserId()!.Value)).ToOk()).WithName("SetUpTwoFactor");
        me.MapPost("/2fa/enable", async (TwoFactorCodeRequest request, ClaimsPrincipal user, MyAccountHandler h, CancellationToken ct) =>
                (await h.EnableAsync(user.GetUserId()!.Value, request, ct)).ToOk())
            .WithValidation<TwoFactorCodeRequest>()
            .WithName("EnableTwoFactor")
            .WithDescription("Verifies the first code from the app, turns 2FA on and returns 10 recovery codes (shown once). Refresh the session afterwards.");
        me.MapPost("/2fa/recovery-codes", async (TwoFactorCodeRequest request, ClaimsPrincipal user, MyAccountHandler h, CancellationToken ct) =>
                (await h.NewRecoveryCodesAsync(user.GetUserId()!.Value, request, ct)).ToOk())
            .WithValidation<TwoFactorCodeRequest>()
            .WithName("RenewRecoveryCodes");
        me.MapPost("/2fa/disable", async (TwoFactorCodeRequest request, ClaimsPrincipal user, MyAccountHandler h, CancellationToken ct) =>
                (await h.DisableAsync(user.GetUserId()!.Value, request, ct)).ToNoContent())
            .WithValidation<TwoFactorCodeRequest>()
            .WithName("DisableTwoFactor");

        adminArea.MapGet("", async (StaffAccountsHandler h, CancellationToken ct) => TypedResults.Ok(await h.ListAsync(ct))).WithName("ListStaffAccounts");
        adminArea.MapPost("", async (CreateStaffRequest request, StaffAccountsHandler h, CancellationToken ct) =>
                (await h.CreateAsync(request, ct)).ToCreated(s => $"/api/admin/staff/{s.Id}"))
            .WithValidation<CreateStaffRequest>()
            .WithName("CreateStaffAccount")
            .WithDescription("Temporary password: the user must choose their own at first sign-in.");
        adminArea.MapPost("/{id:guid}/deactivate", async (Guid id, ClaimsPrincipal user, StaffAccountsHandler h, CancellationToken ct) =>
                (await h.SetActiveAsync(id, false, user.GetUserId()!.Value, ct)).ToOk())
            .WithName("DeactivateStaffAccount");
        adminArea.MapPost("/{id:guid}/reactivate", async (Guid id, ClaimsPrincipal user, StaffAccountsHandler h, CancellationToken ct) =>
                (await h.SetActiveAsync(id, true, user.GetUserId()!.Value, ct)).ToOk())
            .WithName("ReactivateStaffAccount");
        adminArea.MapPost("/{id:guid}/reset-password", async (Guid id, ResetStaffPasswordRequest request, StaffAccountsHandler h, CancellationToken ct) =>
                (await h.ResetPasswordAsync(id, request, ct)).ToOk())
            .WithValidation<ResetStaffPasswordRequest>()
            .WithName("ResetStaffPassword");
    }
}
