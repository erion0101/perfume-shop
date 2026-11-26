using PerfumeShop.Modules.Catalog.Domain.Common;
using PerfumeShop.Modules.Catalog.Domain.Fragrances;
using PerfumeShop.Shared.Kernel;
using static PerfumeShop.Modules.Catalog.Tests.TestHelpers;

namespace PerfumeShop.Modules.Catalog.Tests.Domain;

public sealed class FragranceVariantTests
{
    // ---------- creating variants ----------

    [Fact]
    public void New_variant_is_inactive_and_has_no_price()
    {
        var variant = NewFragrance().AddBottle();

        Assert.False(variant.IsActive);
        Assert.Null(variant.Price);
    }

    [Theory]
    [InlineData((int)VariantType.FullBottle)]
    [InlineData((int)VariantType.Tester)]
    [InlineData((int)VariantType.Mini)]
    public void Bottles_require_a_size(int type) =>
        NewFragrance().AddVariant(Sku.Create("LAT-1").Ok(), (VariantType)type, null, null).Fails(VariantErrors.SizeRequired);

    [Theory]
    [InlineData((int)VariantType.GiftSet)]
    [InlineData((int)VariantType.SampleSet)]
    public void Sets_do_not_require_a_size(int type) =>
        NewFragrance().AddVariant(Sku.Create("LAT-SET").Ok(), (VariantType)type, null, null).Ok();

    [Theory]
    [InlineData(0, (int)SizeUnit.Ml)]
    [InlineData(-5, (int)SizeUnit.Ml)]
    [InlineData(1000.1, (int)SizeUnit.Ml)]
    [InlineData(2.55, (int)SizeUnit.Ml)]
    [InlineData(0, (int)SizeUnit.Gram)]
    [InlineData(1500, (int)SizeUnit.Gram)]
    [InlineData(0, (int)SizeUnit.Piece)]
    public void Size_must_be_positive_and_realistic_for_every_unit(decimal value, int unit) =>
        VariantSize.Create(value, (SizeUnit)unit).Fails(VariantErrors.InvalidSize);

    [Fact]
    public void Size_unit_must_be_known() =>
        VariantSize.Create(10, (SizeUnit)99).Fails(VariantErrors.InvalidSizeUnit);

    [Fact]
    public void Bakhoor_can_be_sold_by_weight_and_by_piece()
    {
        var fragrance = NewFragrance("Bakhoor Al Arais");

        var byWeight = fragrance.AddVariant(Sku.Create("BAK-40G").Ok(), VariantType.FullBottle, VariantSize.Create(40, SizeUnit.Gram).Ok(), null).Ok();
        var byPiece = fragrance.AddVariant(Sku.Create("BAK-12PC").Ok(), VariantType.FullBottle, VariantSize.Create(12, SizeUnit.Piece).Ok(), null).Ok();

        Assert.Equal("40 g", byWeight.Size!.ToString());
        Assert.Equal("12 pcs", byPiece.Size!.ToString());
    }

    [Fact]
    public void Same_value_in_different_units_is_a_different_offer()
    {
        var fragrance = NewFragrance();
        fragrance.AddVariant(Sku.Create("X-40ML").Ok(), VariantType.FullBottle, Ml(40), null).Ok();

        fragrance.AddVariant(Sku.Create("X-40G").Ok(), VariantType.FullBottle, VariantSize.Create(40, SizeUnit.Gram).Ok(), null).Ok();
    }

    [Fact]
    public void Sku_must_be_unique_within_the_fragrance()
    {
        var fragrance = NewFragrance();
        fragrance.AddBottle(100, sku: "LAT-KHA");

        fragrance.AddVariant(Sku.Create("lat-kha").Ok(), VariantType.Tester, Ml(100), null).Fails(FragranceErrors.DuplicateSku);
    }

    [Fact]
    public void Ean_must_be_unique_within_the_fragrance()
    {
        var fragrance = NewFragrance();
        fragrance.AddBottle(100, ean: "4006381333931");

        fragrance.AddVariant(Sku.Create("LAT-KHA-T").Ok(), VariantType.Tester, Ml(100), Ean.Create("4006381333931").Ok())
            .Fails(FragranceErrors.DuplicateEan);
    }

    [Fact]
    public void Same_type_and_size_cannot_be_added_twice()
    {
        var fragrance = NewFragrance();
        fragrance.AddBottle(100, sku: "LAT-A");

        fragrance.AddVariant(Sku.Create("LAT-B").Ok(), VariantType.FullBottle, Ml(100), null).Fails(FragranceErrors.DuplicateVariant);
        fragrance.AddVariant(Sku.Create("LAT-C").Ok(), VariantType.Tester, Ml(100), null).Ok();
        fragrance.AddVariant(Sku.Create("LAT-D").Ok(), VariantType.FullBottle, Ml(30), null).Ok();
    }

    [Fact]
    public void Variant_codes_can_be_corrected_but_not_to_a_duplicate()
    {
        var fragrance = NewFragrance();
        var first = fragrance.AddBottle(100, sku: "LAT-A", ean: "4006381333931");
        var second = fragrance.AddBottle(50, sku: "LAT-B");

        fragrance.ChangeVariantCodes(second.Id, Sku.Create("LAT-A").Ok(), null).Fails(FragranceErrors.DuplicateSku);
        fragrance.ChangeVariantCodes(second.Id, Sku.Create("LAT-B").Ok(), Ean.Create("4006381333931").Ok()).Fails(FragranceErrors.DuplicateEan);
        fragrance.ChangeVariantCodes(first.Id, Sku.Create("LAT-A2").Ok(), Ean.Create("4006381333931").Ok()).Ok();

        Assert.Equal("LAT-A2", first.Sku.Value);
    }

