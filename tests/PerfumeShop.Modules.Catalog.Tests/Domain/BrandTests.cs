using PerfumeShop.Modules.Catalog.Domain.Brands;
using PerfumeShop.Modules.Catalog.Domain.Common;

namespace PerfumeShop.Modules.Catalog.Tests.Domain;

public sealed class BrandTests
{
    private static readonly Slug LattafaSlug = Slug.Create("lattafa").Ok();

    [Fact]
    public void Create_trims_name_and_assigns_id()
    {
        var brand = Brand.Create("  Lattafa  ", LattafaSlug).Ok();

        Assert.Equal("Lattafa", brand.Name);
        Assert.Equal("lattafa", brand.Slug.Value);
        Assert.NotEqual(Guid.Empty, brand.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Create_requires_name(string? name) =>
        Brand.Create(name, LattafaSlug).Fails(BrandErrors.NameRequired);

    [Fact]
    public void Create_rejects_too_long_name() =>
        Brand.Create(new string('x', Brand.NameMaxLength + 1), LattafaSlug).Fails(BrandErrors.NameTooLong);

    [Fact]
    public void UpdateDetails_normalizes_country_code()
    {
        var brand = Brand.Create("Lattafa", LattafaSlug).Ok();

        brand.UpdateDetails("ae", "https://cdn.example.com/lattafa.webp", " Shtëpi parfumesh nga Dubai. ").Ok();

        Assert.Equal("AE", brand.CountryCode);
        Assert.Equal("Shtëpi parfumesh nga Dubai.", brand.Description);
    }

    [Theory]
    [InlineData("UAE")]
    [InlineData("A1")]
    public void UpdateDetails_rejects_invalid_country(string country) =>
        Brand.Create("Lattafa", LattafaSlug).Ok().UpdateDetails(country, null, null).Fails(BrandErrors.InvalidCountryCode);

    [Fact]
    public void UpdateDetails_rejects_relative_logo_url() =>
        Brand.Create("Lattafa", LattafaSlug).Ok().UpdateDetails(null, "/logo.png", null).Fails(BrandErrors.InvalidLogoUrl);
}
