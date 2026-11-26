using PerfumeShop.Modules.Catalog.Domain.Common;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Catalog.Domain.Fragrances;

/// <summary>Barcode printed on the box: GTIN-8, GTIN-12 (UPC), GTIN-13 (EAN) or GTIN-14, check digit verified.</summary>
internal sealed record Ean
{
    public const int MaxLength = 14;

    private Ean(string value) => Value = value;

    public string Value { get; }

    public static Result<Ean> Create(string? input)
    {
        var value = input?.Replace(" ", string.Empty, StringComparison.Ordinal).Trim();
        if (value is not { Length: 8 or 12 or 13 or 14 } || !value.All(char.IsAsciiDigit) || !HasValidCheckDigit(value))
        {
            return Result<Ean>.Failure(VariantErrors.EanInvalid);
        }

        return Result<Ean>.Success(new Ean(value));
    }

    public override string ToString() => Value;

    private static bool HasValidCheckDigit(string digits)
    {
        // GS1: from the right (excluding the check digit) weights alternate 3, 1, 3, 1, ...
        var sum = 0;
        for (int i = digits.Length - 2, weight = 3; i >= 0; i--, weight = 4 - weight)
        {
            sum += (digits[i] - '0') * weight;
        }

        var checkDigit = (10 - (sum % 10)) % 10;
        return checkDigit == digits[^1] - '0';
    }
}