    // ---------- price ----------

    [Fact]
    public void Price_is_set_in_eur_with_optional_compare_at_price()
    {
        var fragrance = NewFragrance();
        var variant = fragrance.AddBottle();

        fragrance.SetVariantPrice(variant.Id, Eur(39.90m), Eur(49.90m)).Ok();

        Assert.Equal(39.90m, variant.Price!.Amount);
        Assert.Equal(49.90m, variant.CompareAtPrice!.Amount);
    }

    [Fact]
    public void Price_must_be_greater_than_zero()
    {
        var fragrance = NewFragrance();
        var variant = fragrance.AddBottle();

        fragrance.SetVariantPrice(variant.Id, Eur(0m)).Fails(VariantErrors.PriceMustBePositive);
    }

    [Fact]
    public void Price_must_be_in_eur()
    {
        var fragrance = NewFragrance();
        var variant = fragrance.AddBottle();

        fragrance.SetVariantPrice(variant.Id, Money.Create(3900m, "ALL").Ok()).Fails(VariantErrors.PriceCurrency);
    }

    [Theory]
    [InlineData(39.90)]
    [InlineData(29.90)]
    public void Compare_at_price_must_be_higher_than_price(decimal compareAt)
    {
        var fragrance = NewFragrance();
        var variant = fragrance.AddBottle();

        fragrance.SetVariantPrice(variant.Id, Eur(39.90m), Eur(compareAt)).Fails(VariantErrors.CompareAtPriceTooLow);
        Assert.Null(variant.Price);
    }

    [Fact]
    public void Unknown_variant_is_reported() =>
        NewFragrance().SetVariantPrice(Guid.CreateVersion7(), Eur(10m)).Fails(FragranceErrors.VariantNotFound);

    // ---------- activation and publishing ----------

    [Fact]
    public void Variant_cannot_be_active_without_a_price()
    {
        var fragrance = NewFragrance();
        var variant = fragrance.AddBottle();

        fragrance.ActivateVariant(variant.Id).Fails(VariantErrors.ActivationRequiresPrice);
        Assert.False(variant.IsActive);
    }

    [Fact]
    public void Variant_with_price_can_be_activated()
    {
        var fragrance = NewFragrance();
        var variant = fragrance.AddBottle();
        fragrance.SetVariantPrice(variant.Id, Eur(39.90m)).Ok();

        fragrance.ActivateVariant(variant.Id).Ok();

        Assert.True(variant.IsActive);
    }

    [Fact]
    public void Fragrance_cannot_be_published_without_an_active_variant()
    {
        var fragrance = NewFragrance();
        fragrance.AddBottle();

        fragrance.Publish().Fails(FragranceErrors.PublishRequiresActiveVariant);
        Assert.Equal(FragranceStatus.Draft, fragrance.Status);
    }

    [Fact]
    public void Last_active_variant_of_a_published_fragrance_cannot_be_deactivated_or_removed()
    {
        var (fragrance, variant) = PublishedFragrance();

        fragrance.DeactivateVariant(variant.Id).Fails(FragranceErrors.LastActiveVariant);
        fragrance.RemoveVariant(variant.Id).Fails(FragranceErrors.LastActiveVariant);
        Assert.True(variant.IsActive);
    }

    [Fact]
    public void Variant_can_be_deactivated_after_unpublishing()
    {
        var (fragrance, variant) = PublishedFragrance();

        fragrance.Unpublish();
        fragrance.DeactivateVariant(variant.Id).Ok();

        Assert.False(variant.IsActive);
    }

    [Fact]
    public void Other_variants_can_be_deactivated_while_one_stays_active()
    {
        var (fragrance, _) = PublishedFragrance();
        var tester = fragrance.AddVariant(Sku.Create("LAT-KHA-T").Ok(), VariantType.Tester, Ml(100), null).Ok();
        fragrance.SetVariantPrice(tester.Id, Eur(29.90m)).Ok();
        fragrance.ActivateVariant(tester.Id).Ok();

        fragrance.DeactivateVariant(tester.Id).Ok();
        fragrance.RemoveVariant(tester.Id).Ok();

        Assert.Single(fragrance.Variants);
    }

    // ---------- images ----------

    [Fact]
    public void Images_are_ordered_by_sort_order()
    {
        var fragrance = NewFragrance();
        var variant = fragrance.AddBottle();

        fragrance.SetVariantImages(variant.Id,
        [
            ProductImage.Create("https://cdn.example.com/b.webp", null, 2).Ok(),
            ProductImage.Create("https://cdn.example.com/a.webp", "Khamrah 100 ml", 1).Ok(),
        ]).Ok();

        Assert.Equal("https://cdn.example.com/a.webp", variant.Images[0].Url);
    }

    [Fact]
    public void Images_cannot_repeat_the_same_url()
    {
        var fragrance = NewFragrance();
        var variant = fragrance.AddBottle();
        var image = ProductImage.Create("https://cdn.example.com/a.webp", null, 0).Ok();

        fragrance.SetVariantImages(variant.Id, [image, image]).Fails(VariantErrors.DuplicateImage);
    }

    [Fact]
    public void Image_url_must_be_absolute_http() =>
        ProductImage.Create("images/a.webp", null, 0).Fails(VariantErrors.ImageUrlInvalid);
}
