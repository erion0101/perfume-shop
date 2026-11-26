using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PerfumeShop.Shared.Infrastructure.Jobs;

namespace PerfumeShop.Shared.Infrastructure.Outbox;

/// <summary>One module's outbox dispatcher; every registered one can be run without knowing the module.</summary>
public interface IOutboxProcessor : IRecurringJob
{
    /// <returns>Number of messages delivered.</returns>
    Task<int> ProcessAsync(CancellationToken cancellationToken);
}

public static class OutboxRegistration
{
    /// <summary>Registers <see cref="OutboxProcessor{TContext}"/> for a module DbContext that maps an outbox.</summary>
    public static IServiceCollection AddOutbox<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.AddScoped<OutboxProcessor<TContext>>();
        services.AddScoped<IOutboxProcessor>(sp => sp.GetRequiredService<OutboxProcessor<TContext>>());
        return services;
    }
}
