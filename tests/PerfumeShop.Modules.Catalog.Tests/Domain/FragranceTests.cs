using PerfumeShop.Modules.Catalog.Domain.Common;
using PerfumeShop.Modules.Catalog.Domain.Fragrances;
using static PerfumeShop.Modules.Catalog.Tests.TestHelpers;

namespace PerfumeShop.Modules.Catalog.Tests.Domain;

public sealed class FragranceTests
{
    private static readonly Slug AnySlug = Slug.Create("lattafa-khamrah-edp").Ok();

    [Fact]
    public void New_fragrance_is_a_draft_without_variants()
    {
        var fragrance = NewFragrance();

        Assert.Equal(FragranceStatus.Draft, fragrance.Status);
        Assert.Empty(fragrance.Variants);
        Assert.True(fragrance.Notes.IsEmpty);
    }

    [Fact]
    public void Create_requires_brand() =>
        Fragrance.Create(Guid.Empty, "Khamrah", AnySlug, Concentration.EauDeParfum, Gender.Unisex)
            .Fails(FragranceErrors.BrandRequired);

    [Fact]
    public void Create_requires_name() =>
        Fragrance.Create(Guid.CreateVersion7(), " ", AnySlug, Concentration.EauDeParfum, Gender.Unisex)
            .Fails(FragranceErrors.NameRequired);

    [Fact]
    public void Create_rejects_undefined_enum_values()
    {
        Fragrance.Create(Guid.CreateVersion7(), "Khamrah", AnySlug, (Concentration)99, Gender.Unisex)
            .Fails(FragranceErrors.InvalidConcentration);
        Fragrance.Create(Guid.CreateVersion7(), "Khamrah", AnySlug, Concentration.EauDeParfum, (Gender)99)
            .Fails(FragranceErrors.InvalidGender);
    }

    [Theory]
    [InlineData("Lattafa", "Khamrah", (int)Concentration.EauDeParfum, "lattafa-khamrah-edp")]
    [InlineData("Armaf", "Club de Nuit Intense Man", (int)Concentration.EauDeToilette, "armaf-club-de-nuit-intense-man-edt")]
    [InlineData("Al Haramain", "Amber Oud Gold Edition", (int)Concentration.ExtraitDeParfum, "al-haramain-amber-oud-gold-edition-extrait")]
    [InlineData("Ajmal", "Dahn Al Oudh Moattaq", (int)Concentration.Attar, "ajmal-dahn-al-oudh-moattaq-attar")]
    public void SuggestSlug_combines_brand_name_and_concentration(string brand, string name, int concentration, string expected) =>
        Assert.Equal(expected, Fragrance.SuggestSlug(brand, name, (Concentration)concentration).Ok().Value);

    [Fact]
    public void Accords_are_sorted_strongest_first()
    {
        var fragrance = NewFragrance();

        fragrance.SetAccords([Accord.Create("sweet", 80).Ok(), Accord.Create("amber", 100).Ok(), Accord.Create("cinnamon", 65).Ok()]).Ok();

        Assert.Equal(["amber", "sweet", "cinnamon"], fragrance.Accords.Select(a => a.Name));
    }

    [Fact]
    public void Accords_must_be_unique() =>
        NewFragrance().SetAccords([Accord.Create("amber", 100).Ok(), Accord.Create("Amber", 60).Ok()])
            .Fails(FragranceErrors.DuplicateAccord);

    [Fact]
    public void Similar_to_cannot_reference_itself()
    {
        var fragrance = NewFragrance();

        fragrance.SetSimilarTo([FragranceReference.Create("Lattafa", "Khamrah", fragrance.Id).Ok()])
            .Fails(FragranceErrors.SimilarToItself);
    }

    [Fact]
    public void Similar_to_rejects_duplicates() =>
        NewFragrance().SetSimilarTo(
            [FragranceReference.Create("Kilian", "Angels' Share").Ok(), FragranceReference.Create("KILIAN", "angels' share").Ok()])
            .Fails(FragranceErrors.DuplicateSimilar);

    [Theory]
    [InlineData(1899)]
    [InlineData(3000)]
    public void Launch_year_must_be_realistic(int year) =>
        NewFragrance().UpdateDetails(null, year, [], null).Fails(FragranceErrors.InvalidLaunchYear);

    [Fact]
    public void UpdateDetails_trims_and_deduplicates_perfumers()
    {
        var fragrance = NewFragrance();

        fragrance.UpdateDetails(OlfactiveFamily.Amber, 2022, [" Quentin Bisch ", "quentin bisch"], "Parfum i ëmbël oriental.").Ok();

        Assert.Equal(["Quentin Bisch"], fragrance.Perfumers);
        Assert.Equal(2022, fragrance.LaunchYear);
    }

    [Fact]
    public void UpdateProfile_rejects_unknown_season_flags() =>
        NewFragrance().UpdateProfile(Longevity.LongLasting, Sillage.Strong, (Season)64, Occasion.Night)
            .Fails(FragranceErrors.InvalidProfile);

    [Fact]
    public void UpdateProfile_accepts_combined_flags()
    {
        var fragrance = NewFragrance();

        fragrance.UpdateProfile(Longevity.LongLasting, Sillage.Strong, Season.Autumn | Season.Winter, Occasion.Night | Occasion.Evening).Ok();

        Assert.True(fragrance.Seasons.HasFlag(Season.Winter));
        Assert.False(fragrance.Seasons.HasFlag(Season.Summer));
    }

    [Fact]
    public void LinkToFragella_stores_raw_payload()
    {
        var fragrance = NewFragrance();
        var importedAt = DateTime.UtcNow;

        fragrance.LinkToFragella("frg-123", """{"Name":"Khamrah","Brand":"Lattafa"}""", importedAt).Ok();

        Assert.Equal("frg-123", fragrance.FragellaId);
        Assert.Equal(importedAt, fragrance.FragellaImportedAtUtc);
    }

    [Fact]
    public void LinkToFragella_allows_refresh_with_same_id_but_not_a_different_id()
    {
        var fragrance = NewFragrance();
        fragrance.LinkToFragella("frg-123", "{}", DateTime.UtcNow).Ok();

        fragrance.LinkToFragella("frg-123", """{"refreshed":true}""", DateTime.UtcNow).Ok();
        fragrance.LinkToFragella("frg-999", "{}", DateTime.UtcNow).Fails(FragranceErrors.FragellaIdMismatch);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    [InlineData("")]
    public void LinkToFragella_requires_a_json_object(string raw) =>
        NewFragrance().LinkToFragella("frg-123", raw, DateTime.UtcNow).Fails(FragranceErrors.FragellaRawInvalid);

    [Fact]
    public void LinkToFragella_requires_utc_timestamp() =>
        NewFragrance().LinkToFragella("frg-123", "{}", DateTime.Now).Fails(FragranceErrors.TimestampNotUtc);
}
