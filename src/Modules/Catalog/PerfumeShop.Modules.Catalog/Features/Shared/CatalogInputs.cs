using PerfumeShop.Modules.Catalog.Domain.Fragrances;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Catalog.Features.Shared;

internal sealed record NoteInput(string Name, string? ImageUrl);

internal sealed record NotePyramidInput(List<NoteInput>? Top, List<NoteInput>? Heart, List<NoteInput>? Base);

internal sealed record AccordInput(string Name, int Percentage);

internal sealed record SimilarFragranceInput(string BrandName, string Name, Guid? FragranceId);

internal sealed record ImageInput(string Url, string? AltText, int SortOrder);

internal sealed record SizeInput(decimal Value, SizeUnit Unit);

/// <summary>Turns request shapes into validated domain values (first failure wins).</summary>
internal static class CatalogInputs
{
    public static Result<NotePyramid> ToNotePyramid(NotePyramidInput? input)
    {
        if (input is null)
        {
            return NotePyramid.Empty;
        }

        var top = Collect(input.Top, n => Note.Create(n.Name, n.ImageUrl));
        var heart = Collect(input.Heart, n => Note.Create(n.Name, n.ImageUrl));
        var @base = Collect(input.Base, n => Note.Create(n.Name, n.ImageUrl));

        foreach (var layer in new[] { top, heart, @base })
        {
            if (layer.IsFailure)
            {
                return layer.Error;
            }
        }

        return NotePyramid.Create(top.Value, heart.Value, @base.Value);
    }

    public static Result<IReadOnlyList<Accord>> ToAccords(IEnumerable<AccordInput>? input) =>
        Collect(input, a => Accord.Create(a.Name, a.Percentage));

    public static Result<IReadOnlyList<FragranceReference>> ToReferences(IEnumerable<SimilarFragranceInput>? input) =>
        Collect(input, r => FragranceReference.Create(r.BrandName, r.Name, r.FragranceId));

    public static Result<IReadOnlyList<ProductImage>> ToImages(IEnumerable<ImageInput>? input) =>
        Collect(input, i => ProductImage.Create(i.Url, i.AltText, i.SortOrder));

    public static Result<VariantSize?> ToSize(SizeInput? input)
    {
        if (input is null)
        {
            return Result<VariantSize?>.Success(null);
        }

        var size = VariantSize.Create(input.Value, input.Unit);
        return size.IsSuccess ? Result<VariantSize?>.Success(size.Value) : size.Error;
    }

    public static Result<Money?> ToEuro(decimal? amount)
    {
        if (amount is null)
        {
            return Result<Money?>.Success(null);
        }

        var money = Money.Euro(amount.Value);
        return money.IsSuccess ? Result<Money?>.Success(money.Value) : money.Error;
    }

    public static Result<Ean?> ToEan(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Result<Ean?>.Success(null);
        }

        var ean = Ean.Create(input);
        return ean.IsSuccess ? Result<Ean?>.Success(ean.Value) : ean.Error;
    }

    private static Result<IReadOnlyList<TOut>> Collect<TIn, TOut>(IEnumerable<TIn>? input, Func<TIn, Result<TOut>> convert)
    {
        List<TOut> values = [];
        foreach (var item in input ?? [])
        {
            var result = convert(item);
            if (result.IsFailure)
            {
                return result.Error;
            }

            values.Add(result.Value);
        }

        return values;
    }
}
