using PerfumeShop.Modules.Catalog.Domain.Common;
using PerfumeShop.Modules.Catalog.Domain.Fragrances;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Catalog.Tests;

internal static class TestHelpers
{
    /// <summary>Unwraps a result that the test expects to succeed.</summary>
    public static T Ok<T>(this Result<T> result)
    {
        Assert.True(result.IsSuccess, $"Expected success but got {result.Error.Code}: {result.Error.Message}");
        return result.Value;
    }

    public static void Ok(this Result result) =>
        Assert.True(result.IsSuccess, $"Expected success but got {result.Error.Code}: {result.Error.Message}");

    public static void Fails(this Result result, Error expected)
    {
        Assert.True(result.IsFailure, $"Expected {expected.Code} but the operation succeeded.");
        Assert.Equal(expected, result.Error);
    }

    public static Money Eur(decimal amount) => Money.Euro(amount).Ok();

    public static VariantSize Ml(decimal value) => VariantSize.Ml(value).Ok();

    public static Fragrance NewFragrance(string name = "Khamrah") =>
        Fragrance.Create(Guid.CreateVersion7(), name, Slug.Create($"lattafa {name} edp").Ok(), Concentration.EauDeParfum, Gender.Unisex).Ok();

    public static FragranceVariant AddBottle(this Fragrance fragrance, decimal sizeMl = 100, string? sku = null, string? ean = null) =>
        fragrance.AddVariant(
            Sku.Create(sku ?? $"LAT-KHA-{sizeMl}").Ok(),
            VariantType.FullBottle,
            Ml(sizeMl),
            ean is null ? null : Ean.Create(ean).Ok()).Ok();

    /// <summary>Fragrance with one priced, active variant, published.</summary>
    public static (Fragrance Fragrance, FragranceVariant Variant) PublishedFragrance()
    {
        var fragrance = NewFragrance();
        var variant = fragrance.AddBottle();
        fragrance.SetVariantPrice(variant.Id, Eur(39.90m)).Ok();
        fragrance.ActivateVariant(variant.Id).Ok();
        fragrance.Publish().Ok();
        return (fragrance, variant);
    }
}
