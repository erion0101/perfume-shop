using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PerfumeShop.Modules.Orders.Contracts;
using PerfumeShop.Modules.Orders.Features.AddOrderNote;
using PerfumeShop.Modules.Orders.Features.ChangeOrderStatus;
using PerfumeShop.Modules.Orders.Features.GetOrder;
using PerfumeShop.Modules.Orders.Features.ListOrders;
using PerfumeShop.Modules.Orders.Features.MyOrders;
using PerfumeShop.Modules.Orders.Features.OrderSettings;
using PerfumeShop.Modules.Orders.Features.PlaceOrder;
using PerfumeShop.Modules.Orders.Features.QuoteCart;
using PerfumeShop.Modules.Orders.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Auth;
using PerfumeShop.Shared.Infrastructure.Jobs;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Infrastructure.Outbox;
using PerfumeShop.Shared.Infrastructure.Persistence;

namespace PerfumeShop.Modules.Orders;

public sealed class OrdersModule : IModule
{
    public const string OutboxJobId = "orders-outbox";
    public const string ExpiryJobId = "orders-expire-pending";

    public string Name => OrdersDbContext.SchemaName;

    public void Register(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddModuleDbContext<OrdersDbContext>(configuration, OrdersDbContext.SchemaName);

        services.AddScoped<OrderPricing>();
        services.AddScoped<OrderNumbers>();
        services.AddScoped<OrderTransitions>();
        services.AddScoped<OrderExpiryJob>();
        services.AddOutbox<OrdersDbContext>();
        services.AddScoped<IOrdersApi, OrdersApi>();

        services.AddHandlersFromAssembly(typeof(OrdersModule).Assembly);
        services.AddValidatorsFromAssembly(typeof(OrdersModule).Assembly, includeInternalTypes: true);
    }

    public async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await services.MigrateIfEnabledAsync<OrdersDbContext>(cancellationToken);

        var configuration = services.GetRequiredService<IConfiguration>();
        var jobs = services.GetRequiredService<IRecurringJobRegistry>();
        jobs.AddOrUpdate<OutboxProcessor<OrdersDbContext>>(OutboxJobId, configuration["Orders:OutboxCron"] ?? "*/30 * * * * *");
        jobs.AddOrUpdate<OrderExpiryJob>(ExpiryJobId, configuration["Orders:ExpiryCron"] ?? "*/5 * * * *");
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        QuoteCartEndpoint.Map(endpoints);

        var customer = endpoints.MapGroup("/api/orders").RequireAuthorization(Policies.Customer).WithTags("My orders");
        PlaceOrderEndpoint.Map(customer);
        MyOrdersEndpoint.Map(customer);

        var staff = endpoints.MapGroup("/api/admin/orders").RequireAuthorization(Policies.Staff).WithTags("Orders (staff)");
        ListOrdersEndpoint.Map(staff);
        PerfumeShop.Modules.Orders.Features.CustomerHistory.CustomerHistoryEndpoint.Map(staff);
        GetOrderEndpoint.Map(staff);
        ChangeOrderStatusEndpoint.Map(staff);
        AddOrderNoteEndpoint.Map(staff);

        var admin = endpoints.MapGroup("/api/admin/orders").RequireAuthorization(Policies.Admin).WithTags("Orders (admin settings)");
        OrderSettingsEndpoint.Map(admin);
    }
}
