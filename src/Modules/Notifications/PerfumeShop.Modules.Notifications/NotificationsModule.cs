using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using PerfumeShop.Modules.Notifications.Contracts;
using PerfumeShop.Modules.Notifications.Domain;
using PerfumeShop.Modules.Notifications.Features;
using PerfumeShop.Modules.Notifications.Infrastructure;
using PerfumeShop.Modules.Notifications.Infrastructure.Email;
using PerfumeShop.Modules.Notifications.Infrastructure.Sms;
using PerfumeShop.Modules.Orders.Contracts;
using PerfumeShop.Modules.Payments.Contracts;
using PerfumeShop.Modules.Shipping.Contracts;
using PerfumeShop.Shared.Infrastructure.Auth;
using PerfumeShop.Shared.Infrastructure.Events;
using PerfumeShop.Shared.Infrastructure.Jobs;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Infrastructure.Outbox;
using PerfumeShop.Shared.Infrastructure.Persistence;

namespace PerfumeShop.Modules.Notifications;

public sealed class NotificationsModule : IModule
{
    public const string DispatchJobId = "notifications-dispatch";
    public const string LowStockJobId = "notifications-low-stock";
    public const string UnpaidCodJobId = "notifications-unpaid-cod";

    public string Name => NotificationsDbContext.SchemaName;

    public void Register(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddModuleDbContext<NotificationsDbContext>(configuration, NotificationsDbContext.SchemaName);
        services.AddOptions<NotificationOptions>()
            .BindConfiguration(NotificationOptions.SectionName)
            .Validate(o => o.IsValid(), "Notifications settings are invalid (time zone, quiet hours HH:mm, batch size 1-500, shop name).")
            .ValidateOnStart();
        services.AddScoped(sp => sp.GetRequiredService<IOptions<NotificationOptions>>().Value.ToQuietHours());

        // The console senders print OTP codes and emails, so they must never exist outside Development.
        if (environment.IsDevelopment())
        {
            services.AddSingleton<ISmsSender, ConsoleSmsSender>();
            services.AddSingleton<IEmailSender, ConsoleEmailSender>();
        }
        else
        {
            services.AddSingleton<ISmsSender, UnconfiguredSmsSender>();
            services.AddSingleton<IEmailSender, UnconfiguredEmailSender>();
        }

        services.AddScoped<NotificationQueue>();
        services.AddScoped<NotificationDispatcher>();
        services.AddScoped<LowStockAlertJob>();
        services.AddScoped<UnpaidDeliveriesAlertJob>();
        services.AddScoped<INotificationsApi, NotificationsApi>();

        services.AddScoped<NotificationEventHandlers>();
        services.AddScoped<IIntegrationEventHandler<OrderPlacedIntegrationEvent>>(sp => sp.GetRequiredService<NotificationEventHandlers>());
        services.AddScoped<IIntegrationEventHandler<OrderConfirmedIntegrationEvent>>(sp => sp.GetRequiredService<NotificationEventHandlers>());
        services.AddScoped<IIntegrationEventHandler<OrderCancelledIntegrationEvent>>(sp => sp.GetRequiredService<NotificationEventHandlers>());
        services.AddScoped<IIntegrationEventHandler<OrderExpiredIntegrationEvent>>(sp => sp.GetRequiredService<NotificationEventHandlers>());
        services.AddScoped<IIntegrationEventHandler<ShipmentHandedOverIntegrationEvent>>(sp => sp.GetRequiredService<NotificationEventHandlers>());
        services.AddScoped<IIntegrationEventHandler<ShipmentDeliveredIntegrationEvent>>(sp => sp.GetRequiredService<NotificationEventHandlers>());
        services.AddScoped<IIntegrationEventHandler<FiscalReceiptFailedIntegrationEvent>>(sp => sp.GetRequiredService<NotificationEventHandlers>());
        services.AddScoped<IJobFailureListener>(sp => sp.GetRequiredService<NotificationEventHandlers>());
        services.AddScoped<IDeadLetterListener>(sp => sp.GetRequiredService<NotificationEventHandlers>());

        services.AddHandlersFromAssembly(typeof(NotificationsModule).Assembly);
        services.AddValidatorsFromAssembly(typeof(NotificationsModule).Assembly, includeInternalTypes: true);
    }

    public async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await services.MigrateIfEnabledAsync<NotificationsDbContext>(cancellationToken);
        await SeedAsync(services, cancellationToken);

        var configuration = services.GetRequiredService<IConfiguration>();
        var jobs = services.GetRequiredService<IRecurringJobRegistry>();
        jobs.AddOrUpdate<NotificationDispatcher>(DispatchJobId, configuration["Notifications:DispatchCron"] ?? "*/30 * * * * *");
        // Daily alerts at 08:00 / 08:05 Kosovo time all year (the cron is read in the shop's time zone, DST included).
        var local = TimeZoneInfo.FindSystemTimeZoneById(services.GetRequiredService<IOptions<NotificationOptions>>().Value.TimeZone);
        jobs.AddOrUpdate<LowStockAlertJob>(LowStockJobId, configuration["Notifications:LowStockCron"] ?? "0 0 8 * * *", local);
        jobs.AddOrUpdate<UnpaidDeliveriesAlertJob>(UnpaidCodJobId, configuration["Notifications:UnpaidCodCron"] ?? "0 5 8 * * *", local);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var staff = endpoints.MapGroup("/api/admin/notifications").RequireAuthorization(Policies.Staff).WithTags("Notifications (staff)");
        TemplatesEndpoint.Map(staff);
        MessagesEndpoint.Map(staff);
        StaffAlertsEndpoint.Map(staff);
    }

    /// <summary>Missing templates (new keys) and the alert settings row; existing (edited) templates are never overwritten.</summary>
    private static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var existing = await db.Templates.Select(t => t.Key).ToListAsync(cancellationToken);
        foreach (var definition in TemplateCatalog.All.Where(d => !existing.Contains(d.Key)))
        {
            db.Templates.Add(NotificationTemplate.FromDefinition(definition));
        }

        if (!await db.StaffAlertSettings.AnyAsync(cancellationToken))
        {
            db.StaffAlertSettings.Add(new StaffAlertSettings());
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // another instance seeded at the same time
        }
    }
}
