namespace PerfumeShop.Modules.Notifications.Domain;

/// <summary>No customer SMS between <see cref="Start"/> and <see cref="End"/> local time (default 22:00-08:00 Kosovo time).</summary>
internal sealed record QuietHours(TimeOnly Start, TimeOnly End, TimeZoneInfo Zone)
{
    public bool IsQuiet(DateTime utc)
    {
        var local = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, Zone));
        return Start <= End
            ? local >= Start && local < End
            : local >= Start || local < End;
    }

    /// <summary><paramref name="utc"/> itself outside quiet hours, otherwise the next <see cref="End"/> (e.g. 08:00).</summary>
    public DateTime NextAllowedUtc(DateTime utc)
    {
        if (!IsQuiet(utc))
        {
            return utc;
        }

        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, Zone);
        var day = DateOnly.FromDateTime(local);
        if (Start > End && TimeOnly.FromDateTime(local) >= Start)
        {
            day = day.AddDays(1); // evening: tomorrow morning
        }

        var end = day.ToDateTime(End, DateTimeKind.Unspecified);
        while (Zone.IsInvalidTime(end))
        {
            end = end.AddMinutes(30); // a DST gap; never the case at 08:00 in Europe
        }

        return TimeZoneInfo.ConvertTimeToUtc(end, Zone);
    }
}
