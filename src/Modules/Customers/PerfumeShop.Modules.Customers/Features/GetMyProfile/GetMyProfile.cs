using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PerfumeShop.Modules.Customers.Features.Shared;
using PerfumeShop.Modules.Customers.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Auth;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Customers.Features.GetMyProfile;

internal sealed class GetMyProfileHandler(CustomersDbContext db) : IHandler
{
    public async Task<Result<CustomerProfileDto>> HandleAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var customer = await db.LoadActiveAsync(customerId, cancellationToken);
        return customer.IsSuccess ? customer.Value.ToProfileDto() : customer.Error;
    }
}

internal static class GetMyProfileEndpoint
{
    public static void Map(RouteGroupBuilder me) =>
        me.MapGet("/", async (ClaimsPrincipal user, GetMyProfileHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(user.GetCustomerId()!.Value, ct)).ToOk())
            .WithName("GetMyProfile");
}
