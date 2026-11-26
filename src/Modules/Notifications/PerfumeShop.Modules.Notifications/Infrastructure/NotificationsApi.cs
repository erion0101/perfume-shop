using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Notifications.Contracts;

namespace PerfumeShop.Modules.Notifications.Infrastructure;

internal sealed class NotificationsApi(NotificationsDbContext db) : INotificationsApi
{
    public async Task<IReadOnlyList<NotificationSummaryDto>> GetForOrderAsync(string orderNumber, CancellationToken cancellationToken = default)
    {
        var number = orderNumber.Trim().ToUpperInvariant();
        return await db.Messages.AsNoTracking()
            .Where(m => m.OrderNumber == number)
            .OrderBy(m => m.CreatedAtUtc)
            .Select(m => new NotificationSummaryDto(m.Id, m.Channel.ToString(), m.TemplateKey, m.Recipient, m.Status.ToString(), m.Body, m.CreatedAtUtc, m.SentAtUtc))
            .ToListAsync(cancellationToken);
    }
}
