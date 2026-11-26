using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PerfumeShop.Shared.Infrastructure.Modules;

/// <summary>Entry point of a module. The API host calls these once at startup.</summary>
public interface IModule
{
    /// <summary>Lowercase module name, also used as the database schema (e.g. "catalog").</summary>
    string Name { get; }

    /// <summary>
    /// Registers services. Use <paramref name="environment"/> to keep development-only services
    /// (e.g. a console SMS sender) out of every other environment.
    /// </summary>
    void Register(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment);

    void MapEndpoints(IEndpointRouteBuilder endpoints);

    /// <summary>Runs before the app starts serving requests: migrations (if enabled), seed data.</summary>
    Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken) => Task.CompletedTask;
}
