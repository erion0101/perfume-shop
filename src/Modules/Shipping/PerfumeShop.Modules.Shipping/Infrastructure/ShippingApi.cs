using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Shipping.Contracts;
using PerfumeShop.Modules.Shipping.Domain;
using PerfumeShop.Modules.Shipping.Features.Settlements;

namespace PerfumeShop.Modules.Shipping.Infrastructure;

internal sealed class ShippingApi(ShippingDbContext db, TimeProvider timeProvider) : IShippingApi
{
    public async Task<IReadOnlyList<OverdueCodDto>> GetOverdueCodAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        List<OverdueCodDto> overdue = [];
        foreach (var courier in await db.Couriers.AsNoTracking().OrderBy(c => c.Name).ToListAsync(cancellationToken))
        {
            overdue.AddRange((await SettlementsHandler.UnpaidAsync(db, courier, now, cancellationToken)).Select(u =>
                new OverdueCodDto(u.ShipmentId, u.OrderNumber, courier.Name, u.TrackingNumber, u.CodAmount, u.DeliveredAtUtc, u.DaysSinceDelivery, courier.RemittanceDays)));
        }

        return [.. overdue.OrderBy(o => o.DeliveredAtUtc)];
    }

    public async Task<ShipmentSummaryDto?> GetShipmentForOrderAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        ToDto(await db.Shipments.AsNoTracking().FirstOrDefaultAsync(s => s.OrderId == orderId, cancellationToken));

    public async Task<ShipmentSummaryDto?> GetShipmentByTrackingNumberAsync(string trackingNumber, CancellationToken cancellationToken = default)
    {
        var tracking = trackingNumber.Trim().ToUpperInvariant();
        return ToDto(await db.Shipments.AsNoTracking().FirstOrDefaultAsync(s => s.TrackingNumber == tracking, cancellationToken));
    }

    private static ShipmentSummaryDto? ToDto(Shipment? s) => s is null
        ? null
        : new ShipmentSummaryDto(s.Id, s.OrderId, s.OrderNumber, s.CourierId, s.CourierName, s.TrackingNumber, s.Status.ToString(),
            s.CodAmount, s.CreatedAtUtc, s.DeliveredAtUtc, s.SettledAtUtc);
}
