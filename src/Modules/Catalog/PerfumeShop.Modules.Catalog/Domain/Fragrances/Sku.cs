using PerfumeShop.Modules.Catalog.Domain.Common;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Catalog.Domain.Fragrances;

/// <summary>Our own stock-keeping unit, e.g. "LAT-KHAMRAH-100". Stored uppercase.</summary>
internal sealed record Sku
{
    public const int MaxLength = 64;

    private Sku(string value) => Value = value;

    public string Value { get; }

    public static Result<Sku> Create(string? input)
    {
        var value = input?.Trim().ToUpperInvariant();
        if (value is null || value.Length < 2 || value.Length > MaxLength
            || !char.IsAsciiLetterOrDigit(value[0]) || !char.IsAsciiLetterOrDigit(value[^1])
            || value.Contains("--", StringComparison.Ordinal)
            || !value.All(ch => char.IsAsciiLetterUpper(ch) || char.IsAsciiDigit(ch) || ch == '-'))
        {
            return Result<Sku>.Failure(VariantErrors.SkuInvalid);
        }

        return Result<Sku>.Success(new Sku(value));
    }

    public override string ToString() => Value;
}
