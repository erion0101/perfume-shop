using PerfumeShop.Modules.Catalog.Domain.Common;

namespace PerfumeShop.Modules.Catalog.Tests.Domain;

public sealed class SlugTests
{
    [Theory]
    [InlineData("Maison Alhambra", "maison-alhambra")]
    [InlineData("Lattafa Khamrah EDP", "lattafa-khamrah-edp")]
    [InlineData("Parfum për meshkuj", "parfum-per-meshkuj")]
    [InlineData("ËNDËRR ÇAJI", "enderr-caji")]
    [InlineData("Çikëlë e çmuar", "cikele-e-cmuar")]
    [InlineData("Club de Nuit Intense Man", "club-de-nuit-intense-man")]
    [InlineData("L'Homme Idéal", "lhomme-ideal")]
    [InlineData("Dolce & Gabbana", "dolce-and-gabbana")]
    [InlineData("  9PM -- Rebel!!  ", "9pm-rebel")]
    [InlineData("Ana Abiyedh / Rouge", "ana-abiyedh-rouge")]
    [InlineData("Straße Ølf Łódź", "strasse-olf-lodz")]
    public void Creates_url_safe_lowercase_slug(string input, string expected) =>
        Assert.Equal(expected, Slug.Create(input).Ok().Value);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!! --- ???")]
    [InlineData("عود")]
    public void Rejects_input_without_letters_or_digits(string? input) =>
        Slug.Create(input).Fails(SlugErrors.Empty);

    [Fact]
    public void Rejects_too_long_slug() =>
        Slug.Create(new string('a', Slug.MaxLength + 1)).Fails(SlugErrors.TooLong);

    [Fact]
    public void Creating_from_a_slug_returns_the_same_slug()
    {
        var slug = Slug.Create("Afnan 9PM Rebel").Ok();

        Assert.Equal(slug, Slug.Create(slug.Value).Ok());
    }

    [Fact]
    public void WithSuffix_appends_a_number()
    {
        var slug = Slug.Create("lattafa-khamrah").Ok();

        Assert.Equal("lattafa-khamrah-2", slug.WithSuffix(2).Ok().Value);
        slug.WithSuffix(1).Fails(SlugErrors.InvalidSuffix);
    }
}
