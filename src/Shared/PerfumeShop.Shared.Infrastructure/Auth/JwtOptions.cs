using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace PerfumeShop.Shared.Infrastructure.Auth;

/// <summary>
/// Section "Auth:Jwt". <see cref="SigningKey"/> is a secret (user-secrets locally,
/// <c>Auth__Jwt__SigningKey</c> in production) of at least 32 bytes.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Auth:Jwt";

    public string Issuer { get; set; } = "perfumeshop";

    public string Audience { get; set; } = "perfumeshop-api";

    public string? SigningKey { get; set; }

    public int AccessTokenMinutes { get; set; } = 15;

    public int CustomerRefreshTokenDays { get; set; } = 30;

    public int StaffRefreshTokenHours { get; set; } = 12;
}

/// <summary>
/// Resolves secrets that must never be committed. Outside Development a missing secret is a
/// startup error; in Development a random one is generated per process (logged as a warning),
/// so the app runs without setup but tokens/codes do not survive a restart.
/// </summary>
public static class SecretKeys
{
    public const int MinimumBytes = 32;

    public static bool IsValid(string? secret) => secret is not null && Encoding.UTF8.GetByteCount(secret) >= MinimumBytes;

    public static byte[] Resolve(string? configured, string settingName, IHostEnvironment environment, ILogger logger)
    {
        if (IsValid(configured))
        {
            return Encoding.UTF8.GetBytes(configured!);
        }

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException($"'{settingName}' must be configured with at least {MinimumBytes} bytes.");
        }

        logger.LogWarning("'{Setting}' is not configured; using a random development-only key. Set it with dotnet user-secrets to keep it across restarts.", settingName);
        return RandomNumberGenerator.GetBytes(64);
    }
}

/// <summary>The one signing key used both to issue (Customers) and to validate (host) tokens.</summary>
public sealed class JwtSigningKey(IOptions<JwtOptions> options, IHostEnvironment environment, ILogger<JwtSigningKey> logger)
{
    private readonly Lazy<SymmetricSecurityKey> _key = new(() =>
        new SymmetricSecurityKey(SecretKeys.Resolve(options.Value.SigningKey, "Auth:Jwt:SigningKey", environment, logger)));

    public SymmetricSecurityKey Key => _key.Value;

    public SigningCredentials Credentials => new(Key, SecurityAlgorithms.HmacSha256);
}
