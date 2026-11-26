using PerfumeShop.Modules.Catalog.Domain.Common;
using PerfumeShop.Modules.Catalog.Domain.Fragrances;

namespace PerfumeShop.Modules.Catalog.Tests.Domain;

public sealed class ScentProfileTests
{
    private static Note N(string name) => Note.Create(name).Ok();

    [Fact]
    public void Note_pyramid_keeps_layers()
    {
        var pyramid = NotePyramid.Create([N("Cinnamon"), N("Cardamom")], [N("Dates"), N("Praline")], [N("Vanilla"), N("Benzoin")]).Ok();

        Assert.Equal(["Cinnamon", "Cardamom"], pyramid.Top.Select(n => n.Name));
        Assert.Equal(["Dates", "Praline"], pyramid.Heart.Select(n => n.Name));
        Assert.Equal(["Vanilla", "Benzoin"], pyramid.Base.Select(n => n.Name));
        Assert.False(pyramid.IsEmpty);
        Assert.True(NotePyramid.Empty.IsEmpty);
    }

    [Fact]
    public void Note_pyramid_rejects_duplicate_note_in_a_layer() =>
        NotePyramid.Create([N("Oud"), N("oud")], [], []).Fails(ScentErrors.DuplicateNote);

    [Fact]
    public void Same_note_may_appear_in_different_layers() =>
        NotePyramid.Create([N("Saffron")], [N("Saffron")], []).Ok();

    [Fact]
    public void Note_pyramid_rejects_too_many_notes() =>
        NotePyramid.Create(Enumerable.Range(1, NotePyramid.MaxNotesPerLayer + 1).Select(i => N($"Note {i}")), [], [])
            .Fails(ScentErrors.TooManyNotes);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Note_requires_a_name(string name) =>
        Note.Create(name).Fails(ScentErrors.NoteNameInvalid);

    [Fact]
    public void Note_rejects_non_http_image_url() =>
        Note.Create("Rose", "ftp://example.com/rose.png").Fails(ScentErrors.NoteImageUrlInvalid);

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Accord_percentage_must_be_between_1_and_100(int percentage) =>
        Accord.Create("amber", percentage).Fails(ScentErrors.AccordPercentageInvalid);

    [Fact]
    public void Accord_name_is_normalized_to_lowercase() =>
        Assert.Equal("warm spicy", Accord.Create("  Warm Spicy ", 74).Ok().Name);

    [Fact]
    public void Fragrance_reference_requires_brand_and_name()
    {
        FragranceReference.Create("", "Angels' Share").Fails(ScentErrors.ReferenceInvalid);
        FragranceReference.Create("Kilian", " ").Fails(ScentErrors.ReferenceInvalid);
        FragranceReference.Create("Kilian", "Angels' Share", Guid.Empty).Fails(ScentErrors.ReferenceInvalid);
    }
}
