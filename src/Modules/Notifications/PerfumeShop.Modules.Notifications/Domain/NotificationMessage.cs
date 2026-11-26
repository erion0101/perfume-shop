using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Notifications.Domain;

internal enum NotificationStatus
{
    /// <summary>Waiting to be sent (possibly held until quiet hours end, or until the next retry).</summary>
    Queued = 1,
    Sent = 2,
    /// <summary>All attempts failed; staff can resend.</summary>
    Failed = 3,
}

/// <summary>
/// One message to one recipient, rendered when it was queued. <see cref="DedupKey"/> (template + source + recipient)
/// is unique, so an event or a daily job produces each message at most once.
/// </summary>
internal sealed class NotificationMessage : AggregateRoot<Guid>, IAuditable
{
    public const int MaxAttempts = 5;

    /// <summary>Wait after the 1st, 2nd, 3rd and 4th failed attempt; the 5th failure is final.</summary>
    public static readonly IReadOnlyList<TimeSpan> Backoff = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2)];

    private readonly List<NotificationAttempt> _attempts = [];

    private NotificationMessage() { }

    private NotificationMessage(Guid id) : base(id) { }

    public NotificationChannel Channel { get; private set; }

    public NotificationAudience Audience { get; private set; }

    public string Recipient { get; private set; } = null!;

    public string TemplateKey { get; private set; } = null!;

    public string? Subject { get; private set; }

    /// <summary>Exactly what is (or was) sent, after transliteration.</summary>
    public string Body { get; private set; } = null!;

    public string DedupKey { get; private set; } = null!;

    /// <summary>Event id, "2026-11-26" for daily alerts, Hangfire job id, ...</summary>
    public string SourceId { get; private set; } = null!;

    public string? OrderNumber { get; private set; }

    /// <summary>SMS only.</summary>
    public string? Encoding { get; private set; }

    public int? Segments { get; private set; }

    public NotificationStatus Status { get; private set; }

    /// <summary>Earliest send time when queued (after quiet hours for customer SMS).</summary>
    public DateTime SendAfterUtc { get; private set; }

    public DateTime NextAttemptAtUtc { get; private set; }

    /// <summary>Attempts since it was queued (or last resent).</summary>
    public int Attempts { get; private set; }

    public string? Provider { get; private set; }

    public string? ProviderMessageId { get; private set; }

    public string? LastError { get; private set; }

    public DateTime? SentAtUtc { get; private set; }

    public string? ResentBy { get; private set; }

    public IReadOnlyList<NotificationAttempt> AttemptLog => _attempts.AsReadOnly();

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Queued during quiet hours: sent when they end.</summary>
    public bool HeldForQuietHours { get; private set; }

    public static string Dedup(string templateKey, string sourceId, string recipient) => $"{templateKey}:{sourceId}:{recipient}";

    public static NotificationMessage Queue(
        NotificationChannel channel,
        NotificationAudience audience,
        string recipient,
        string templateKey,
        string? subject,
        string body,
        string sourceId,
        string? orderNumber,
        SmsInfo? sms,
        DateTime sendAfterUtc,
        DateTime nowUtc) => new(Guid.CreateVersion7())
        {
            Channel = channel,
            Audience = audience,
            Recipient = recipient,
            TemplateKey = templateKey,
            Subject = subject,
            Body = body,
            SourceId = sourceId,
            DedupKey = Dedup(templateKey, sourceId, recipient),
            OrderNumber = orderNumber,
            Encoding = sms?.Encoding,
            Segments = sms?.Segments,
            Status = NotificationStatus.Queued,
            SendAfterUtc = sendAfterUtc,
            NextAttemptAtUtc = sendAfterUtc,
            HeldForQuietHours = sendAfterUtc > nowUtc,
        };

    public bool IsCustomerSms => Channel == NotificationChannel.Sms && Audience == NotificationAudience.Customer;

    /// <summary>Quiet hours started while it waited (e.g. a retry): try again when they end.</summary>
    public void HoldUntil(DateTime utc) => NextAttemptAtUtc = utc;

    public void RecordSent(string provider, string? providerMessageId, string? response, DateTime nowUtc)
    {
        _attempts.Add(new NotificationAttempt(Id, nowUtc, provider, true, providerMessageId, response, null));
        Attempts++;
        Status = NotificationStatus.Sent;
        Provider = provider;
        ProviderMessageId = providerMessageId;
        LastError = null;
        SentAtUtc = nowUtc;
    }

    /// <param name="quietHours">For customer SMS: a retry never lands in quiet hours.</param>
    public void RecordFailed(string provider, string error, string? response, DateTime nowUtc, QuietHours? quietHours)
    {
        error = error.Length > 1000 ? error[..1000] : error;
        _attempts.Add(new NotificationAttempt(Id, nowUtc, provider, false, null, response, error));
        Attempts++;
        Provider = provider;
        LastError = error;
        if (Attempts >= MaxAttempts)
        {
            Status = NotificationStatus.Failed;
            return;
        }

        var next = nowUtc + Backoff[Attempts - 1];
        NextAttemptAtUtc = quietHours is not null && IsCustomerSms ? quietHours.NextAllowedUtc(next) : next;
    }

    /// <summary>Staff send a failed message again (same text; a new series of attempts).</summary>
    public Result Resend(string actorId, DateTime nowUtc, QuietHours? quietHours)
    {
        if (Status != NotificationStatus.Failed)
        {
            return Error.Conflict("Notifications.Message.NotFailed", $"Only failed messages can be resent; this one is {Status}.");
        }

        Status = NotificationStatus.Queued;
        Attempts = 0;
        ResentBy = actorId;
        NextAttemptAtUtc = quietHours is not null && IsCustomerSms ? quietHours.NextAllowedUtc(nowUtc) : nowUtc;
        return Result.Success();
    }
}

/// <summary>One send attempt with the provider's answer (append-only).</summary>
internal sealed class NotificationAttempt : Entity<Guid>
{
    private NotificationAttempt() { }

    public NotificationAttempt(Guid messageId, DateTime attemptedAtUtc, string provider, bool succeeded, string? providerMessageId, string? response, string? error)
        : base(Guid.CreateVersion7())
    {
        MessageId = messageId;
        AttemptedAtUtc = attemptedAtUtc;
        Provider = provider;
        Succeeded = succeeded;
        ProviderMessageId = providerMessageId;
        Response = response is { Length: > 2000 } ? response[..2000] : response;
        Error = error;
    }

    public Guid MessageId { get; private set; }

    public DateTime AttemptedAtUtc { get; private set; }

    public string Provider { get; private set; } = null!;

    public bool Succeeded { get; private set; }

    public string? ProviderMessageId { get; private set; }

    public string? Response { get; private set; }

    public string? Error { get; private set; }
}
