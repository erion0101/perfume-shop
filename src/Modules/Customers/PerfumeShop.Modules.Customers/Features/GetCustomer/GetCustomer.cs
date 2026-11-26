using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PerfumeShop.Modules.Customers.Features.Shared;
using PerfumeShop.Modules.Customers.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Customers.Features.GetCustomer;

internal sealed class GetCustomerHandler(CustomersDbContext db) : IHandler
{
    public async Task<Result<CustomerAdminDto>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var customer = await db.LoadAsync(id, cancellationToken);
        return customer.IsSuccess ? customer.Value.ToAdminDto() : customer.Error;
    }
}

internal static class GetCustomerEndpoint
{
    public static void Map(RouteGroupBuilder admin) =>
        admin.MapGet("/{id:guid}", async (Guid id, GetCustomerHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(id, ct)).ToOk())
            .WithName("GetCustomer");
}
