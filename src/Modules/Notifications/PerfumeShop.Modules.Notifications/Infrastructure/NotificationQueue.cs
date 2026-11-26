using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PerfumeShop.Modules.Notifications.Domain;

namespace PerfumeShop.Modules.Notifications.Infrastructure;

/// <summary>
/// Renders a template and queues one message per recipient (the dispatcher sends them). Does not save:
/// the caller saves in the same transaction as its inbox entry. Already queued messages (same dedup key) are skipped.
/// </summary>
internal sealed class NotificationQueue(
    NotificationsDbContext db,
    IOptions<NotificationOptions> options,
    QuietHours quietHours,
    TimeProvider timeProvider,
    ILogger<NotificationQueue> logger)
{
    /// <summary>Customer SMS to <paramref name="phone"/>.</summary>
    public Task<int> CustomerSmsAsync(string templateKey, string? phone, IReadOnlyDictionary<string, string> values, string sourceId, string? orderNumber, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(phone)
            ? Skip(templateKey, "no phone number")
            : QueueAsync(templateKey, [phone], values, sourceId, orderNumber, ct);

    /// <summary>Staff alert email to every recipient in the alert settings (if the alert is on).</summary>
    public async Task<int> StaffAlertAsync(string templateKey, IReadOnlyDictionary<string, string> values, string sourceId, string? orderNumber, CancellationToken ct)
    {
        var definition = TemplateCatalog.Find(templateKey) ?? throw new InvalidOperationException($"Unknown template {templateKey}.");
        var settings = await db.StaffAlertSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        if (settings is null || definition.Alert is not { } alert || !settings.IsOn(alert))
        {
            return await Skip(templateKey, "alert is switched off");
        }

        if (settings.Recipients.Count == 0)
        {
            logger.LogWarning("Staff alert {Template} ({Source}) has no recipients: set them in the staff alert settings.", templateKey, sourceId);
            return 0;
        }

        return await QueueAsync(templateKey, settings.Recipients, values, sourceId, orderNumber, ct);
    }

    private async Task<int> QueueAsync(string templateKey, IReadOnlyList<string> recipients, IReadOnlyDictionary<string, string> values, string sourceId, string? orderNumber, CancellationToken ct)
    {
        var definition = TemplateCatalog.Find(templateKey) ?? throw new InvalidOperationException($"Unknown template {templateKey}.");
        var template = await db.Templates.AsNoTracking().FirstOrDefaultAsync(t => t.Key == templateKey, ct);
        if (template is not { IsEnabled: true })
        {
            return await Skip(templateKey, template is null ? "template missing" : "template disabled");
        }

        var rendered = Render(template, definition, values, options.Value);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var queued = 0;
        foreach (var recipient in recipients.Select(r => r.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var dedup = NotificationMessage.Dedup(templateKey, sourceId, recipient);
            if (db.Messages.Local.Any(m => m.DedupKey == dedup) || await db.Messages.AnyAsync(m => m.DedupKey == dedup, ct))
            {
                continue; // this event / job run already produced this message
            }

            var customerSms = definition.Channel == NotificationChannel.Sms && definition.Audience == NotificationAudience.Customer;
            var sendAfter = customerSms ? quietHours.NextAllowedUtc(now) : now;
            if (rendered.Sms is { IsMultiSegment: true } sms)
            {
                logger.LogWarning("SMS {Template} for {Order} is {Segments} segments ({Length} chars, {Encoding})", templateKey, orderNumber, sms.Segments, sms.Length, sms.Encoding);
            }

            db.Messages.Add(NotificationMessage.Queue(definition.Channel, definition.Audience, recipient, templateKey, rendered.Subject, rendered.Body,
                sourceId, orderNumber, rendered.Sms, sendAfter, now));
            queued++;
        }

        return queued;
    }

    /// <summary>Shop placeholders + the given values; SMS transliterated when the template says so.</summary>
    public static RenderedMessage Render(NotificationTemplate template, TemplateDefinition definition, IReadOnlyDictionary<string, string> values, NotificationOptions options) =>
        Render(definition, template.Subject, template.Body, template.Transliterate, values, options);

    public static RenderedMessage Render(TemplateDefinition definition, string? subject, string body, bool transliterate, IReadOnlyDictionary<string, string> values, NotificationOptions options)
    {
        var all = new Dictionary<string, string>(values, StringComparer.Ordinal)
        {
            ["shopName"] = options.ShopName,
            ["shopPhone"] = options.ShopPhone,
        };
        var text = TemplateText.Render(body, all);
        if (definition.Channel == NotificationChannel.Sms)
        {
            text = transliterate ? SmsText.Transliterate(text) : text;
            return new RenderedMessage(null, text, SmsText.Analyze(text));
        }

        return new RenderedMessage(subject is null ? null : TemplateText.Render(subject, all), text, null);
    }

    private Task<int> Skip(string templateKey, string reason)
    {
        logger.LogInformation("Notification {Template} not queued: {Reason}", templateKey, reason);
        return Task.FromResult(0);
    }
}

internal sealed record RenderedMessage(string? Subject, string Body, SmsInfo? Sms);
