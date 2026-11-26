namespace PerfumeShop.Shared.Kernel;

/// <summary>
/// Amount in a currency, max 2 decimals. Store prices are EUR with 18% VAT included.
/// TODO: arithmetic and VAT breakdown (needed for orders and fiscal receipts).
/// </summary>
public sealed record Money
{
    public const string Eur = "EUR";

    /// <summary>Largest amount that fits numeric(10,2).</summary>
    public const decimal MaxAmount = 99_999_999.99m;

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public static Result<Money> Create(decimal amount, string currency)
    {
        if (amount < 0)
        {
            return Result<Money>.Failure(MoneyErrors.Negative);
        }

        if (amount > MaxAmount)
        {
            return Result<Money>.Failure(MoneyErrors.TooLarge);
        }

        if (decimal.Round(amount, 2) != amount)
        {
            return Result<Money>.Failure(MoneyErrors.TooManyDecimals);
        }

        if (currency is not { Length: 3 } || !currency.All(char.IsAsciiLetterUpper))
        {
            return Result<Money>.Failure(MoneyErrors.InvalidCurrency);
        }

        return Result<Money>.Success(new Money(amount, currency));
    }

    public static Result<Money> Euro(decimal amount) => Create(amount, Eur);

    public override string ToString() => FormattableString.Invariant($"{Amount:0.00} {Currency}");
}

public static class MoneyErrors
{
    public static readonly Error Negative = new("Money.Negative", "Amount cannot be negative.");
    public static readonly Error TooLarge = new("Money.TooLarge", "Amount is too large.");
    public static readonly Error TooManyDecimals = new("Money.TooManyDecimals", "Amount can have at most 2 decimal places.");
    public static readonly Error InvalidCurrency = new("Money.InvalidCurrency", "Currency must be a 3-letter ISO 4217 code, e.g. EUR.");
}
