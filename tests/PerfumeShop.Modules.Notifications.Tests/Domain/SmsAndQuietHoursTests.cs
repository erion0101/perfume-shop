using PerfumeShop.Modules.Notifications.Domain;

namespace PerfumeShop.Modules.Notifications.Tests.Domain;

public sealed class SmsTextTests
{
    [Fact]
    public void Transliteration_replaces_e_and_c_with_diacritics() =>
        Assert.Equal("Faleminderit qe zgjodhet Cajupin, cmimi eshte i ulet. E", SmsText.Transliterate("Faleminderit që zgjodhët Çajupin, çmimi është i ulët. Ë"));

    [Theory]
    [InlineData(1, 1)]
    [InlineData(160, 1)]
    [InlineData(161, 2)]
    [InlineData(306, 2)]
    [InlineData(307, 3)]
    public void Gsm7_segments_are_160_then_153(int length, int segments)
    {
        var info = SmsText.Analyze(new string('a', length));

        Assert.Equal((SmsText.Gsm7, length, segments), (info.Encoding, info.Length, info.Segments));
    }

    [Theory]
    [InlineData(70, 1)]
    [InlineData(71, 2)]
    [InlineData(134, 2)]
    [InlineData(135, 3)]
    public void One_e_with_diaeresis_forces_ucs2_70_then_67(int length, int segments)
    {
        var info = SmsText.Analyze("ë" + new string('a', length - 1));

        Assert.Equal((SmsText.Ucs2, segments, "ë"), (info.Encoding, info.Segments, info.NonGsmCharacters));
    }

    [Fact]
    public void Euro_sign_counts_twice_and_capital_c_cedilla_is_gsm()
    {
        Assert.Equal((SmsText.Gsm7, 2), (SmsText.Analyze("€").Encoding, SmsText.Analyze("€").Length));
        Assert.Equal(SmsText.Gsm7, SmsText.Analyze("Ç").Encoding);
        Assert.Equal(SmsText.Ucs2, SmsText.Analyze("ç").Encoding);
        Assert.Equal(2, SmsText.Analyze(new string('a', 159) + "€").Segments); // 161 septets
    }

    [Fact]
    public void Multi_segment_sms_has_a_warning() =>
        Assert.Contains("2 SMS segments", SmsText.Analyze(new string('a', 200)).Warning, StringComparison.Ordinal);
}

public sealed class QuietHoursTests
{
    private static readonly TimeZoneInfo Kosovo = TimeZoneInfo.FindSystemTimeZoneById("Europe/Belgrade");
    private static readonly QuietHours Quiet = new(new TimeOnly(22, 0), new TimeOnly(8, 0), Kosovo);

    // November: Kosovo = UTC+1. July: UTC+2.
    [Theory]
    [InlineData("2026-11-26T20:59:00Z", false)] // 21:59
    [InlineData("2026-11-26T21:00:00Z", true)]  // 22:00
    [InlineData("2026-11-27T02:00:00Z", true)]  // 03:00
    [InlineData("2026-11-27T06:59:00Z", true)]  // 07:59
    [InlineData("2026-11-27T07:00:00Z", false)] // 08:00
    [InlineData("2026-07-10T20:30:00Z", true)]  // 22:30 summer time
    [InlineData("2026-07-10T19:30:00Z", false)] // 21:30 summer time
    public void Quiet_between_22_and_08_kosovo_time(string utc, bool quiet) =>
        Assert.Equal(quiet, Quiet.IsQuiet(DateTime.Parse(utc, null, System.Globalization.DateTimeStyles.AdjustToUniversal)));

    [Theory]
    [InlineData("2026-11-26T21:30:00Z", "2026-11-27T07:00:00Z")] // 22:30 → tomorrow 08:00
    [InlineData("2026-11-27T02:00:00Z", "2026-11-27T07:00:00Z")] // 03:00 → today 08:00
    [InlineData("2026-07-10T23:00:00Z", "2026-07-11T06:00:00Z")] // 01:00 summer → 08:00 = 06:00 UTC
    [InlineData("2026-11-26T12:00:00Z", "2026-11-26T12:00:00Z")] // daytime: now
    public void Messages_in_quiet_hours_wait_for_08_00(string utc, string expected) =>
        Assert.Equal(
            DateTime.Parse(expected, null, System.Globalization.DateTimeStyles.AdjustToUniversal),
            Quiet.NextAllowedUtc(DateTime.Parse(utc, null, System.Globalization.DateTimeStyles.AdjustToUniversal)));

