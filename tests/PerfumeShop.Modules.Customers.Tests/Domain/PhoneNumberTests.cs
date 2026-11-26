using PerfumeShop.Modules.Customers.Domain.Common;

namespace PerfumeShop.Modules.Customers.Tests.Domain;

public sealed class PhoneNumberTests
{
    [Theory]
    [InlineData("+383 44 123 456", "+38344123456")]
    [InlineData("044 123 456", "+38344123456")]
    [InlineData("044-123-456", "+38344123456")]
    [InlineData("00383 49 123 456", "+38349123456")]
    [InlineData("(043) 123.456", "+38343123456")]
    [InlineData("+355 69 123 4567", "+355691234567")]
    [InlineData("+389 70 123 456", "+38970123456")]
    public void Normalizes_supported_mobile_numbers_to_E164(string input, string expected) =>
        Assert.Equal(expected, PhoneNumber.Create(input).Ok().Value);

    [Theory]
    [InlineData("+383 38 123 456")]   // Prishtina landline
    [InlineData("+383 44 123 45")]    // too short
    [InlineData("+383 44 123 4567")]  // too long
    [InlineData("+355 4 123 4567")]   // Tirana landline
    [InlineData("+389 2 123 4567")]   // Skopje landline
    [InlineData("44123456")]          // no prefix
    [InlineData("+383 4a 123 456")]
    [InlineData("")]
    [InlineData(null)]
    public void Rejects_landlines_and_malformed_numbers(string? input) =>
        PhoneNumber.Create(input).Fails("Customers.Phone.Invalid");

    [Theory]
    [InlineData("+49 151 12345678")]
    [InlineData("+1 202 555 0123")]
    public void Rejects_other_countries(string input) =>
        PhoneNumber.Create(input).Fails("Customers.Phone.CountryNotSupported");

    [Fact]
    public void Masks_the_middle_digits() =>
        Assert.Equal("+383 44 *** 456", PhoneNumber.Create("+38344123456").Ok().Masked());
}
