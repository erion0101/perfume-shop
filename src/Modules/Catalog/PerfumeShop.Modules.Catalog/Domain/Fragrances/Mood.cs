namespace PerfumeShop.Modules.Catalog.Domain.Fragrances;

/// <summary>
/// The storefront's "Çfarë të tërheq më shumë?" direction. Derived from the olfactive family and main accords
/// (<see cref="MoodClassifier"/>); an admin may override it per fragrance.
/// </summary>
internal enum Mood
{
    /// <summary>Oud dhe dru.</summary>
    Oud = 1,

    /// <summary>Ëmbël dhe vanilje.</summary>
    Sweet = 2,

    /// <summary>Freski.</summary>
    Fresh = 3,

    /// <summary>Erëza dhe duhan.</summary>
    Spicy = 4,

    /// <summary>Lule.</summary>
    Floral = 5,
}

/// <summary>
/// Scores each mood from the accords (weighted by their percentage) plus a bonus for the olfactive family,
/// and picks the highest. Deterministic: ties go to the lower enum value.
/// </summary>
internal static class MoodClassifier
{
    /// <summary>Accord keyword → mood. Accord names are Fragella's lower-case English names ("warm spicy", "oud").</summary>
    private static readonly (string Keyword, Mood Mood)[] Keywords =
    [
        ("oud", Mood.Oud), ("woody", Mood.Oud), ("leather", Mood.Oud), ("smoky", Mood.Oud), ("agarwood", Mood.Oud),
        ("earthy", Mood.Oud), ("animalic", Mood.Oud), ("incense", Mood.Oud), ("balsamic", Mood.Oud),
        ("sweet", Mood.Sweet), ("vanilla", Mood.Sweet), ("caramel", Mood.Sweet), ("honey", Mood.Sweet),
        ("gourmand", Mood.Sweet), ("chocolate", Mood.Sweet), ("coffee", Mood.Sweet), ("fruity", Mood.Sweet),
        ("powdery", Mood.Sweet), ("lactonic", Mood.Sweet), ("almond", Mood.Sweet), ("amber", Mood.Sweet),
        ("fresh", Mood.Fresh), ("citrus", Mood.Fresh), ("aquatic", Mood.Fresh), ("marine", Mood.Fresh),
        ("ozonic", Mood.Fresh), ("green", Mood.Fresh), ("aromatic", Mood.Fresh), ("herbal", Mood.Fresh),
        ("lavender", Mood.Fresh), ("mint", Mood.Fresh), ("soapy", Mood.Fresh),
        ("spicy", Mood.Spicy), ("tobacco", Mood.Spicy), ("cinnamon", Mood.Spicy),
        ("warm", Mood.Spicy), ("pepper", Mood.Spicy), ("cardamom", Mood.Spicy), ("saffron", Mood.Spicy),
        ("floral", Mood.Floral), ("rose", Mood.Floral), ("white floral", Mood.Floral), ("jasmine", Mood.Floral),
        ("tuberose", Mood.Floral), ("iris", Mood.Floral), ("violet", Mood.Floral), ("yellow floral", Mood.Floral),
    ];

    private const int FamilyBonus = 60;

    public static Mood? Classify(OlfactiveFamily? family, IReadOnlyList<Accord> accords)
    {
        var scores = new Dictionary<Mood, int>();
        foreach (var accord in accords)
        {
            // An accord counts once, for its most specific keyword ("white floral" before "floral", "fresh spicy" → Fresh).
            var match = Keywords.Where(k => accord.Name.Contains(k.Keyword, StringComparison.Ordinal))
                .OrderByDescending(k => k.Keyword.Length)
                .ThenBy(k => accord.Name.IndexOf(k.Keyword, StringComparison.Ordinal))
                .Select(k => (Mood?)k.Mood)
                .FirstOrDefault();
            if (match is { } mood)
            {
                scores[mood] = scores.GetValueOrDefault(mood) + accord.Percentage;
            }
        }

        if (FamilyMood(family) is { } familyMood)
        {
            scores[familyMood] = scores.GetValueOrDefault(familyMood) + FamilyBonus;
        }

        return scores.Count == 0
            ? null
            : scores.OrderByDescending(s => s.Value).ThenBy(s => s.Key).First().Key;
    }

    private static Mood? FamilyMood(OlfactiveFamily? family) => family switch
    {
        OlfactiveFamily.Woody or OlfactiveFamily.Leather => Mood.Oud,
        OlfactiveFamily.Gourmand => Mood.Sweet,
        OlfactiveFamily.Fresh or OlfactiveFamily.Citrus or OlfactiveFamily.Aquatic or OlfactiveFamily.Aromatic or OlfactiveFamily.Fougere => Mood.Fresh,
        OlfactiveFamily.Spicy => Mood.Spicy,

        // Amber (oriental) is sweet-resinous or spicy depending on the fragrance: the accords decide.
        OlfactiveFamily.Floral or OlfactiveFamily.Chypre => Mood.Floral,
        _ => null,
    };

}
