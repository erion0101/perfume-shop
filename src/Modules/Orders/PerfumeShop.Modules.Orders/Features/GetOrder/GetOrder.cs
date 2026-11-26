using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PerfumeShop.Modules.Orders.Features.Shared;
using PerfumeShop.Modules.Orders.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Orders.Features.GetOrder;

internal sealed class GetOrderHandler(OrdersDbContext db) : IHandler
{
    public async Task<Result<OrderAdminDto>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var order = await db.LoadOrderAsync(id, cancellationToken);
        return order.IsSuccess ? order.Value.ToAdminDto() : order.Error;
    }
}

internal static class GetOrderEndpoint
{
    public static void Map(RouteGroupBuilder staff) =>
        staff.MapGet("/{id:guid}", async (Guid id, GetOrderHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(id, ct)).ToOk())
            .WithName("GetOrder");
}
