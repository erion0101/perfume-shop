using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Customers.Domain.Common;

/// <summary>
/// A MOBILE number in E.164 form, e.g. "+38344123456". Only mobiles are accepted because the
/// number receives the login code by SMS and the courier calls it for Cash on Delivery.
/// <list type="bullet">
/// <item>Kosovo +383: 8 digits, 43-49 (e.g. 044 123 456)</item>
/// <item>Albania +355: 9 digits, 66-69 (e.g. 069 123 4567)</item>
/// <item>North Macedonia +389: 8 digits, 7x (e.g. 070 123 456)</item>
/// </list>
/// Local numbers starting with 0 are read as Kosovo numbers (our first market).
/// </summary>
internal sealed record PhoneNumber
{
    public const int MaxLength = 16;

    private static readonly Dictionary<string, (int Length, Func<string, bool> IsMobile)> Countries = new()
    {
        ["383"] = (8, n => n[0] == '4' && n[1] is >= '3' and <= '9'),
        ["355"] = (9, n => n[0] == '6' && n[1] is >= '6' and <= '9'),
        ["389"] = (8, n => n[0] == '7'),
    };

    private PhoneNumber(string value) => Value = value;

    public string Value { get; }

    public string CountryCode => Value[1..4];

    public static Result<PhoneNumber> Create(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return CustomerErrors.PhoneInvalid;
        }

        var digits = new string([.. input.Trim().Where(ch => !(char.IsWhiteSpace(ch) || ch is '-' or '.' or '(' or ')' or '/'))]);

        if (digits.StartsWith("00", StringComparison.Ordinal))
        {
            digits = "+" + digits[2..];
        }
        else if (digits.StartsWith('0'))
        {
            digits = "+383" + digits[1..];
        }

        if (digits.Length < 5 || digits[0] != '+' || !digits[1..].All(char.IsAsciiDigit))
        {
            return CustomerErrors.PhoneInvalid;
        }

        var countryCode = digits[1..4];
        if (!Countries.TryGetValue(countryCode, out var rules))
        {
            return CustomerErrors.PhoneCountryNotSupported;
        }

        var national = digits[4..];
        return national.Length == rules.Length && rules.IsMobile(national)
            ? new PhoneNumber(digits)
            : CustomerErrors.PhoneInvalid;
    }

    /// <summary>"+383 44 *** 456" for logs and admin lists.</summary>
    public string Masked() => $"+{CountryCode} {Value[4..6]} *** {Value[^3..]}";

    public override string ToString() => Value;
}
