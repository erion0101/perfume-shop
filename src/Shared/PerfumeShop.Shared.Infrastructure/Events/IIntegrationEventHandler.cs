using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Shared.Infrastructure.Events;

/// <summary>Handles an integration event published by another module.</summary>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken = default);
}
