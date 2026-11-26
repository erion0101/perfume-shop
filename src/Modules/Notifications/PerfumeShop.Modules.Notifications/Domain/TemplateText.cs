using System.Text.RegularExpressions;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Notifications.Domain;

/// <summary>Placeholders are written <c>{{name}}</c>. Only the names a template declares are allowed.</summary>
internal static partial class TemplateText
{
    public static IReadOnlyList<string> UsedPlaceholders(string text) =>
        [.. Placeholder().Matches(text).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal)];

    public static Result Validate(string text, IReadOnlyCollection<string> allowed)
    {
        var unknown = UsedPlaceholders(text).Where(p => !allowed.Contains(p)).ToList();
        if (unknown.Count > 0)
        {
            return Error.Validation(
                "Notifications.Template.UnknownPlaceholder",
                $"Unknown placeholder(s): {string.Join(", ", unknown.Select(u => "{{" + u + "}}"))}. Allowed: {string.Join(", ", allowed.Select(a => "{{" + a + "}}"))}.");
        }

        var rest = Placeholder().Replace(text, string.Empty);
        if (rest.Contains("{{", StringComparison.Ordinal) || rest.Contains("}}", StringComparison.Ordinal))
        {
            return Error.Validation("Notifications.Template.MalformedPlaceholder", "Placeholders look like {{orderNumber}}: letters and digits between double braces.");
        }

        return Result.Success();
    }

    /// <summary>Replaces every placeholder; a value that is not supplied becomes empty.</summary>
    public static string Render(string text, IReadOnlyDictionary<string, string> values) =>
        Placeholder().Replace(text, m => values.TryGetValue(m.Groups[1].Value, out var value) ? value : string.Empty);

    [GeneratedRegex(@"\{\{\s*([A-Za-z][A-Za-z0-9]*)\s*\}\}")]
    private static partial Regex Placeholder();
}
