using Microsoft.Extensions.DependencyInjection;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Shared.Infrastructure.Events;

/// <summary>
/// Delivers an event to every <see cref="IIntegrationEventHandler{TEvent}"/> registered in DI, in this
/// process. Called by the outbox processor (not directly by business code), so a failure is retried.
/// Handlers run in their own DI scope: they never share the publishing module's DbContext or transaction.
/// </summary>
internal sealed class InProcessEventBus(IServiceScopeFactory scopes) : IEventBus
{
    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent =>
        PublishAsync(integrationEvent, typeof(TEvent), cancellationToken);

    public async Task PublishAsync(IIntegrationEvent integrationEvent, Type eventType, CancellationToken cancellationToken)
    {
        var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);
        var handle = handlerType.GetMethod(nameof(IIntegrationEventHandler<IIntegrationEvent>.HandleAsync))!;

        await using var scope = scopes.CreateAsyncScope();
        foreach (var handler in scope.ServiceProvider.GetServices(handlerType))
        {
            await (Task)handle.Invoke(handler, [integrationEvent, cancellationToken])!;
        }
    }
}
