using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace PerfumeShop.Shared.Infrastructure.Modules;

/// <summary>Marker for a vertical-slice handler; registered as scoped by <see cref="HandlerRegistration"/>.</summary>
public interface IHandler;

public static class HandlerRegistration
{
    public static IServiceCollection AddHandlersFromAssembly(this IServiceCollection services, Assembly assembly)
    {
        var handlers = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IHandler).IsAssignableFrom(t));

        foreach (var handler in handlers)
        {
            services.AddScoped(handler);
        }

        return services;
    }
}
