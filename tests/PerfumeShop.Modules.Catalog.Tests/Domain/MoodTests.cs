using PerfumeShop.Modules.Catalog.Domain.Fragrances;

namespace PerfumeShop.Modules.Catalog.Tests.Domain;

public sealed class MoodTests
{
    private static Accord[] Accords(params (string Name, int Percentage)[] accords) =>
        [.. accords.Select(a => Accord.Create(a.Name, a.Percentage).Value)];

    [Fact]
    public void Khamrah_is_sweet_even_with_warm_spices()
    {
        var accords = Accords(("sweet", 100), ("warm spicy", 95), ("amber", 80), ("vanilla", 70), ("cinnamon", 60));

        Assert.Equal(Mood.Sweet, MoodClassifier.Classify(OlfactiveFamily.Amber, accords));
    }

    [Fact]
    public void Asad_is_spice_and_tobacco()
    {
        var accords = Accords(("warm spicy", 100), ("tobacco", 85), ("vanilla", 70), ("amber", 55));

        Assert.Equal(Mood.Spicy, MoodClassifier.Classify(OlfactiveFamily.Amber, accords));
    }

    [Theory]
    [InlineData("citrus", "woody", "fresh", "Fresh")]
    [InlineData("oud", "rose", "woody", "Oud")]
    [InlineData("white floral", "fresh", "powdery", "Floral")]
    public void Strongest_accords_decide(string first, string second, string third, string expected)
    {
        var accords = Accords((first, 100), (second, 80), (third, 60));

        Assert.Equal(Enum.Parse<Mood>(expected), MoodClassifier.Classify(null, accords));
    }

    [Fact]
    public void Family_alone_gives_a_mood_and_nothing_gives_none()
    {
        Assert.Equal(Mood.Oud, MoodClassifier.Classify(OlfactiveFamily.Leather, []));
        Assert.Null(MoodClassifier.Classify(null, []));
        Assert.Null(MoodClassifier.Classify(null, Accords(("metallic", 100))));
    }

    [Fact]
    public void Admin_override_wins_and_clearing_it_derives_again()
    {
        var fragrance = Fragrance.Create(Guid.NewGuid(), "Khamrah", Slug().Value, Concentration.EauDeParfum, Gender.Unisex).Value;
        Assert.True(fragrance.SetAccords(Accords(("sweet", 100), ("vanilla", 80))).IsSuccess);
        Assert.Equal(Mood.Sweet, fragrance.Mood);

        Assert.True(fragrance.SetMoodOverride(Mood.Oud).IsSuccess);
        Assert.Equal(Mood.Oud, fragrance.Mood);
        Assert.True(fragrance.SetAccords(Accords(("citrus", 100))).IsSuccess);
        Assert.Equal(Mood.Oud, fragrance.Mood); // still the override

        Assert.True(fragrance.SetMoodOverride(null).IsSuccess);
        Assert.Equal(Mood.Fresh, fragrance.Mood);
        Assert.Equal("Catalog.Fragrance.InvalidMood", fragrance.SetMoodOverride((Mood)99).Error.Code);
    }

    [Fact]
    public void Note_keys_are_the_lower_case_names_of_every_layer()
    {
        var fragrance = Fragrance.Create(Guid.NewGuid(), "Khamrah", Slug().Value, Concentration.EauDeParfum, Gender.Unisex).Value;
        var notes = NotePyramid.Create([Note.Create("Cinnamon").Value], [Note.Create("Dates").Value], [Note.Create("Vanilla").Value, Note.Create("cinnamon").Value]).Value;

        fragrance.SetNotes(notes);

        Assert.Equal(["cinnamon", "dates", "vanilla"], fragrance.NoteKeys);
    }

    private static PerfumeShop.Shared.Kernel.Result<PerfumeShop.Modules.Catalog.Domain.Common.Slug> Slug() =>
        PerfumeShop.Modules.Catalog.Domain.Common.Slug.Create("lattafa-khamrah-edp");
}
