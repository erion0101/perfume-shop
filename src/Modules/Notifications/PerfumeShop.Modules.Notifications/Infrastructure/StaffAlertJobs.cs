using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using PerfumeShop.Modules.Catalog.Contracts;
using PerfumeShop.Modules.Inventory.Contracts;
using PerfumeShop.Modules.Notifications.Domain;
using PerfumeShop.Modules.Shipping.Contracts;
using PerfumeShop.Shared.Infrastructure.Jobs;

namespace PerfumeShop.Modules.Notifications.Infrastructure;

/// <summary>Daily job "notifications-low-stock": one email per day listing variants at or below their threshold.</summary>
internal sealed class LowStockAlertJob(
    NotificationsDbContext db, NotificationQueue queue, IInventoryApi inventory, ICatalogApi catalog, IOptions<NotificationOptions> options, TimeProvider timeProvider)
    : IRecurringJob
{
    public async Task RunAsync(CancellationToken cancellationToken) => await QueueAsync(cancellationToken);

    /// <returns>Messages queued (0 when nothing is low, or today's alert was already sent).</returns>
    public async Task<int> QueueAsync(CancellationToken ct)
    {
        var low = await inventory.GetLowStockAsync(ct);
        if (low.Count == 0)
        {
            return 0;
        }

        var names = await catalog.GetVariantSummariesAsync([.. low.Select(l => l.VariantId)], ct);
        var items = new StringBuilder();
        foreach (var item in low)
        {
            var label = names.TryGetValue(item.VariantId, out var v)
                ? $"{v.Sku} {v.BrandName} {v.FragranceName}{Format.Size(v.SizeValue, v.SizeUnit)}"
                : item.VariantId.ToString();
            items.Append(System.Globalization.CultureInfo.InvariantCulture, $"{label}: {item.Available} (kufiri {item.Threshold})\n");
        }

        var day = Format.LocalDay(timeProvider.GetUtcNow().UtcDateTime, options.Value.TimeZone);
        var queued = await queue.StaffAlertAsync(TemplateKeys.StaffLowStock, new Dictionary<string, string>
        {
            ["count"] = low.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["date"] = Format.Day(day),
            ["items"] = items.ToString().TrimEnd(),
        }, $"low-stock-{day:yyyy-MM-dd}", null, ct);
        return await SaveOnceAsync(db, queued, ct);
    }

    /// <summary>A second server running the same daily job at the same moment loses on the dedup key: not an error.</summary>
    internal static async Task<int> SaveOnceAsync(NotificationsDbContext db, int queued, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return queued;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return 0;
        }
    }
}

/// <summary>Daily job "notifications-unpaid-cod": delivered parcels the courier has not paid within its remittance days.</summary>
internal sealed class UnpaidDeliveriesAlertJob(
    NotificationsDbContext db, NotificationQueue queue, IShippingApi shipping, IOptions<NotificationOptions> options, TimeProvider timeProvider)
    : IRecurringJob
{
    public async Task RunAsync(CancellationToken cancellationToken) => await QueueAsync(cancellationToken);

    public async Task<int> QueueAsync(CancellationToken ct)
    {
        var overdue = await shipping.GetOverdueCodAsync(ct);
        if (overdue.Count == 0)
        {
            return 0;
        }

        var zone = options.Value.TimeZone;
        var items = string.Join("\n", overdue.Select(o =>
            $"{o.CourierName}: {o.OrderNumber} {o.TrackingNumber} {Format.Amount(o.CodAmount)} EUR, dorëzuar {Format.Day(Format.LocalDay(o.DeliveredAtUtc, zone))} ({o.DaysSinceDelivery} ditë, afati {o.RemittanceDays})"));
        var day = Format.LocalDay(timeProvider.GetUtcNow().UtcDateTime, zone);
        var queued = await queue.StaffAlertAsync(TemplateKeys.StaffUnpaidDeliveries, new Dictionary<string, string>
        {
            ["count"] = overdue.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["amount"] = Format.Amount(overdue.Sum(o => o.CodAmount)),
            ["date"] = Format.Day(day),
            ["items"] = items,
        }, $"unpaid-cod-{day:yyyy-MM-dd}", null, ct);
        return await LowStockAlertJob.SaveOnceAsync(db, queued, ct);
    }
}
