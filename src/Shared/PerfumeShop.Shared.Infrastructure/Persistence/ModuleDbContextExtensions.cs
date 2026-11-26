using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PerfumeShop.Shared.Infrastructure.Persistence;

public static class ModuleDbContextExtensions
{
    public const string ConnectionStringName = "Postgres";

    public const string MigrationsHistoryTable = "__EFMigrationsHistory";

    /// <summary>
    /// Registers a module DbContext on the shared database, with its migrations history
    /// table inside the module's own schema.
    /// </summary>
    public static IServiceCollection AddModuleDbContext<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        string schema)
        where TContext : DbContext
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddDbContext<TContext>((serviceProvider, options) =>
        {
            var connectionString = configuration.GetConnectionString(ConnectionStringName)
                ?? throw new InvalidOperationException(
                    $"Connection string '{ConnectionStringName}' is not configured.");

            options
                .UseModuleDefaults(connectionString, schema)
                .AddInterceptors(new AuditableEntityInterceptor(serviceProvider.GetRequiredService<TimeProvider>()));
        });

        return services;
    }

    /// <summary>Applies pending migrations when <c>Database:MigrateOnStartup</c> is true (Development, tests).</summary>
    public static async Task MigrateIfEnabledAsync<TContext>(this IServiceProvider services, CancellationToken cancellationToken)
        where TContext : DbContext
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        if (configuration.GetValue<bool>("Database:MigrateOnStartup"))
        {
            await services.GetRequiredService<TContext>().Database.MigrateAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Provider settings shared by runtime and tests, so the model used in tests
    /// (e.g. the pending-migrations check) is exactly the runtime model.
    /// </summary>
    public static DbContextOptionsBuilder UseModuleDefaults(
        this DbContextOptionsBuilder options,
        string connectionString,
        string schema) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, schema))
            .UseSnakeCaseNamingConvention();
}
