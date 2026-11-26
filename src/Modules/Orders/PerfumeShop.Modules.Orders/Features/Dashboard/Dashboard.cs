using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PerfumeShop.Modules.Orders.Domain;
using PerfumeShop.Modules.Orders.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Modules;

namespace PerfumeShop.Modules.Orders.Features.Dashboard;

/// <param name="Shipped">Orders that changed to this status today (Kosovo time), not orders placed today.</param>
internal sealed record TodayOrdersDto(DateOnly Date, int Placed, int Confirmed, int Shipped, int Delivered, int Refused, int Cancelled);

internal sealed record DayOrdersDto(DateOnly Date, int Orders, decimal Revenue);

internal sealed record TopSellerDto(Guid FragranceId, string BrandName, string FragranceName, int Quantity, decimal Revenue);

/// <param name="Rate">Refused / (delivered + refused) over the period, 0-1.</param>
internal sealed record RefusalRateDto(int Days, int Delivered, int Refused, decimal Rate);

internal sealed record OrdersDashboardDto(TodayOrdersDto Today, int PendingNow, IReadOnlyList<DayOrdersDto> Last14Days, IReadOnlyList<TopSellerDto> TopSelling30Days, RefusalRateDto Refusals30Days);

internal sealed class DashboardHandler(OrdersDbContext db, TimeProvider timeProvider, IConfiguration configuration) : IHandler
{
    private TimeZoneInfo Zone => TimeZoneInfo.FindSystemTimeZoneById(configuration["Orders:TimeZone"] ?? "Europe/Belgrade");

    public async Task<OrdersDashboardDto> HandleAsync(CancellationToken ct)
    {
        var zone = Zone;
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone));
        DateTime StartUtc(DateOnly day) => TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone);
        var todayStart = StartUtc(today);
        var tomorrowStart = StartUtc(today.AddDays(1));

        var changesToday = await db.Orders.AsNoTracking().SelectMany(o => o.History)
            .Where(h => h.ChangedAtUtc >= todayStart && h.ChangedAtUtc < tomorrowStart)
            .GroupBy(h => h.ToStatus)
            .Select(g => new { g.Key, Count = g.Select(h => h.OrderId).Distinct().Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        int Today(OrderStatus status) => changesToday.GetValueOrDefault(status);

        var pending = await db.Orders.CountAsync(o => o.Status == OrderStatus.Pending, ct);

        var fromDay = today.AddDays(-13);
        var fromUtc = StartUtc(fromDay);
        var placed = await db.Orders.AsNoTracking()
            .Where(o => o.PlacedAtUtc >= fromUtc && o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Expired)
            .Select(o => new { o.PlacedAtUtc, o.Total })
            .ToListAsync(ct);
        var perDay = Enumerable.Range(0, 14).Select(i => fromDay.AddDays(i)).Select(day =>
        {
            var orders = placed.Where(o => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(o.PlacedAtUtc, zone)) == day).ToList();
            return new DayOrdersDto(day, orders.Count, orders.Sum(o => o.Total));
        }).ToList();

        var since30 = nowUtc.AddDays(-30);
        var sold = await db.Orders.AsNoTracking()
            .Where(o => o.PlacedAtUtc >= since30 && o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Expired && o.Status != OrderStatus.Refused)
            .SelectMany(o => o.Lines.Select(l => new { l.FragranceId, l.BrandName, l.FragranceName, l.Quantity, Revenue = l.UnitPrice * l.Quantity }))
            .GroupBy(l => l.FragranceId)
            .Select(g => new { FragranceId = g.Key, BrandName = g.Max(l => l.BrandName), FragranceName = g.Max(l => l.FragranceName), Quantity = g.Sum(l => l.Quantity), Revenue = g.Sum(l => l.Revenue) })
            .OrderByDescending(t => t.Quantity).ThenByDescending(t => t.Revenue)
            .Take(10)
            .ToListAsync(ct);
        var top = sold.Select(t => new TopSellerDto(t.FragranceId, t.BrandName!, t.FragranceName!, t.Quantity, t.Revenue)).ToList();

        var outcomes = await db.Orders.AsNoTracking().SelectMany(o => o.History)
            .Where(h => h.ChangedAtUtc >= since30 && (h.ToStatus == OrderStatus.Delivered || h.ToStatus == OrderStatus.Refused))
            .GroupBy(h => h.ToStatus)
            .Select(g => new { g.Key, Count = g.Select(h => h.OrderId).Distinct().Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var delivered = outcomes.GetValueOrDefault(OrderStatus.Delivered);
        var refused = outcomes.GetValueOrDefault(OrderStatus.Refused);
        var rate = delivered + refused == 0 ? 0 : decimal.Round((decimal)refused / (delivered + refused), 3);

        return new OrdersDashboardDto(
            new TodayOrdersDto(today, Today(OrderStatus.Pending), Today(OrderStatus.Confirmed), Today(OrderStatus.Shipped), Today(OrderStatus.Delivered),
                Today(OrderStatus.Refused), Today(OrderStatus.Cancelled) + Today(OrderStatus.Expired)),
            pending, perDay, top, new RefusalRateDto(30, delivered, refused, rate));
    }
}

internal static class DashboardEndpoint
{
    public static void Map(RouteGroupBuilder staff) =>
        staff.MapGet("/dashboard", async (DashboardHandler h, CancellationToken ct) => TypedResults.Ok(await h.HandleAsync(ct)))
            .WithName("OrdersDashboard")
            .WithDescription("Today (Kosovo time), pending now, last 14 days, top sellers and refusal rate (30 days).");
}
