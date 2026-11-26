using PerfumeShop.Modules.Customers.Domain.Common;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Customers.Domain.Verification;

/// <summary>
/// One OTP code sent to a phone. Only a hash of the code is stored (hashing is infrastructure).
/// A code is usable once, until it expires, for a limited number of attempts, and only while it
/// is the latest code for that phone (requesting a new code invalidates older ones).
/// </summary>
internal sealed class PhoneVerification : Entity<Guid>
{
    public const int IpAddressMaxLength = 45;

    private PhoneVerification() { }

    private PhoneVerification(Guid id, PhoneNumber phone, string codeHash, DateTime createdAtUtc, DateTime expiresAtUtc, int maxAttempts, string? ipAddress)
        : base(id)
    {
        Phone = phone;
        CodeHash = codeHash;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        MaxAttempts = maxAttempts;
        IpAddress = ipAddress;
    }

    public PhoneNumber Phone { get; private set; } = null!;

    public string CodeHash { get; private set; } = null!;

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime ExpiresAtUtc { get; private set; }

    public int Attempts { get; private set; }

    public int MaxAttempts { get; private set; }

    public DateTime? ConsumedAtUtc { get; private set; }

    public DateTime? InvalidatedAtUtc { get; private set; }

    public string? IpAddress { get; private set; }

    /// <param name="id">Chosen by the caller because the code hash includes it.</param>
    public static PhoneVerification Issue(Guid id, PhoneNumber phone, string codeHash, DateTime nowUtc, TimeSpan validFor, int maxAttempts, string? ipAddress) =>
        new(id, phone, codeHash, nowUtc, nowUtc.Add(validFor), maxAttempts,
            ipAddress is { Length: > IpAddressMaxLength } ? ipAddress[..IpAddressMaxLength] : ipAddress);

    public bool IsPending => ConsumedAtUtc is null && InvalidatedAtUtc is null;

    /// <summary>A newer code was requested, or the SMS could not be sent.</summary>
    public void Invalidate(DateTime nowUtc) => InvalidatedAtUtc ??= nowUtc;

    /// <summary>
    /// Counts the attempt (also when wrong, so guessing is limited) and consumes the code on success.
    /// </summary>
    public Result Verify(bool codeMatches, DateTime nowUtc)
    {
        if (!IsPending)
        {
            return OtpErrors.InvalidCode;
        }

        if (nowUtc >= ExpiresAtUtc)
        {
            return OtpErrors.Expired;
        }

        if (Attempts >= MaxAttempts)
        {
            return OtpErrors.TooManyAttempts;
        }

        Attempts++;

        if (!codeMatches)
        {
            return Attempts >= MaxAttempts ? OtpErrors.TooManyAttempts : OtpErrors.WrongCode(MaxAttempts - Attempts);
        }

        ConsumedAtUtc = nowUtc;
        return Result.Success();
    }
}
