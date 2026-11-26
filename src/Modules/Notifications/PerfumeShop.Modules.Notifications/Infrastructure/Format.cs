using System.Globalization;

namespace PerfumeShop.Modules.Notifications.Infrastructure;

/// <summary>Culture-invariant values for templates (the server culture must never change "41.90").</summary>
internal static class Format
{
    public static string Amount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    public static string Size(decimal? value, string? unit) => value is { } v
        ? FormattableString.Invariant($" {v:0.#} {unit switch { "Ml" => "ml", "Gram" => "g", _ => "copë" }}")
        : string.Empty;

    public static string LocalDateTime(DateTime utc, string timeZone) =>
        TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById(timeZone)).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    public static DateOnly LocalDay(DateTime utc, string timeZone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById(timeZone)));

    public static string Day(DateOnly day) => day.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
}
