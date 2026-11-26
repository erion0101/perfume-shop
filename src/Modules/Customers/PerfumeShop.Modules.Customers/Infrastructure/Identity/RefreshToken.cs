namespace PerfumeShop.Modules.Customers.Infrastructure.Identity;

/// <summary>
/// Long-lived token used to get a new access token. Only its SHA-256 hash is stored.
/// Every refresh rotates it (old revoked, new one in the same family). Presenting an already
/// revoked token means it was stolen or replayed, so the whole family is revoked.
/// </summary>
internal sealed class RefreshToken
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid UserId { get; init; }

    public required string TokenHash { get; init; }

    /// <summary>All tokens descending from one login share a family.</summary>
    public Guid FamilyId { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime ExpiresAtUtc { get; init; }

    public string? CreatedByIp { get; init; }

    public DateTime? RevokedAtUtc { get; set; }

    public string? RevocationReason { get; set; }

    public Guid? ReplacedByTokenId { get; set; }
}

internal static class RevocationReasons
{
    public const string Rotated = "Rotated";
    public const string Logout = "Logout";
    public const string ReuseDetected = "ReuseDetected";
    public const string Blocked = "CustomerBlocked";
    public const string LockedOut = "LockedOut";
}
