using System.Text.Json;
using PerfumeShop.Modules.Catalog.Domain.Fragrances;
using PerfumeShop.Shared.Infrastructure.Persistence;

namespace PerfumeShop.Modules.Catalog.Tests.Infrastructure;

/// <summary>Value objects have private constructors; make sure jsonb round-trips them.</summary>
public sealed class JsonbMappingTests
{
    private static T RoundTrip<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonbPropertyExtensions.SerializerOptions), JsonbPropertyExtensions.SerializerOptions)!;

    [Fact]
    public void Note_pyramid_round_trips()
    {
        var pyramid = NotePyramid.Create(
            [Note.Create("Cinnamon", "https://cdn.example.com/cinnamon.png").Ok()],
            [Note.Create("Dates").Ok()],
            [Note.Create("Vanilla").Ok()]).Ok();

        var copy = RoundTrip(pyramid);

        Assert.Equal("Cinnamon", copy.Top[0].Name);
        Assert.Equal("https://cdn.example.com/cinnamon.png", copy.Top[0].ImageUrl);
        Assert.Equal("Dates", copy.Heart[0].Name);
        Assert.Equal("Vanilla", copy.Base[0].Name);
    }

    [Fact]
    public void Note_pyramid_uses_camel_case_layer_names()
    {
        var json = JsonSerializer.Serialize(NotePyramid.Empty, JsonbPropertyExtensions.SerializerOptions);

        Assert.Equal("""{"top":[],"heart":[],"base":[]}""", json);
    }

    [Fact]
    public void Accords_references_and_images_round_trip()
    {
        IReadOnlyList<Accord> accords = [Accord.Create("amber", 100).Ok()];
        IReadOnlyList<FragranceReference> similar = [FragranceReference.Create("Kilian", "Angels' Share", Guid.CreateVersion7()).Ok()];
        IReadOnlyList<ProductImage> images = [ProductImage.Create("https://cdn.example.com/a.webp", "Khamrah", 1).Ok()];

        Assert.Equal(accords, RoundTrip(accords));
        Assert.Equal(similar, RoundTrip(similar));
        Assert.Equal(images, RoundTrip(images));
    }
}
