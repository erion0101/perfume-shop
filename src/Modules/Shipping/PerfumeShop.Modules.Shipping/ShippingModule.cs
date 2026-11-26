using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PerfumeShop.Modules.Shipping.Contracts;
using PerfumeShop.Modules.Shipping.Features.CourierFiles;
using PerfumeShop.Modules.Shipping.Features.Couriers;
using PerfumeShop.Modules.Shipping.Features.CreateShipment;
using PerfumeShop.Modules.Shipping.Features.Reports;
using PerfumeShop.Modules.Shipping.Features.Settlements;
using PerfumeShop.Modules.Shipping.Features.Shipments;
using PerfumeShop.Modules.Shipping.Infrastructure;
using PerfumeShop.Modules.Shipping.Infrastructure.Couriers;
using PerfumeShop.Shared.Infrastructure.Auth;
using PerfumeShop.Shared.Infrastructure.Jobs;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Infrastructure.Outbox;
using PerfumeShop.Shared.Infrastructure.Persistence;

namespace PerfumeShop.Modules.Shipping;

public sealed class ShippingModule : IModule
{
    public const string OutboxJobId = "shipping-outbox";

    public string Name => ShippingDbContext.SchemaName;

    public void Register(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddModuleDbContext<ShippingDbContext>(configuration, ShippingDbContext.SchemaName);
        services.AddOptions<ShopInfo>()
            .BindConfiguration(ShopInfo.SectionName)
            .Validate(s => TimeZoneInfo.TryFindSystemTimeZoneById(s.TimeZone, out _), "Shipping:Shop:TimeZone is not a known time zone.")
            .ValidateOnStart();

        // Only manual/CSV couriers exist today; an API-based courier would add an adapter and be chosen by Courier.AdapterCode.
        services.AddSingleton<ICourierAdapter, ManualCourierAdapter>();
        services.AddScoped<ShipmentWorkflow>();
        services.AddOutbox<ShippingDbContext>();
        services.AddScoped<IShippingApi, ShippingApi>();
        services.AddScoped<ShopDetails>();

        services.AddHandlersFromAssembly(typeof(ShippingModule).Assembly);
        services.AddValidatorsFromAssembly(typeof(ShippingModule).Assembly, includeInternalTypes: true);
    }

    public async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await services.MigrateIfEnabledAsync<ShippingDbContext>(cancellationToken);
        var configuration = services.GetRequiredService<IConfiguration>();
        services.GetRequiredService<IRecurringJobRegistry>()
            .AddOrUpdate<OutboxProcessor<ShippingDbContext>>(OutboxJobId, configuration["Shipping:OutboxCron"] ?? "*/30 * * * * *");
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var staff = endpoints.MapGroup("/api/admin/shipping").RequireAuthorization(Policies.Staff).WithTags("Shipping (staff)");
        CouriersEndpoint.MapRead(staff);
        CreateShipmentEndpoint.Map(staff);
        ShipmentsEndpoint.Map(staff);
        CourierFilesEndpoint.Map(staff);
        SettlementsEndpoint.Map(staff);
        ReportsEndpoint.Map(staff);

        var admin = endpoints.MapGroup("/api/admin/shipping").RequireAuthorization(Policies.Admin).WithTags("Shipping (admin)");
        CouriersEndpoint.MapWrite(admin);
    }
}
