using PerfumeShop.Modules.Notifications.Domain;

namespace PerfumeShop.Modules.Notifications.Infrastructure;

/// <summary>Section "Notifications".</summary>
internal sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    /// <summary>Fallback only: {{shopName}} comes from the business profile (Admin → Settings).</summary>
    public string ShopName { get; set; } = "PerfumeShop";

    /// <summary>Fallback only: {{shopPhone}} comes from the business profile.</summary>
    public string ShopPhone { get; set; } = "+383 44 000 000";

    public string TimeZone { get; set; } = "Europe/Belgrade";

    /// <summary>No customer SMS from this local time…</summary>
    public string QuietHoursStart { get; set; } = "22:00";

    /// <summary>…until this local time (queued messages go out then). OTP codes are not affected.</summary>
    public string QuietHoursEnd { get; set; } = "08:00";

    public int DispatchBatchSize { get; set; } = 50;

    /// <summary>Development only (the console sender): also append every SMS to this file. Used by the e2e tests to read OTP codes.</summary>
    public string? DevelopmentSmsFile { get; set; }

    public QuietHours ToQuietHours() => new(
        TimeOnly.ParseExact(QuietHoursStart, "HH:mm", System.Globalization.CultureInfo.InvariantCulture),
        TimeOnly.ParseExact(QuietHoursEnd, "HH:mm", System.Globalization.CultureInfo.InvariantCulture),
        TimeZoneInfo.FindSystemTimeZoneById(TimeZone));

    public bool IsValid() =>
        TimeZoneInfo.TryFindSystemTimeZoneById(TimeZone, out _)
        && TimeOnly.TryParseExact(QuietHoursStart, "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _)
        && TimeOnly.TryParseExact(QuietHoursEnd, "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _)
        && DispatchBatchSize is >= 1 and <= 500
        && !string.IsNullOrWhiteSpace(ShopName);

    /// <summary>A copy with the shop name and phone from the business profile.</summary>
    public NotificationOptions WithShop(PerfumeShop.Modules.Administration.Contracts.BusinessProfileDto profile) => new()
    {
        ShopName = string.IsNullOrWhiteSpace(profile.ShopName) ? ShopName : profile.ShopName,
        ShopPhone = string.IsNullOrWhiteSpace(profile.Phone) ? ShopPhone : profile.Phone,
        TimeZone = TimeZone,
        QuietHoursStart = QuietHoursStart,
        QuietHoursEnd = QuietHoursEnd,
        DispatchBatchSize = DispatchBatchSize,
        DevelopmentSmsFile = DevelopmentSmsFile,
    };
}
