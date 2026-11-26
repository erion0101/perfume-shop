using PerfumeShop.Modules.Catalog.Domain.Common;
using PerfumeShop.Modules.Catalog.Domain.Fragrances;

namespace PerfumeShop.Modules.Catalog.Tests.Domain;

public sealed class VariantCodeTests
{
    [Theory]
    [InlineData("lat-khamrah-100", "LAT-KHAMRAH-100")]
    [InlineData(" AFN9PM50 ", "AFN9PM50")]
    public void Sku_is_trimmed_and_uppercased(string input, string expected) =>
        Assert.Equal(expected, Sku.Create(input).Ok().Value);

    [Theory]
    [InlineData(null)]
    [InlineData("A")]
    [InlineData("-LAT")]
    [InlineData("LAT-")]
    [InlineData("LAT--100")]
    [InlineData("LAT 100")]
    [InlineData("LAT_100")]
    public void Sku_rejects_invalid_values(string? input) =>
        Sku.Create(input).Fails(VariantErrors.SkuInvalid);

    [Theory]
    [InlineData("4006381333931")]   // EAN-13
    [InlineData("96385074")]        // EAN-8
    [InlineData("036000291452")]    // UPC-A (GTIN-12)
    [InlineData("10012345678902")]  // GTIN-14
    [InlineData("4006 3813 3393 1")]
    public void Ean_accepts_valid_gtins(string input) =>
        Assert.True(Ean.Create(input).IsSuccess);

    [Theory]
    [InlineData(null)]
    [InlineData("4006381333932")]   // wrong check digit
    [InlineData("400638133393")]    // 12 digits, wrong check digit for UPC
    [InlineData("12345")]
    [InlineData("40063813339AB")]
    public void Ean_rejects_invalid_values(string? input) =>
        Ean.Create(input).Fails(VariantErrors.EanInvalid);
}
