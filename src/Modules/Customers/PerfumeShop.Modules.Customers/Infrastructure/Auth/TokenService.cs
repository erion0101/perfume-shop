using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using PerfumeShop.Modules.Customers.Infrastructure.Identity;
using PerfumeShop.Shared.Infrastructure.Auth;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Customers.Infrastructure.Auth;

internal sealed record AuthTokens(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    string TokenType = "Bearer");

internal static class TokenErrors
{
    public static readonly Error InvalidRefreshToken = Error.Unauthorized(
        "Customers.Auth.InvalidRefreshToken", "The refresh token is invalid, expired or revoked. Sign in again.");
}

/// <summary>
/// Issues short-lived JWT access tokens and rotating refresh tokens. Customers and staff use the
/// same scheme; roles (and the customer_id claim) decide what a token can do.
/// </summary>
internal sealed class TokenService(
    CustomersDbContext db,
    UserManager<AppUser> users,
    JwtSigningKey signingKey,
    IOptions<JwtOptions> options,
    TimeProvider timeProvider)
{
    private static readonly JsonWebTokenHandler Handler = new();

    public async Task<AuthTokens> IssueAsync(AppUser user, string? ipAddress, CancellationToken cancellationToken) =>
        (await IssueCoreAsync(user, ipAddress, familyId: null, cancellationToken)).Tokens;

    private async Task<(AuthTokens Tokens, Guid RefreshTokenId)> IssueCoreAsync(AppUser user, string? ipAddress, Guid? familyId, CancellationToken cancellationToken)
    {
        var roles = await users.GetRolesAsync(user);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var jwt = options.Value;

        var accessExpires = now.AddMinutes(jwt.AccessTokenMinutes);
        var claims = new Dictionary<string, object>
        {
            [PlatformClaims.Subject] = user.Id.ToString(),
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
            [PlatformClaims.Role] = roles.ToArray(),
        };
        if (user.CustomerId is { } customerId)
        {
            claims[PlatformClaims.CustomerId] = customerId.ToString();
        }

        var accessToken = Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            Claims = claims,
            IssuedAt = now,
            NotBefore = now,
            Expires = accessExpires,
            SigningCredentials = signingKey.Credentials,
        });

        // Back-office sessions are short; shop customers stay signed in on their phone.
        var isStaff = roles.Contains(Roles.Admin) || roles.Contains(Roles.Staff);
        var refreshExpires = isStaff ? now.AddHours(jwt.StaffRefreshTokenHours) : now.AddDays(jwt.CustomerRefreshTokenDays);
        var rawRefresh = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

        var refresh = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = HashRefreshToken(rawRefresh),
            FamilyId = familyId ?? Guid.CreateVersion7(),
            CreatedAtUtc = now,
            ExpiresAtUtc = refreshExpires,
            CreatedByIp = ipAddress is { Length: > 45 } ? ipAddress[..45] : ipAddress,
        };
        db.RefreshTokens.Add(refresh);
        await db.SaveChangesAsync(cancellationToken);

        return (new AuthTokens(accessToken, accessExpires, rawRefresh, refreshExpires), refresh.Id);
    }

    public async Task<Result<AuthTokens>> RefreshAsync(string rawRefreshToken, string? ipAddress, CancellationToken cancellationToken)
    {
        var hash = HashRefreshToken(rawRefreshToken);
        var token = await db.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (token is null)
        {
            return TokenErrors.InvalidRefreshToken;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (token.ExpiresAtUtc <= now)
        {
            return TokenErrors.InvalidRefreshToken;
        }

        // Atomic: of two concurrent refreshes with the same token only one can win.
        var claimed = await db.RefreshTokens
            .Where(t => t.Id == token.Id && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAtUtc, now)
                .SetProperty(t => t.RevocationReason, RevocationReasons.Rotated), cancellationToken);
        if (claimed == 0)
        {
            await RevokeFamilyAsync(token.FamilyId, RevocationReasons.ReuseDetected, cancellationToken);
            return TokenErrors.InvalidRefreshToken;
        }

        var user = await users.FindByIdAsync(token.UserId.ToString());
        if (user is null)
        {
            return TokenErrors.InvalidRefreshToken;
        }

        if (await users.IsLockedOutAsync(user))
        {
            await RevokeFamilyAsync(token.FamilyId, RevocationReasons.LockedOut, cancellationToken);
            return TokenErrors.InvalidRefreshToken;
        }

        if (user.CustomerId is { } customerId
            && await db.Customers.AnyAsync(c => c.Id == customerId && c.Status == Domain.Customers.CustomerStatus.Blocked, cancellationToken))
        {
            await RevokeFamilyAsync(token.FamilyId, RevocationReasons.Blocked, cancellationToken);
            return Domain.Common.CustomerErrors.Blocked;
        }

        var (issued, newId) = await IssueCoreAsync(user, ipAddress, token.FamilyId, cancellationToken);
        await db.RefreshTokens.Where(t => t.Id == token.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ReplacedByTokenId, newId), cancellationToken);

        return issued;
    }

    /// <summary>Logout: revokes this token (and therefore this login session).</summary>
    public async Task RevokeAsync(string rawRefreshToken, CancellationToken cancellationToken)
    {
        var hash = HashRefreshToken(rawRefreshToken);
        var familyId = await db.RefreshTokens.Where(t => t.TokenHash == hash).Select(t => (Guid?)t.FamilyId).FirstOrDefaultAsync(cancellationToken);
        if (familyId is { } family)
        {
            await RevokeFamilyAsync(family, RevocationReasons.Logout, cancellationToken);
        }
    }

    public Task RevokeAllForUserAsync(Guid userId, string reason, CancellationToken cancellationToken) =>
        Revoke(db.RefreshTokens.Where(t => t.UserId == userId), reason, cancellationToken);

    private Task RevokeFamilyAsync(Guid familyId, string reason, CancellationToken cancellationToken) =>
        Revoke(db.RefreshTokens.Where(t => t.FamilyId == familyId), reason, cancellationToken);

    private Task Revoke(IQueryable<RefreshToken> tokens, string reason, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return tokens.Where(t => t.RevokedAtUtc == null).ExecuteUpdateAsync(s => s
            .SetProperty(t => t.RevokedAtUtc, now)
            .SetProperty(t => t.RevocationReason, reason), cancellationToken);
    }

    public static string HashRefreshToken(string raw) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
