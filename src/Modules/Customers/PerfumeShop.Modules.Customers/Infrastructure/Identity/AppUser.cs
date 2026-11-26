using Microsoft.AspNetCore.Identity;

namespace PerfumeShop.Modules.Customers.Infrastructure.Identity;

/// <summary>
/// Login account (schema "identity"). Customers: UserName = phone, no password, OTP login.
/// Admin/Staff: UserName = email, password login (2FA later).
/// </summary>
internal sealed class AppUser : IdentityUser<Guid>
{
    public AppUser() => Id = Guid.CreateVersion7();

    /// <summary>Set for customer accounts; links to customers.customers.</summary>
    public Guid? CustomerId { get; set; }

    public string? DisplayName { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

internal sealed class AppRole : IdentityRole<Guid>
{
    public AppRole() => Id = Guid.CreateVersion7();

    public AppRole(string name) : this() => Name = name;
}
