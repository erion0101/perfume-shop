using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PerfumeShop.Modules.Catalog.Features.Shared;
using PerfumeShop.Modules.Catalog.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Catalog.Features.GetFragranceAdmin;

internal sealed class GetFragranceAdminHandler(CatalogDbContext db) : IHandler
{
    public async Task<Result<FragranceAdminDto>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var fragrance = await db.LoadAsync(id, cancellationToken);
        return fragrance.IsFailure ? fragrance.Error : await db.ToAdminDtoAsync(fragrance.Value, cancellationToken);
    }
}

internal static class GetFragranceAdminEndpoint
{
    public static void Map(RouteGroupBuilder admin) =>
        admin.MapGet("/fragrances/{id:guid}", async (Guid id, GetFragranceAdminHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(id, ct)).ToOk())
            .WithName("GetFragranceAdmin");
}
