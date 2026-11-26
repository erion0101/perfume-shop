using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PerfumeShop.Modules.Customers.Domain.Common;
using PerfumeShop.Shared.Infrastructure.Auth;

namespace PerfumeShop.Modules.Customers.Infrastructure.Auth;

/// <summary>Section "Customers:Otp". <see cref="HashKey"/> is a secret (user-secrets / env var).</summary>
internal sealed class OtpOptions
{
    public const string SectionName = "Customers:Otp";

    public string? HashKey { get; set; }

    public int CodeLifetimeMinutes { get; set; } = 5;

    public int MaxAttempts { get; set; } = 5;

    public int RateLimitWindowMinutes { get; set; } = 15;

    public int MaxCodesPerPhone { get; set; } = 3;

    /// <summary>Higher than per phone: Kosovo mobile operators put many customers behind one IP (CGNAT).</summary>
    public int MaxCodesPerIp { get; set; } = 10;
}

/// <summary>Section "Auth:SeedAdmin": first Admin account, from user-secrets / env vars only.</summary>
internal sealed class SeedAdminOptions
{
    public const string SectionName = "Auth:SeedAdmin";

    public string? Email { get; set; }

    public string? Password { get; set; }

    public string? DisplayName { get; set; }
}

/// <summary>Generates OTP codes and compares them through an HMAC, so a leaked table reveals no codes.</summary>
internal sealed class OtpCodes(IOptions<OtpOptions> options, IHostEnvironment environment, ILogger<OtpCodes> logger)
{
    public const int Length = 6;

    private readonly Lazy<byte[]> _key = new(() => SecretKeys.Resolve(options.Value.HashKey, "Customers:Otp:HashKey", environment, logger));

    public static string Generate() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

    public string Hash(Guid verificationId, PhoneNumber phone, string code) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(_key.Value, Encoding.UTF8.GetBytes($"{verificationId:N}:{phone.Value}:{code}")));

    public bool Matches(Guid verificationId, PhoneNumber phone, string code, string storedHash) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Hash(verificationId, phone, code)),
            Encoding.ASCII.GetBytes(storedHash));
}
