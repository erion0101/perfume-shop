using PerfumeShop.Modules.Orders.Contracts;
using PerfumeShop.Modules.Orders.Domain;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Orders.Infrastructure;

internal static class OrderEventMapper
{
    public static IIntegrationEvent ToIntegrationEvent(OrderStatusChangedDomainEvent change)
    {
        var o = change.Order;
        var id = Guid.CreateVersion7();
        var at = change.AtUtc;
        return change.To switch
        {
            OrderStatus.Pending => new OrderPlacedIntegrationEvent(id, at, o.Id, o.Number, o.CustomerId, o.Total, Order.Currency, o.Customer.Phone),
            OrderStatus.Confirmed => new OrderConfirmedIntegrationEvent(id, at, o.Id, o.Number, o.CustomerId),
            OrderStatus.Shipped => new OrderShippedIntegrationEvent(id, at, o.Id, o.Number, o.CustomerId, o.CourierName!, o.TrackingNumber!, o.Total),
            OrderStatus.Delivered => new OrderDeliveredIntegrationEvent(id, at, o.Id, o.Number, o.CustomerId),
            OrderStatus.CashCollected => new OrderCashCollectedIntegrationEvent(id, at, o.Id, o.Number, o.CustomerId, o.CashCollectedAmount!.Value, o.Total, change.Note),
            OrderStatus.Cancelled => new OrderCancelledIntegrationEvent(id, at, o.Id, o.Number, o.CustomerId, change.Actor.Kind == ActorKind.Customer ? "Customer" : "Staff", o.CancellationReason),
            OrderStatus.Refused => new OrderRefusedIntegrationEvent(id, at, o.Id, o.Number, o.CustomerId, change.Note),
            OrderStatus.Expired => new OrderExpiredIntegrationEvent(id, at, o.Id, o.Number, o.CustomerId),
            _ => throw new ArgumentOutOfRangeException(nameof(change), change.To, null),
        };
    }
}
