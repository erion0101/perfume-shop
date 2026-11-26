namespace PerfumeShop.Modules.Catalog.Domain.Fragrances;

internal enum Concentration
{
    EauDeParfum = 1,
    EauDeToilette = 2,
    ExtraitDeParfum = 3,
    Parfum = 4,
    EauDeCologne = 5,
    Attar = 6,
}

internal enum Gender
{
    Male = 1,
    Female = 2,
    Unisex = 3,
}

internal enum OlfactiveFamily
{
    Amber = 1,
    Woody = 2,
    Floral = 3,
    Fresh = 4,
    Citrus = 5,
    Aquatic = 6,
    Gourmand = 7,
    Fougere = 8,
    Chypre = 9,
    Leather = 10,
    Aromatic = 11,
    Spicy = 12,
}

internal enum Longevity
{
    VeryWeak = 1,
    Weak = 2,
    Moderate = 3,
    LongLasting = 4,
    Eternal = 5,
}

internal enum Sillage
{
    Intimate = 1,
    Moderate = 2,
    Strong = 3,
    Enormous = 4,
}

[Flags]
internal enum Season
{
    None = 0,
    Spring = 1,
    Summer = 2,
    Autumn = 4,
    Winter = 8,
    All = Spring | Summer | Autumn | Winter,
}

[Flags]
internal enum Occasion
{
    None = 0,
    Day = 1,
    Night = 2,
    Office = 4,
    Casual = 8,
    Evening = 16,
    SpecialOccasion = 32,
    All = Day | Night | Office | Casual | Evening | SpecialOccasion,
}

internal enum FragranceStatus
{
    Draft = 1,
    Published = 2,
}

internal enum VariantType
{
    FullBottle = 1,
    Tester = 2,
    Mini = 3,
    GiftSet = 4,
    SampleSet = 5,
}

internal static class CatalogEnumExtensions
{
    public static string Abbreviation(this Concentration concentration) => concentration switch
    {
        Concentration.EauDeParfum => "edp",
        Concentration.EauDeToilette => "edt",
        Concentration.ExtraitDeParfum => "extrait",
        Concentration.Parfum => "parfum",
        Concentration.EauDeCologne => "edc",
        Concentration.Attar => "attar",
        _ => throw new ArgumentOutOfRangeException(nameof(concentration), concentration, null),
    };

    /// <summary>Sets are sold as a box; a single size in ml does not apply.</summary>
    public static bool RequiresSize(this VariantType type) => type is VariantType.FullBottle or VariantType.Tester or VariantType.Mini;
}
