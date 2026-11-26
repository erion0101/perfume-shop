using Microsoft.AspNetCore.Identity;

namespace PerfumeShop.Modules.Customers.Infrastructure.Identity;

/// <summary>
/// Login account (schema "identity"). Customers: UserName = phone, no password, OTP login.
/// Admin/Staff: UserName = email, password login, optional/required TOTP 2FA (authenticator app).
/// </summary>
internal sealed class AppUser : IdentityUser<Guid>
{
    public AppUser() => Id = Guid.CreateVersion7();

    /// <summary>Set for customer accounts; links to customers.customers.</summary>
    public Guid? CustomerId { get; set; }

    public string? DisplayName { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Staff: set by Admin with a temporary password; sign-in is refused until the user picks their own.</summary>
    public bool MustChangePassword { get; set; }

    /// <summary>Staff: switched off by Admin (also locked out and all sessions revoked).</summary>
    public bool IsDeactivated { get; set; }

    public DateTime? LastLoginAtUtc { get; set; }
}

internal sealed class AppRole : IdentityRole<Guid>
{
    public AppRole() => Id = Guid.CreateVersion7();

    public AppRole(string name) : this() => Name = name;
}
