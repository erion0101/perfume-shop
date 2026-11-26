namespace PerfumeShop.Modules.Notifications.Contracts;

/// <summary>Read access to the notification log (e.g. the admin order page: "which SMS did this customer get?").</summary>
public interface INotificationsApi
{
    Task<IReadOnlyList<NotificationSummaryDto>> GetForOrderAsync(string orderNumber, CancellationToken cancellationToken = default);
}

/// <param name="Channel">Sms or Email.</param>
/// <param name="Status">Queued, Sent, Failed.</param>
public sealed record NotificationSummaryDto(
    Guid Id, string Channel, string TemplateKey, string Recipient, string Status, string Body, DateTime CreatedAtUtc, DateTime? SentAtUtc);
