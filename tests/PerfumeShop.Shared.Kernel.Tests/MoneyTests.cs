namespace PerfumeShop.Shared.Kernel.Tests;

public sealed class MoneyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(49.90)]
    [InlineData(99_999_999.99)]
    public void Euro_accepts_valid_amounts(decimal amount)
    {
        var money = Money.Euro(amount);

        Assert.True(money.IsSuccess);
        Assert.Equal(amount, money.Value.Amount);
        Assert.Equal("EUR", money.Value.Currency);
    }

    [Fact]
    public void Rejects_negative_amount() =>
        Assert.Equal(MoneyErrors.Negative, Money.Euro(-0.01m).Error);

    [Fact]
    public void Rejects_more_than_two_decimals() =>
        Assert.Equal(MoneyErrors.TooManyDecimals, Money.Euro(10.999m).Error);

    [Fact]
    public void Rejects_amount_that_does_not_fit_the_column() =>
        Assert.Equal(MoneyErrors.TooLarge, Money.Euro(100_000_000m).Error);

    [Theory]
    [InlineData("eur")]
    [InlineData("EU")]
    [InlineData("EURO")]
    [InlineData("")]
    public void Rejects_invalid_currency(string currency) =>
        Assert.Equal(MoneyErrors.InvalidCurrency, Money.Create(10m, currency).Error);

    [Fact]
    public void Equal_amounts_are_equal_values() =>
        Assert.Equal(Money.Euro(25m).Value, Money.Euro(25.00m).Value);

    [Fact]
    public void Value_of_failed_result_throws() =>
        Assert.Throws<InvalidOperationException>(() => Money.Euro(-1m).Value);
}
