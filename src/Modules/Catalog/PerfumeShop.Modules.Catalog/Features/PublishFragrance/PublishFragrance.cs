using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PerfumeShop.Modules.Catalog.Features.Shared;
using PerfumeShop.Modules.Catalog.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Catalog.Features.PublishFragrance;

internal sealed class PublishFragranceHandler(CatalogDbContext db) : IHandler
{
    /// <summary>Needs at least one active variant.</summary>
    public Task<Result<FragranceAdminDto>> PublishAsync(Guid id, CancellationToken cancellationToken) =>
        db.ChangeAsync(id, f => f.Publish(), cancellationToken);

    public Task<Result<FragranceAdminDto>> UnpublishAsync(Guid id, CancellationToken cancellationToken) =>
        db.ChangeAsync(id, f =>
        {
            f.Unpublish();
            return Result.Success();
        }, cancellationToken);
}

internal static class PublishFragranceEndpoint
{
    public static void Map(RouteGroupBuilder admin)
    {
        admin.MapPost("/fragrances/{id:guid}/publish", async (Guid id, PublishFragranceHandler handler, CancellationToken ct) =>
                (await handler.PublishAsync(id, ct)).ToOk())
            .WithName("PublishFragrance");

        admin.MapPost("/fragrances/{id:guid}/unpublish", async (Guid id, PublishFragranceHandler handler, CancellationToken ct) =>
                (await handler.UnpublishAsync(id, ct)).ToOk())
            .WithName("UnpublishFragrance");
    }
}
