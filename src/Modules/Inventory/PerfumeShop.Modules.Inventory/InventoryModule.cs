using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PerfumeShop.Modules.Inventory.Contracts;
using PerfumeShop.Modules.Inventory.Features.AdjustStock;
using PerfumeShop.Modules.Inventory.Features.GetStockItem;
using PerfumeShop.Modules.Inventory.Features.ListStock;
using PerfumeShop.Modules.Inventory.Features.ReceiveStock;
using PerfumeShop.Modules.Inventory.Features.SetLowStockThreshold;
using PerfumeShop.Modules.Inventory.Features.TraceBatch;
using PerfumeShop.Modules.Inventory.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Auth;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Infrastructure.Persistence;

namespace PerfumeShop.Modules.Inventory;

public sealed class InventoryModule : IModule
{
    public string Name => InventoryDbContext.SchemaName;

    public void Register(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddModuleDbContext<InventoryDbContext>(configuration, InventoryDbContext.SchemaName);
        services.AddOptions<InventoryOptions>()
            .BindConfiguration(InventoryOptions.SectionName)
            .Validate(o => o.DefaultLowStockThreshold is >= 0 and <= 1000 && o.MaxConcurrencyRetries is >= 1 and <= 20, "Inventory settings are out of range.")
            .ValidateOnStart();

        services.AddScoped<StockTransactions>();
        services.AddScoped<StockReservationSweeper>();
        services.AddScoped<IInventoryApi, InventoryApi>();

        services.AddHandlersFromAssembly(typeof(InventoryModule).Assembly);
        services.AddValidatorsFromAssembly(typeof(InventoryModule).Assembly, includeInternalTypes: true);
    }

    public Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.MigrateIfEnabledAsync<InventoryDbContext>(cancellationToken);

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/admin/inventory").RequireAuthorization(Policies.Staff).WithTags("Inventory (admin)");

        ReceiveStockEndpoint.Map(admin);
        AdjustStockEndpoint.Map(admin);
        ListStockEndpoint.Map(admin);
        GetStockItemEndpoint.Map(admin);
        SetLowStockThresholdEndpoint.Map(admin);
        TraceBatchEndpoint.Map(admin);
    }
}
