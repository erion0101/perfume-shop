using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PerfumeShop.Modules.Orders.Contracts;
using PerfumeShop.Modules.Payments.Contracts;
using PerfumeShop.Modules.Payments.Features.Payments;
using PerfumeShop.Modules.Payments.Features.Receipts;
using PerfumeShop.Modules.Payments.Infrastructure;
using PerfumeShop.Modules.Payments.Infrastructure.Fiscal;
using PerfumeShop.Modules.Payments.Infrastructure.Providers;
using PerfumeShop.Modules.Shipping.Contracts;
using PerfumeShop.Shared.Infrastructure.Auth;
using PerfumeShop.Shared.Infrastructure.Events;
using PerfumeShop.Shared.Infrastructure.Jobs;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Infrastructure.Outbox;
using PerfumeShop.Shared.Infrastructure.Persistence;

namespace PerfumeShop.Modules.Payments;

public sealed class PaymentsModule : IModule
{
    public const string OutboxJobId = "payments-outbox";

    public string Name => PaymentsDbContext.SchemaName;

    public void Register(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddModuleDbContext<PaymentsDbContext>(configuration, PaymentsDbContext.SchemaName);
        services.AddOptions<BusinessInfo>()
            .BindConfiguration(BusinessInfo.SectionName)
            .Validate(b => TimeZoneInfo.TryFindSystemTimeZoneById(b.TimeZone, out _), "Payments:Business:TimeZone is not a known time zone.")
            .ValidateOnStart();

        // Cash on Delivery is the only payment method in v1.
        services.AddSingleton<IPaymentProvider, CashOnDeliveryProvider>();

        // The development provider writes NON-FISCAL receipts, so it must never exist outside Development.
        if (environment.IsDevelopment())
        {
            services.AddScoped<IFiscalReceiptService, DevelopmentFiscalReceiptService>();
        }
        else
        {
            services.AddScoped<IFiscalReceiptService, UnconfiguredFiscalReceiptService>();
        }

        services.AddScoped<BusinessDetails>();
        services.AddScoped<ReceiptNumbers>();
        services.AddScoped<ReceiptIssuer>();
        services.AddScoped<IPaymentsApi, PaymentsApi>();
        services.AddOutbox<PaymentsDbContext>();

        services.AddScoped<PaymentEventHandlers>();
        services.AddScoped<IIntegrationEventHandler<OrderPlacedIntegrationEvent>>(sp => sp.GetRequiredService<PaymentEventHandlers>());
        services.AddScoped<IIntegrationEventHandler<OrderShippedIntegrationEvent>>(sp => sp.GetRequiredService<PaymentEventHandlers>());
        services.AddScoped<IIntegrationEventHandler<OrderCashCollectedIntegrationEvent>>(sp => sp.GetRequiredService<PaymentEventHandlers>());
        services.AddScoped<IIntegrationEventHandler<OrderCancelledIntegrationEvent>>(sp => sp.GetRequiredService<PaymentEventHandlers>());
        services.AddScoped<IIntegrationEventHandler<OrderExpiredIntegrationEvent>>(sp => sp.GetRequiredService<PaymentEventHandlers>());
        services.AddScoped<IIntegrationEventHandler<OrderRefusedIntegrationEvent>>(sp => sp.GetRequiredService<PaymentEventHandlers>());
        services.AddScoped<IIntegrationEventHandler<CodSettledIntegrationEvent>>(sp => sp.GetRequiredService<PaymentEventHandlers>());

        services.AddHandlersFromAssembly(typeof(PaymentsModule).Assembly);
        services.AddValidatorsFromAssembly(typeof(PaymentsModule).Assembly, includeInternalTypes: true);
    }

    public async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await services.MigrateIfEnabledAsync<PaymentsDbContext>(cancellationToken);
        var configuration = services.GetRequiredService<IConfiguration>();
        services.GetRequiredService<IRecurringJobRegistry>()
            .AddOrUpdate<OutboxProcessor<PaymentsDbContext>>(OutboxJobId, configuration["Payments:OutboxCron"] ?? "*/30 * * * * *");
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var staff = endpoints.MapGroup("/api/admin/payments").RequireAuthorization(Policies.Staff).WithTags("Payments (staff)");
        PaymentsEndpoint.Map(staff);
        ReceiptsEndpoint.Map(staff);
    }
}
