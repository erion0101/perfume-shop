using System.Text.Json;
using PerfumeShop.Modules.Catalog.Domain.Fragrances;
using PerfumeShop.Modules.Catalog.Infrastructure.Fragella;

namespace PerfumeShop.Modules.Catalog.Tests.Fragella;

/// <summary>
/// Maps the saved sample (Fragella/Samples/fragella-search-khamrah.json). The sample follows the
/// response format documented at api.fragella.com/docs.html; it is hand-written, not a recorded response.
/// </summary>
public sealed class FragellaMapperTests
{
    private static string Item(int index)
    {
        using var document = JsonDocument.Parse(FakeFragellaClient.Sample());
        return document.RootElement[index].GetRawText();
    }

    [Fact]
    public void Maps_identity_and_classification()
    {
        var data = FragellaMapper.Map(Item(0))!;

        Assert.Equal("Lattafa-Khamrah", data.FragellaId);
        Assert.Equal("Khamrah", data.Name);
        Assert.Equal("Lattafa", data.Brand);
        Assert.Equal("AE", data.CountryCode);
        Assert.Equal(2022, data.Year);
        Assert.Equal(Gender.Unisex, data.Gender);
        Assert.Equal(Concentration.EauDeParfum, data.Concentration);
        Assert.Equal(Longevity.LongLasting, data.Longevity);
        Assert.Equal(Sillage.Strong, data.Sillage);
        Assert.Empty(data.Perfumers);
    }

    [Fact]
    public void Maps_note_pyramid_with_middle_as_heart()
    {
        var notes = FragellaMapper.Map(Item(0))!.Notes;

        Assert.Equal(["Cinnamon", "Nutmeg", "Bergamot"], notes.Top.Select(n => n.Name));
        Assert.Equal(["Dates", "Praline", "Tuberose"], notes.Heart.Select(n => n.Name));
        Assert.Equal(["Vanilla", "Tonka Bean", "Benzoin", "Myrrh"], notes.Base.Select(n => n.Name));
        Assert.Equal("https://cdn.fragella.com/notes/cinnamon.jpg", notes.Top[0].ImageUrl);
    }

    [Fact]
    public void Invalid_note_image_url_keeps_the_note_without_image()
    {
        var tuberose = FragellaMapper.Map(Item(0))!.Notes.Heart.Single(n => n.Name == "Tuberose");

        Assert.Null(tuberose.ImageUrl);
    }

    [Fact]
    public void Maps_accord_levels_to_percentages_strongest_first()
    {
        var accords = FragellaMapper.Map(Item(0))!.Accords;

        Assert.Equal(
            [("sweet", 100), ("warm spicy", 100), ("amber", 75), ("vanilla", 75), ("cinnamon", 50)],
            accords.Select(a => (a.Name, a.Percentage)));
    }

    [Fact]
    public void Keeps_seasons_and_occasions_close_to_the_top_score()
    {
        var data = FragellaMapper.Map(Item(0))!;

        Assert.Equal(Season.Autumn | Season.Winter, data.Seasons);
        Assert.Equal(Occasion.Night | Occasion.Casual, data.Occasions);
    }

    [Fact]
    public void Sparse_item_maps_to_nulls_and_flat_notes_become_the_heart()
    {
        var data = FragellaMapper.Map(Item(1))!;

        Assert.Null(data.Concentration);
        Assert.Null(data.Year);
        Assert.Null(data.CountryCode);
        Assert.Equal(Gender.Male, data.Gender);
        Assert.Equal(Longevity.Eternal, data.Longevity);
        Assert.Equal(Sillage.Intimate, data.Sillage);
        Assert.Equal(["Perfumer One", "Perfumer Two"], data.Perfumers);
        Assert.Empty(data.Notes.Top);
        Assert.Equal(["Coffee", "Cardamom", "Praline", "Vanilla"], data.Notes.Heart.Select(n => n.Name));
        Assert.Equal(Season.None, data.Seasons);
    }

    [Fact]
    public void Accord_without_level_gets_the_lowest_strength()
    {
        var accords = FragellaMapper.Map(Item(1))!.Accords;

        Assert.Equal([("coffee", 100), ("sweet", 25), ("warm spicy", 25)], accords.Select(a => (a.Name, a.Percentage)));
    }

    [Fact]
    public void Item_without_id_is_not_mapped() =>
        Assert.Null(FragellaMapper.Map(Item(2)));

    [Theory]
    [InlineData("Eau de Parfum", (int)Concentration.EauDeParfum)]
    [InlineData("eau de toilette", (int)Concentration.EauDeToilette)]
    [InlineData("Extrait de Parfum", (int)Concentration.ExtraitDeParfum)]
    [InlineData("Parfum", (int)Concentration.Parfum)]
    [InlineData("Eau de Cologne", (int)Concentration.EauDeCologne)]
    [InlineData("Perfume Oil", (int)Concentration.Attar)]
    [InlineData("Attar", (int)Concentration.Attar)]
    public void Maps_oil_types(string oilType, int expected) =>
        Assert.Equal((Concentration)expected, FragellaMapper.MapConcentration(oilType));

    [Theory]
    [InlineData("")]
    [InlineData("Body Mist")]
    [InlineData(null)]
    public void Unknown_oil_type_is_null(string? oilType) =>
        Assert.Null(FragellaMapper.MapConcentration(oilType));
}
