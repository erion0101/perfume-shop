namespace PerfumeShop.Shared.Kernel;

/// <summary>
/// Event published by one module and consumed by others (e.g. OrderPlacedIntegrationEvent).
/// Concrete events live in the publishing module's .Contracts project. Delivery is at-least-once
/// (outbox), so handlers must be idempotent.
/// </summary>
public interface IIntegrationEvent
{
    Guid Id { get; }

    DateTime OccurredOnUtc { get; }
}

/// <summary>Convenience base record for integration events.</summary>
public abstract record IntegrationEvent(Guid Id, DateTime OccurredOnUtc) : IIntegrationEvent;
