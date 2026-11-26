using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PerfumeShop.Modules.Catalog.Domain.Fragrances;
using PerfumeShop.Modules.Catalog.Features.Shared;
using PerfumeShop.Modules.Catalog.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Audit;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Catalog.Features.ChangeVariantStatus;

internal sealed class ChangeVariantStatusHandler(CatalogDbContext db) : IHandler
{
    /// <summary>Requires a price.</summary>
    public Task<Result<VariantDto>> ActivateAsync(Guid fragranceId, Guid variantId, CancellationToken cancellationToken) =>
        ChangeAsync(fragranceId, variantId, f => f.ActivateVariant(variantId), cancellationToken);

    /// <summary>Refused for the last active variant of a published fragrance.</summary>
    public Task<Result<VariantDto>> DeactivateAsync(Guid fragranceId, Guid variantId, CancellationToken cancellationToken) =>
        ChangeAsync(fragranceId, variantId, f => f.DeactivateVariant(variantId), cancellationToken);

    private async Task<Result<VariantDto>> ChangeAsync(Guid fragranceId, Guid variantId, Func<Fragrance, Result> change, CancellationToken cancellationToken)
    {
        var loaded = await db.LoadAsync(fragranceId, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var result = change(loaded.Value);
        if (result.IsFailure)
        {
            return result.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return loaded.Value.Variants.First(v => v.Id == variantId).ToDto();
    }
}

internal static class ChangeVariantStatusEndpoint
{
    public static void Map(RouteGroupBuilder admin)
    {
        admin.MapPost("/fragrances/{id:guid}/variants/{variantId:guid}/activate",
                async (Guid id, Guid variantId, ChangeVariantStatusHandler handler, CancellationToken ct) =>
                    (await handler.ActivateAsync(id, variantId, ct)).ToOk())
            .Audited("Catalog.Variant.Activated", "Variant", "Varianti u aktivizua")
            .WithName("ActivateVariant");

        admin.MapPost("/fragrances/{id:guid}/variants/{variantId:guid}/deactivate",
                async (Guid id, Guid variantId, ChangeVariantStatusHandler handler, CancellationToken ct) =>
                    (await handler.DeactivateAsync(id, variantId, ct)).ToOk())
            .Audited("Catalog.Variant.Deactivated", "Variant", "Varianti u çaktivizua")
            .WithName("DeactivateVariant");
    }
}
