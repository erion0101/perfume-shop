using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PerfumeShop.Shared.Infrastructure.Events;
using PerfumeShop.Shared.Infrastructure.Persistence;

namespace PerfumeShop.Shared.Infrastructure.Jobs;

/// <summary>A job a module wants to run on a schedule (resolved from DI in its own scope).</summary>
public interface IRecurringJob
{
    Task RunAsync(CancellationToken cancellationToken);
}

/// <summary>Modules register their schedules here in <c>IModule.InitializeAsync</c>.</summary>
public interface IRecurringJobRegistry
{
    /// <param name="cron">Cron expression; may include seconds (6 fields).</param>
    /// <param name="timeZone">Time zone the cron is read in (e.g. Europe/Belgrade for "08:00 local all year"); UTC when null.</param>
    void AddOrUpdate<TJob>(string id, string cron, TimeZoneInfo? timeZone = null)
        where TJob : class, IRecurringJob;
}

/// <summary>Section "BackgroundJobs". Disabled in integration tests, which run jobs directly.</summary>
public sealed class BackgroundJobOptions
{
    public const string SectionName = "BackgroundJobs";

    public bool Enabled { get; set; } = true;

    public int WorkerCount { get; set; } = 2;

    /// <summary>How often Hangfire looks for due recurring jobs and queued work (default 15 s; the e2e tests use 1 s).</summary>
    public int PollingIntervalSeconds { get; set; } = 15;
}

internal sealed class HangfireRecurringJobRegistry(IRecurringJobManager manager) : IRecurringJobRegistry
{
    public void AddOrUpdate<TJob>(string id, string cron, TimeZoneInfo? timeZone = null)
        where TJob : class, IRecurringJob =>
        manager.AddOrUpdate<TJob>(id, job => job.RunAsync(CancellationToken.None), cron, new RecurringJobOptions { TimeZone = timeZone ?? TimeZoneInfo.Utc });
}

internal sealed class DisabledRecurringJobRegistry : IRecurringJobRegistry
{
    public void AddOrUpdate<TJob>(string id, string cron, TimeZoneInfo? timeZone = null)
        where TJob : class, IRecurringJob
    {
    }
}

public static class BackgroundJobExtensions
{
    /// <summary>Hangfire on PostgreSQL (schema "hangfire") + the in-process event bus.</summary>
    public static IServiceCollection AddPlatformBackgroundJobs(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IEventBus, InProcessEventBus>();

        var options = configuration.GetSection(BackgroundJobOptions.SectionName).Get<BackgroundJobOptions>() ?? new BackgroundJobOptions();
        if (!options.Enabled)
        {
            services.AddSingleton<IRecurringJobRegistry, DisabledRecurringJobRegistry>();
            services.AddSingleton<RecordingBackgroundJobQueue>();
            services.AddSingleton<IBackgroundJobQueue>(sp => sp.GetRequiredService<RecordingBackgroundJobQueue>());
            return services;
        }

        var connectionString = configuration.GetConnectionString(ModuleDbContextExtensions.ConnectionStringName)
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");

        services.AddHangfire((provider, hangfire) => hangfire
            .UseFilter(new JobFailureFilter(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetService<TimeProvider>() ?? TimeProvider.System,
                provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<JobFailureFilter>>()) { Order = JobFailureFilter.FilterOrder })
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(storage => storage.UseNpgsqlConnection(connectionString), new PostgreSqlStorageOptions
            {
                SchemaName = "hangfire",
                PrepareSchemaIfNecessary = true,
                QueuePollInterval = TimeSpan.FromSeconds(options.PollingIntervalSeconds),
            }));
        services.AddHangfireServer(server =>
        {
            server.WorkerCount = options.WorkerCount;
            server.SchedulePollingInterval = TimeSpan.FromSeconds(options.PollingIntervalSeconds);
        });
        services.AddSingleton<IRecurringJobRegistry, HangfireRecurringJobRegistry>();
        services.AddSingleton<IBackgroundJobQueue, HangfireBackgroundJobQueue>();
        return services;
    }

    /// <summary>Hangfire dashboard at /hangfire: Development only, local requests only (Hangfire's default).</summary>
    public static WebApplication MapPlatformJobDashboard(this WebApplication app)
    {
        var options = app.Configuration.GetSection(BackgroundJobOptions.SectionName).Get<BackgroundJobOptions>() ?? new BackgroundJobOptions();
        if (options.Enabled && app.Environment.IsDevelopment())
        {
            app.UseHangfireDashboard("/hangfire");
        }

        return app;
    }
}
