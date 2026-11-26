using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Orders.Contracts;
using PerfumeShop.Modules.Orders.Domain;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Orders.Infrastructure;

internal sealed class OrdersApi(OrdersDbContext db, OrderTransitions transitions) : IOrdersApi
{
    public Task<Result<OrderSummaryDto>> MarkShippedAsync(Guid orderId, string courierName, string trackingNumber, string actorId, CancellationToken cancellationToken = default) =>
        IdempotentAsync(orderId, o => o.Status is not (OrderStatus.Pending or OrderStatus.Confirmed) && o.TrackingNumber == trackingNumber.Trim(),
            () => transitions.ShipAsync(orderId, OrderActor.Staff(actorId), courierName, trackingNumber, cancellationToken), cancellationToken);

    public Task<Result<OrderSummaryDto>> MarkDeliveredAsync(Guid orderId, string actorId, CancellationToken cancellationToken = default) =>
        IdempotentAsync(orderId, o => o.Status is OrderStatus.Delivered or OrderStatus.CashCollected,
            () => transitions.MarkDeliveredAsync(orderId, OrderActor.Staff(actorId), cancellationToken), cancellationToken);

    public Task<Result<OrderSummaryDto>> MarkRefusedAsync(Guid orderId, string? reason, string actorId, CancellationToken cancellationToken = default) =>
        IdempotentAsync(orderId, o => o.Status == OrderStatus.Refused,
            () => transitions.MarkRefusedAsync(orderId, OrderActor.Staff(actorId), reason, cancellationToken), cancellationToken);

    public Task<Result<OrderSummaryDto>> MarkCashCollectedAsync(Guid orderId, decimal amount, string? note, string actorId, CancellationToken cancellationToken = default) =>
        IdempotentAsync(orderId, o => o.Status == OrderStatus.CashCollected,
            () => transitions.MarkCashCollectedAsync(orderId, OrderActor.Staff(actorId), amount, note, cancellationToken), cancellationToken);

    private async Task<Result<OrderSummaryDto>> IdempotentAsync(
        Guid orderId, Func<Order, bool> alreadyDone, Func<Task<Result<Order>>> change, CancellationToken cancellationToken)
    {
        var current = await db.Orders.AsNoTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        if (current is null)
        {
            return Error.NotFound("Orders.Order.NotFound", "Order not found.");
        }

        if (alreadyDone(current))
        {
            return ToDto(current)!;
        }

        var changed = await change();
        return changed.IsSuccess ? ToDto(changed.Value)! : changed.Error;
    }

    public async Task<OrderSummaryDto?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        ToDto(await db.Orders.AsNoTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken));

    public async Task<OrderSummaryDto?> GetOrderByNumberAsync(string orderNumber, CancellationToken cancellationToken = default) =>
        ToDto(await db.Orders.AsNoTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Number == orderNumber.Trim().ToUpperInvariant(), cancellationToken));

    public async Task<IReadOnlyDictionary<Guid, string>> GetOrderNumbersAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default)
    {
        var ids = orderIds.Distinct().ToList();
        return ids.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Orders.AsNoTracking().Where(o => ids.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Number, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, CustomerOrderStatsDto>> GetCustomerOrderStatsAsync(IReadOnlyCollection<Guid> customerIds, CancellationToken cancellationToken = default)
    {
        var ids = customerIds.Distinct().ToList();
        return ids.Count == 0
            ? new Dictionary<Guid, CustomerOrderStatsDto>()
            : await db.Orders.AsNoTracking().Where(o => ids.Contains(o.CustomerId))
                .GroupBy(o => o.CustomerId)
                .Select(g => new { g.Key, Orders = g.Count(), Refused = g.Count(o => o.Status == OrderStatus.Refused) })
                .ToDictionaryAsync(g => g.Key, g => new CustomerOrderStatsDto(g.Orders, g.Refused), cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetCustomerIdsWithRefusalsAsync(CancellationToken cancellationToken = default) =>
        await db.Orders.AsNoTracking().Where(o => o.Status == OrderStatus.Refused).Select(o => o.CustomerId).Distinct().ToListAsync(cancellationToken);

    internal static OrderSummaryDto? ToDto(Order? o) => o is null
        ? null
        : new OrderSummaryDto(
            o.Id, o.Number, o.Status.ToString(), o.CustomerId, o.Customer.FullName, o.Customer.Phone,
            new OrderAddressDto(o.Address.CountryCode, o.Address.City, o.Address.Street, o.Address.Notes),
            o.Subtotal, o.DeliveryFee, o.Total, o.VatAmount, Order.Currency, Order.PaymentMethod, o.CourierName, o.TrackingNumber, o.PlacedAtUtc,
            [.. o.Lines.Select(l => new OrderLineSummaryDto(
                l.Id, l.VariantId, l.Sku, l.BrandName, l.FragranceName, l.VariantType, l.SizeValue, l.SizeUnit,
                l.UnitPrice, l.Quantity, l.LineTotal, l.VatAmount, [.. l.Batches.Select(b => $"{b.BatchCode}×{b.Quantity}")]))]);
}
