using PerfumeShop.Modules.Catalog.Domain.Common;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Catalog.Domain.Fragrances;

internal enum SizeUnit
{
    /// <summary>Perfumes, attars, minis.</summary>
    Ml = 1,

    /// <summary>Bakhoor and other solid products sold by weight.</summary>
    Gram = 2,

    /// <summary>Counted items, e.g. a box of 12 bakhoor tablets.</summary>
    Piece = 3,
}

/// <summary>Size of what is sold: 100 ml, 40 g, 12 pieces. Value &gt; 0, at most 1000, one decimal.</summary>
internal sealed record VariantSize
{
    public const decimal MaxValue = 1000m;

    private VariantSize(decimal value, SizeUnit unit)
    {
        Value = value;
        Unit = unit;
    }

    public decimal Value { get; }

    public SizeUnit Unit { get; }

    public static Result<VariantSize> Create(decimal value, SizeUnit unit)
    {
        if (!Enum.IsDefined(unit))
        {
            return Result<VariantSize>.Failure(VariantErrors.InvalidSizeUnit);
        }

        return value <= 0 || value > MaxValue || decimal.Round(value, 1) != value
            ? Result<VariantSize>.Failure(VariantErrors.InvalidSize)
            : Result<VariantSize>.Success(new VariantSize(value, unit));
    }

    public static Result<VariantSize> Ml(decimal value) => Create(value, SizeUnit.Ml);

    public override string ToString() => Unit switch
    {
        SizeUnit.Ml => FormattableString.Invariant($"{Value:0.#} ml"),
        SizeUnit.Gram => FormattableString.Invariant($"{Value:0.#} g"),
        _ => FormattableString.Invariant($"{Value:0.#} pcs"),
    };
}
