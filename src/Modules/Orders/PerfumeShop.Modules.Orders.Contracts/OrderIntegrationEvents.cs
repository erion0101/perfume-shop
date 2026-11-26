using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Orders.Contracts;

// Published through the Orders outbox (at-least-once): handlers must be idempotent, e.g. by event Id.

/// <summary>Common data of every order event.</summary>
public abstract record OrderIntegrationEvent(Guid Id, DateTime OccurredOnUtc, Guid OrderId, string OrderNumber, Guid CustomerId)
    : IntegrationEvent(Id, OccurredOnUtc);

public sealed record OrderPlacedIntegrationEvent(
    Guid Id, DateTime OccurredOnUtc, Guid OrderId, string OrderNumber, Guid CustomerId, decimal Total, string Currency, string CustomerPhone)
    : OrderIntegrationEvent(Id, OccurredOnUtc, OrderId, OrderNumber, CustomerId);

public sealed record OrderConfirmedIntegrationEvent(Guid Id, DateTime OccurredOnUtc, Guid OrderId, string OrderNumber, Guid CustomerId)
    : OrderIntegrationEvent(Id, OccurredOnUtc, OrderId, OrderNumber, CustomerId);

/// <summary>Payments issues the fiscal receipt on this event.</summary>
public sealed record OrderShippedIntegrationEvent(
    Guid Id, DateTime OccurredOnUtc, Guid OrderId, string OrderNumber, Guid CustomerId, string CourierName, string TrackingNumber, decimal Total)
    : OrderIntegrationEvent(Id, OccurredOnUtc, OrderId, OrderNumber, CustomerId);

public sealed record OrderDeliveredIntegrationEvent(Guid Id, DateTime OccurredOnUtc, Guid OrderId, string OrderNumber, Guid CustomerId)
    : OrderIntegrationEvent(Id, OccurredOnUtc, OrderId, OrderNumber, CustomerId);

public sealed record OrderCashCollectedIntegrationEvent(
    Guid Id, DateTime OccurredOnUtc, Guid OrderId, string OrderNumber, Guid CustomerId, decimal AmountCollected, decimal Total, string? Note)
    : OrderIntegrationEvent(Id, OccurredOnUtc, OrderId, OrderNumber, CustomerId);

/// <param name="CancelledBy">Customer or Staff.</param>
public sealed record OrderCancelledIntegrationEvent(
    Guid Id, DateTime OccurredOnUtc, Guid OrderId, string OrderNumber, Guid CustomerId, string CancelledBy, string? Reason)
    : OrderIntegrationEvent(Id, OccurredOnUtc, OrderId, OrderNumber, CustomerId);

/// <summary>Payments issues the storno receipt; Customers may count refusals.</summary>
public sealed record OrderRefusedIntegrationEvent(Guid Id, DateTime OccurredOnUtc, Guid OrderId, string OrderNumber, Guid CustomerId, string? Reason)
    : OrderIntegrationEvent(Id, OccurredOnUtc, OrderId, OrderNumber, CustomerId);

public sealed record OrderExpiredIntegrationEvent(Guid Id, DateTime OccurredOnUtc, Guid OrderId, string OrderNumber, Guid CustomerId)
    : OrderIntegrationEvent(Id, OccurredOnUtc, OrderId, OrderNumber, CustomerId);
