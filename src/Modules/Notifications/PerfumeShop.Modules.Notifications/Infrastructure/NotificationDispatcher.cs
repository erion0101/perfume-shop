using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PerfumeShop.Modules.Notifications.Contracts;
using PerfumeShop.Modules.Notifications.Domain;
using PerfumeShop.Shared.Infrastructure.Jobs;

namespace PerfumeShop.Modules.Notifications.Infrastructure;

/// <summary>
/// Hangfire job "notifications-dispatch": sends due messages (locked with SKIP LOCKED, so two servers never send the
/// same one), records every attempt and schedules retries with backoff. Customer SMS never go out in quiet hours.
/// </summary>
internal sealed class NotificationDispatcher(
    NotificationsDbContext db,
    ISmsSender sms,
    IEmailSender email,
    QuietHours quietHours,
    IOptions<NotificationOptions> options,
    TimeProvider timeProvider,
    ILogger<NotificationDispatcher> logger) : IRecurringJob
{
    public async Task RunAsync(CancellationToken cancellationToken) => await DispatchAsync(cancellationToken);

    /// <returns>Messages sent successfully.</returns>
    public async Task<int> DispatchAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var batch = options.Value.DispatchBatchSize;
        var queued = NotificationStatus.Queued.ToString();
        var messages = await db.Messages
            .FromSql($"""
                SELECT *, xmin FROM notifications.messages
                WHERE status = {queued} AND next_attempt_at_utc <= {now}
                ORDER BY next_attempt_at_utc
                LIMIT {batch}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        var sent = 0;
        foreach (var message in messages)
        {
            if (message.IsCustomerSms && quietHours.IsQuiet(now))
            {
                message.HoldUntil(quietHours.NextAllowedUtc(now));
                continue;
            }

            var (provider, ok, id, response, error) = await SendAsync(message, cancellationToken);
            if (ok)
            {
                message.RecordSent(provider, id, response, now);
                sent++;
            }
            else
            {
                message.RecordFailed(provider, error ?? "The provider did not accept the message.", response, now, quietHours);
                logger.Log(message.Status == NotificationStatus.Failed ? LogLevel.Error : LogLevel.Warning,
                    "{Channel} {Template} to {Recipient} failed (attempt {Attempt}, now {Status}): {Error}",
                    message.Channel, message.TemplateKey, message.Recipient, message.Attempts, message.Status, message.LastError);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return sent;
    }

    private async Task<(string Provider, bool Ok, string? Id, string? Response, string? Error)> SendAsync(NotificationMessage message, CancellationToken ct)
    {
        try
        {
            if (message.Channel == NotificationChannel.Sms)
            {
                var r = await sms.SendAsync(message.Recipient, message.Body, ct);
                return (sms.Name, r.Succeeded, r.ProviderMessageId, r.Response, r.Error);
            }

            var e = await email.SendAsync(new EmailMessage(message.Recipient, message.Subject ?? message.TemplateKey, message.Body), ct);
            return (email.Name, e.Succeeded, e.ProviderMessageId, e.Response, e.Error);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (message.Channel == NotificationChannel.Sms ? sms.Name : email.Name, false, null, null, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
