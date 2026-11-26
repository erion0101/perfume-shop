namespace PerfumeShop.Shared.Infrastructure.Auth;

/// <summary>Application roles, seeded by the Customers module (schema "identity").</summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Staff = "Staff";
    public const string Customer = "Customer";

    public static readonly IReadOnlyList<string> All = [Admin, Staff, Customer];
}

public static class Policies
{
    /// <summary>Admin only: catalog, settings, staff accounts.</summary>
    public const string Admin = "AdminOnly";

    /// <summary>Admin or Staff: back-office work such as orders and customers.</summary>
    public const string Staff = "StaffOrAdmin";

    /// <summary>A signed-in shop customer (token carries a customer_id).</summary>
    public const string Customer = "Customer";
}

/// <summary>Claim names inside our JWTs (short, not the long WS-* URIs).</summary>
public static class PlatformClaims
{
    public const string Subject = "sub";
    public const string Role = "role";
    public const string CustomerId = "customer_id";
}
