using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Shipping.Domain;
using PerfumeShop.Modules.Shipping.Features.Settlements;
using PerfumeShop.Modules.Shipping.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Modules;

namespace PerfumeShop.Modules.Shipping.Features.Reports;

internal sealed record InTransitDto(Guid ShipmentId, string OrderNumber, string CourierName, string TrackingNumber, string Status, string City, decimal CodAmount, DateTime HandedOverAtUtc, int Days);

/// <param name="RefusalRate">(refused + returned) / (delivered + refused + returned), 0-1.</param>
internal sealed record CourierRefusalsDto(Guid CourierId, string CourierName, int Delivered, int Refused, decimal RefusalRate);

internal sealed record CustomerRefusalsDto(Guid CustomerId, string LastName, string Phone, int Shipments, int Refused, decimal RefusalRate);

internal sealed record RefusalReportDto(IReadOnlyList<CourierRefusalsDto> ByCourier, IReadOnlyList<CustomerRefusalsDto> ByCustomer);

/// <param name="HeldAmount">Delivered but not yet paid to us (EUR).</param>
/// <param name="OverdueAmount">Part of it past the courier's remittance days.</param>
internal sealed record CodHeldDto(Guid CourierId, string CourierName, int Parcels, decimal HeldAmount, int OverdueParcels, decimal OverdueAmount, DateTime? OldestDeliveryUtc, IReadOnlyList<UnpaidDeliveryDto> Overdue);

/// <summary>Staff reports over shipments.</summary>
internal sealed class ReportsHandler(ShippingDbContext db, TimeProvider timeProvider) : IHandler
{
    private static readonly ShipmentStatus[] Finished = [ShipmentStatus.Delivered, ShipmentStatus.Refused, ShipmentStatus.ReturnedToWarehouse];

    /// <summary>Parcels with the courier for more than <paramref name="olderThanDays"/> days and not delivered/refused.</summary>
    public async Task<IReadOnlyList<InTransitDto>> InTransitAsync(int olderThanDays, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var before = now.AddDays(-olderThanDays);
        var rows = await db.Shipments.AsNoTracking()
            .Where(s => (s.Status == ShipmentStatus.HandedOver || s.Status == ShipmentStatus.InTransit) && s.HandedOverAtUtc <= before)
            .OrderBy(s => s.HandedOverAtUtc)
            .ToListAsync(ct);
        return [.. rows.Select(s => new InTransitDto(s.Id, s.OrderNumber, s.CourierName, s.TrackingNumber, s.Status.ToString(), s.Recipient.City, s.CodAmount,
            s.HandedOverAtUtc!.Value, (int)(now - s.HandedOverAtUtc!.Value).TotalDays))];
    }

    public async Task<RefusalReportDto> RefusalsAsync(int top, CancellationToken ct)
    {
        var finished = await db.Shipments.AsNoTracking()
            .Where(s => Finished.Contains(s.Status))
            .Select(s => new { s.CourierId, s.CourierName, s.CustomerId, s.Status, Name = s.Recipient.FullName, s.Recipient.Phone, s.CreatedAtUtc })
            .ToListAsync(ct);

        static bool IsRefused(ShipmentStatus status) => status is ShipmentStatus.Refused or ShipmentStatus.ReturnedToWarehouse;
        static decimal Rate(int refused, int total) => total == 0 ? 0 : decimal.Round((decimal)refused / total, 3);

        var byCourier = finished.GroupBy(s => new { s.CourierId, s.CourierName })
            .Select(g => new CourierRefusalsDto(g.Key.CourierId, g.Key.CourierName,
                g.Count(s => s.Status == ShipmentStatus.Delivered), g.Count(s => IsRefused(s.Status)), Rate(g.Count(s => IsRefused(s.Status)), g.Count())))
            .OrderByDescending(c => c.RefusalRate)
            .ToList();

        var byCustomer = finished.GroupBy(s => s.CustomerId)
            .Select(g =>
            {
                var latest = g.MaxBy(s => s.CreatedAtUtc)!;
                var refused = g.Count(s => IsRefused(s.Status));
                return new CustomerRefusalsDto(g.Key, latest.Name, latest.Phone, g.Count(), refused, Rate(refused, g.Count()));
            })
            .Where(c => c.Refused > 0)
            .OrderByDescending(c => c.Refused).ThenByDescending(c => c.RefusalRate)
            .Take(top)
            .ToList();

        return new RefusalReportDto(byCourier, byCustomer);
    }

    /// <summary>Cash each courier collected for us and still owes, with what is overdue.</summary>
    public async Task<IReadOnlyList<CodHeldDto>> CodHeldAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var couriers = await db.Couriers.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);
        var held = await db.Shipments.AsNoTracking()
            .Where(s => s.Status == ShipmentStatus.Delivered && s.SettledAtUtc == null)
            .GroupBy(s => s.CourierId)
            .Select(g => new { CourierId = g.Key, Parcels = g.Count(), Amount = g.Sum(s => s.CodAmount), Oldest = g.Min(s => s.DeliveredAtUtc) })
            .ToDictionaryAsync(g => g.CourierId, ct);

        List<CodHeldDto> report = [];
        foreach (var courier in couriers)
        {
            var overdue = await SettlementsHandler.UnpaidAsync(db, courier, now, ct);
            var h = held.GetValueOrDefault(courier.Id);
            report.Add(new CodHeldDto(courier.Id, courier.Name, h?.Parcels ?? 0, h?.Amount ?? 0, overdue.Count, overdue.Sum(o => o.CodAmount), h?.Oldest, overdue));
        }

        return report;
    }
}

internal static class ReportsEndpoint
{
    public static void Map(RouteGroupBuilder staff)
    {
        staff.MapGet("/reports/in-transit", async (int? olderThanDays, ReportsHandler h, CancellationToken ct) =>
                TypedResults.Ok(await h.InTransitAsync(Math.Clamp(olderThanDays ?? 3, 0, 365), ct)))
            .WithName("InTransitReport");

        staff.MapGet("/reports/refusals", async (int? top, ReportsHandler h, CancellationToken ct) =>
                TypedResults.Ok(await h.RefusalsAsync(Math.Clamp(top ?? 50, 1, 500), ct)))
            .WithName("RefusalReport");

        staff.MapGet("/reports/cod-held", async (ReportsHandler h, CancellationToken ct) => TypedResults.Ok(await h.CodHeldAsync(ct)))
            .WithName("CodHeldReport");
    }
}
