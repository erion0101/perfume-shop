using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PerfumeShop.Shared.Infrastructure.Modules;

/// <summary>
/// Calls <see cref="IModule.InitializeAsync"/> for every module in the host's "starting" phase,
/// i.e. before Kestrel accepts requests, so migrations and seed data are in place first.
/// </summary>
internal sealed class ModuleInitializer(IReadOnlyList<IModule> modules, IServiceScopeFactory scopes) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        foreach (var module in modules)
        {
            await using var scope = scopes.CreateAsyncScope();
            await module.InitializeAsync(scope.ServiceProvider, cancellationToken);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public static class ModuleHostingExtensions
{
    public static IServiceCollection AddModules(
        this IServiceCollection services,
        IReadOnlyList<IModule> modules,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        IHostEnvironment environment)
    {
        foreach (var module in modules)
        {
            module.Register(services, configuration, environment);
        }

        services.AddHostedService(sp => new ModuleInitializer(modules, sp.GetRequiredService<IServiceScopeFactory>()));
        return services;
    }
}
