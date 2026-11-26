using System.Globalization;
using System.Text.Json;
using PerfumeShop.Modules.Catalog.Domain.Fragrances;

namespace PerfumeShop.Modules.Catalog.Infrastructure.Fragella;

/// <summary>What we take from a Fragella fragrance. Photos are NOT taken (they come from the distributor).</summary>
internal sealed record FragellaFragranceData(
    string FragellaId,
    string Name,
    string Brand,
    string? CountryCode,
    int? Year,
    IReadOnlyList<string> Perfumers,
    Gender? Gender,
    Concentration? Concentration,
    Longevity? Longevity,
    Sillage? Sillage,
    NotePyramid Notes,
    IReadOnlyList<Accord> Accords,
    Season Seasons,
    Occasion Occasions);

/// <summary>
/// Maps one Fragella fragrance object (as stored in catalog.fragella_fragrances.raw) to Catalog values.
/// Unknown or invalid values become null/empty instead of failing, so the admin can fix them afterwards.
/// </summary>
internal static class FragellaMapper
{
    /// <summary>A ranked season/occasion is kept when its score is at least this share of the top score.</summary>
    public const double RankingThreshold = 0.7;

    private static readonly Dictionary<string, int> AccordStrength = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Dominant"] = 100,
        ["Prominent"] = 75,
        ["Moderate"] = 50,
        ["Subtle"] = 25,
    };

    private static readonly Dictionary<string, string> CountryCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["United Arab Emirates"] = "AE", ["UAE"] = "AE", ["Saudi Arabia"] = "SA", ["Kuwait"] = "KW",
        ["Oman"] = "OM", ["Qatar"] = "QA", ["Bahrain"] = "BH", ["Jordan"] = "JO", ["Lebanon"] = "LB",
        ["Egypt"] = "EG", ["Turkey"] = "TR", ["India"] = "IN", ["Pakistan"] = "PK", ["France"] = "FR",
        ["Italy"] = "IT", ["Spain"] = "ES", ["Germany"] = "DE", ["Switzerland"] = "CH",
        ["United Kingdom"] = "GB", ["UK"] = "GB", ["USA"] = "US", ["United States"] = "US",
    };

    public static FragellaFragranceData? Map(string rawJson)
    {
        using var document = JsonDocument.Parse(rawJson);
        var root = document.RootElement;

        var id = FragellaJson.String(root, "_id");
        var name = FragellaJson.String(root, "Name");
        if (id is null || name is null)
        {
            return null;
        }

        return new FragellaFragranceData(
            FragellaId: id,
            Name: name,
            Brand: FragellaJson.String(root, "Brand") ?? string.Empty,
            CountryCode: MapCountry(FragellaJson.String(root, "Country")),
            Year: MapYear(FragellaJson.String(root, "Year")),
            Perfumers: MapPerfumers(FragellaJson.String(root, "perfumers")),
            Gender: MapGender(FragellaJson.String(root, "Gender")),
            Concentration: MapConcentration(FragellaJson.String(root, "OilType")),
            Longevity: MapLongevity(FragellaJson.String(root, "Longevity")),
            Sillage: MapSillage(FragellaJson.String(root, "Sillage")),
            Notes: MapNotes(root),
            Accords: MapAccords(root),
            Seasons: MapRanking(root, "Season Ranking", MapSeason),
            Occasions: MapRanking(root, "Occasion Ranking", MapOccasion));
    }

    public static Concentration? MapConcentration(string? oilType) => oilType?.ToLowerInvariant() switch
    {
        "eau de parfum" or "edp" => Concentration.EauDeParfum,
        "eau de toilette" or "edt" => Concentration.EauDeToilette,
        "extrait de parfum" or "extrait" => Concentration.ExtraitDeParfum,
        "parfum" or "pure parfum" or "perfume" => Concentration.Parfum,
        "eau de cologne" or "cologne" or "edc" => Concentration.EauDeCologne,
        "attar" or "perfume oil" or "concentrated perfume oil" or "oil" => Concentration.Attar,
        _ => null,
    };

    public static Gender? MapGender(string? gender) => gender?.ToLowerInvariant() switch
    {
        "men" or "male" or "for men" => Domain.Fragrances.Gender.Male,
        "women" or "female" or "for women" => Domain.Fragrances.Gender.Female,
        "unisex" => Domain.Fragrances.Gender.Unisex,
        _ => null,
    };

    public static Longevity? MapLongevity(string? value) => value?.ToLowerInvariant() switch
    {
        "poor" or "very weak" => Domain.Fragrances.Longevity.VeryWeak,
        "weak" => Domain.Fragrances.Longevity.Weak,
        "moderate" => Domain.Fragrances.Longevity.Moderate,
        "long lasting" => Domain.Fragrances.Longevity.LongLasting,
        "very long lasting" or "eternal" => Domain.Fragrances.Longevity.Eternal,
        _ => null,
    };

    public static Sillage? MapSillage(string? value) => value?.ToLowerInvariant() switch
    {
        "intimate" or "soft" => Domain.Fragrances.Sillage.Intimate,
        "moderate" => Domain.Fragrances.Sillage.Moderate,
        "strong" => Domain.Fragrances.Sillage.Strong,
        "enormous" => Domain.Fragrances.Sillage.Enormous,
        _ => null,
    };

    private static Season MapSeason(string name) => name.ToLowerInvariant() switch
    {
        "spring" => Season.Spring,
        "summer" => Season.Summer,
        "fall" or "autumn" => Season.Autumn,
        "winter" => Season.Winter,
        _ => Season.None,
    };

    private static Occasion MapOccasion(string name) => name.ToLowerInvariant() switch
    {
        "casual" => Occasion.Casual,
        "night out" or "night" => Occasion.Night,
        "professional" or "office" => Occasion.Office,
        "day" => Occasion.Day,
        "evening" => Occasion.Evening,
        _ => Occasion.None,
    };

    private static string? MapCountry(string? country) =>
        country is not null && CountryCodes.TryGetValue(country, out var code) ? code : null;

    private static int? MapYear(string? year) =>
        int.TryParse(year, NumberStyles.None, CultureInfo.InvariantCulture, out var y)
        && y >= Fragrance.MinLaunchYear && y <= DateTime.UtcNow.Year + 1
            ? y
            : null;

    private static IReadOnlyList<string> MapPerfumers(string? perfumers) =>
        perfumers is null
            ? []
            : [.. perfumers.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(p => p.Length <= Fragrance.PerfumerMaxLength)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(Fragrance.MaxPerfumers)];

    private static NotePyramid MapNotes(JsonElement root)
    {
        root.TryGetProperty("Notes", out var notes);
        var top = MapNoteLayer(notes, "Top");
        var heart = MapNoteLayer(notes, "Middle");
        var @base = MapNoteLayer(notes, "Base");

        // Many Arabian fragrances only have a flat note list. Show it as the heart so the product
        // page is not empty; the admin can move notes later.
        if (top.Count == 0 && heart.Count == 0 && @base.Count == 0)
        {
            heart = Distinct(FragellaJson.Array(root, "General Notes")
                .Where(n => n.ValueKind == JsonValueKind.String)
                .Select(n => Note.Create(ToTitleCase(n.GetString())))
                .Where(r => r.IsSuccess)
                .Select(r => r.Value));
        }

        return NotePyramid.Create(top, heart, @base).Value;
    }

    private static List<Note> MapNoteLayer(JsonElement notes, string layer) =>
        Distinct(FragellaJson.Array(notes, layer)
            .Select(n =>
            {
                var noteName = FragellaJson.String(n, "name");
                var image = FragellaJson.String(n, "imageUrl");
                // An invalid image URL must not drop the note itself.
                var note = Note.Create(noteName, image);
                return note.IsSuccess ? note : Note.Create(noteName);
            })
            .Where(r => r.IsSuccess)
            .Select(r => r.Value));

    private static List<Note> Distinct(IEnumerable<Note> notes) =>
        [.. notes.DistinctBy(n => n.Name, StringComparer.OrdinalIgnoreCase).Take(NotePyramid.MaxNotesPerLayer)];

    private static IReadOnlyList<Accord> MapAccords(JsonElement root)
    {
        root.TryGetProperty("Main Accords Percentage", out var strengths);

        return [.. FragellaJson.Array(root, "Main Accords")
            .Where(a => a.ValueKind == JsonValueKind.String)
            .Select(a => a.GetString()!)
            .Select(name =>
            {
                var level = FragellaJson.String(strengths, name);
                var percentage = level is not null && AccordStrength.TryGetValue(level, out var p) ? p : 25;
                return Accord.Create(name, percentage);
            })
            .Where(r => r.IsSuccess)
            .Select(r => r.Value)
            .DistinctBy(a => a.Name)
            .OrderByDescending(a => a.Percentage)
            .Take(Fragrance.MaxAccords)];
    }

    private static TFlags MapRanking<TFlags>(JsonElement root, string property, Func<string, TFlags> map)
        where TFlags : struct, Enum
    {
        var ranked = FragellaJson.Array(root, property)
            .Select(r => (Name: FragellaJson.String(r, "name"), Score: FragellaJson.Number(r, "score") ?? 0))
            .Where(r => r.Name is not null && r.Score > 0)
            .ToList();

        if (ranked.Count == 0)
        {
            return default;
        }

        var cutOff = ranked.Max(r => r.Score) * RankingThreshold;
        var flags = ranked
            .Where(r => r.Score >= cutOff)
            .Aggregate(0, (acc, r) => acc | Convert.ToInt32(map(r.Name!), CultureInfo.InvariantCulture));

        return (TFlags)Enum.ToObject(typeof(TFlags), flags);
    }

    private static string? ToTitleCase(string? value) =>
        value is null ? null : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.Trim());
}
