using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Shared.Infrastructure.Events;

/// <summary>
/// Delivers integration events to their handlers. Business code does not call this directly: it
/// writes events to its module's outbox (<c>db.AddIntegrationEvent</c>) and the outbox job publishes them.
/// </summary>
public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent;

    Task PublishAsync(IIntegrationEvent integrationEvent, Type eventType, CancellationToken cancellationToken);
}