    [Fact]
    public void A_window_that_does_not_cross_midnight_works_too()
    {
        var lunch = new QuietHours(new TimeOnly(12, 0), new TimeOnly(13, 0), TimeZoneInfo.Utc);

        Assert.True(lunch.IsQuiet(new DateTime(2026, 11, 26, 12, 30, 0, DateTimeKind.Utc)));
        Assert.Equal(new DateTime(2026, 11, 26, 13, 0, 0, DateTimeKind.Utc), lunch.NextAllowedUtc(new DateTime(2026, 11, 26, 12, 30, 0, DateTimeKind.Utc)));
    }
}

public sealed class NotificationMessageTests
{
    private static readonly DateTime Noon = new(2026, 11, 26, 11, 0, 0, DateTimeKind.Utc); // 12:00 Kosovo
    private static readonly QuietHours Quiet = new(new TimeOnly(22, 0), new TimeOnly(8, 0), TimeZoneInfo.FindSystemTimeZoneById("Europe/Belgrade"));

    private static NotificationMessage Sms(DateTime now) =>
        NotificationMessage.Queue(NotificationChannel.Sms, NotificationAudience.Customer, "+38344123456", TemplateKeys.OrderPlaced, null, "Hi", "evt-1", "PS-1",
            SmsText.Analyze("Hi"), Quiet.NextAllowedUtc(now), now);

    [Fact]
    public void Failures_are_retried_with_backoff_then_marked_failed()
    {
        var message = Sms(Noon);
        var now = Noon;
        List<TimeSpan> waits = [];
        while (message.Status == NotificationStatus.Queued)
        {
            message.RecordFailed("test", "down", """{"status":503}""", now, Quiet);
            if (message.Status == NotificationStatus.Queued)
            {
                waits.Add(message.NextAttemptAtUtc - now);
                now = message.NextAttemptAtUtc;
            }
        }

        Assert.Equal(NotificationMessage.Backoff, waits);
        Assert.Equal((NotificationStatus.Failed, 5, 5), (message.Status, message.Attempts, message.AttemptLog.Count));
        Assert.All(message.AttemptLog, a => Assert.Equal("""{"status":503}""", a.Response));
    }

    [Fact]
    public void A_retry_never_lands_in_quiet_hours()
    {
        var evening = new DateTime(2026, 11, 26, 20, 58, 0, DateTimeKind.Utc); // 21:58 Kosovo
        var message = Sms(evening);

        message.RecordFailed("test", "down", null, evening, Quiet); // +1 min = 21:59: fine
        message.RecordFailed("test", "down", null, evening.AddMinutes(1), Quiet); // +5 min = 22:04: quiet

        Assert.Equal(new DateTime(2026, 11, 27, 7, 0, 0, DateTimeKind.Utc), message.NextAttemptAtUtc);
    }

    [Fact]
    public void Queued_at_night_is_held_until_08_00()
    {
        var message = Sms(new DateTime(2026, 11, 26, 22, 0, 0, DateTimeKind.Utc)); // 23:00 Kosovo

        Assert.True(message.HeldForQuietHours);
        Assert.Equal(new DateTime(2026, 11, 27, 7, 0, 0, DateTimeKind.Utc), message.SendAfterUtc);
    }

    [Fact]
    public void Only_failed_messages_can_be_resent()
    {
        var message = Sms(Noon);
        Assert.Equal("Notifications.Message.NotFailed", message.Resend("staff", Noon, Quiet).Error.Code);

        for (var i = 0; i < NotificationMessage.MaxAttempts; i++)
        {
            message.RecordFailed("test", "down", null, Noon, Quiet);
        }

        Assert.True(message.Resend("staff", Noon, Quiet).IsSuccess);
        Assert.Equal((NotificationStatus.Queued, 0, "staff", Noon), (message.Status, message.Attempts, message.ResentBy, message.NextAttemptAtUtc));
        message.RecordSent("test", "id-1", "ok", Noon);
        Assert.Equal((NotificationStatus.Sent, 6), (message.Status, message.AttemptLog.Count));
    }

    [Fact]
    public void Dedup_key_is_template_source_and_recipient() =>
        Assert.Equal("order-placed:evt-1:+38344123456", Sms(Noon).DedupKey);
}
