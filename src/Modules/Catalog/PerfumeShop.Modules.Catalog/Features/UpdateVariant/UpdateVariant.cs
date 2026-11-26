using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PerfumeShop.Modules.Catalog.Domain.Fragrances;
using PerfumeShop.Modules.Catalog.Features.CreateVariant;
using PerfumeShop.Modules.Catalog.Features.Shared;
using PerfumeShop.Modules.Catalog.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Catalog.Features.UpdateVariant;

/// <summary>
/// Price: null = keep the current price (and compare-at price). Images: null = keep, [] = remove all.
/// Type and size are fixed once created; add a new variant instead.
/// </summary>
internal sealed record UpdateVariantRequest(string Sku, string? Ean, decimal? Price, decimal? CompareAtPrice, List<ImageInput>? Images);

internal sealed class UpdateVariantValidator : AbstractValidator<UpdateVariantRequest>
{
    public UpdateVariantValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(Sku.MaxLength);
        RuleFor(x => x.Ean).MaximumLength(20);
        VariantPriceRules.Apply(this, x => x.Price, x => x.CompareAtPrice, x => x.Images);
    }
}

internal sealed class UpdateVariantHandler(CatalogDbContext db) : IHandler
{
    public async Task<Result<VariantDto>> HandleAsync(Guid fragranceId, Guid variantId, UpdateVariantRequest request, CancellationToken cancellationToken)
    {
        var loaded = await db.LoadAsync(fragranceId, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var fragrance = loaded.Value;
        var sku = Sku.Create(request.Sku);
        var ean = CatalogInputs.ToEan(request.Ean);
        var price = CatalogInputs.ToEuro(request.Price);
        var compareAt = CatalogInputs.ToEuro(request.CompareAtPrice);
        var images = CatalogInputs.ToImages(request.Images);
        foreach (var input in new Result[] { sku, ean, price, compareAt, images })
        {
            if (input.IsFailure)
            {
                return input.Error;
            }
        }

        var taken = await VariantCodes.CheckGloballyFreeAsync(db, sku.Value, ean.Value, variantId, cancellationToken);
        if (taken.IsFailure)
        {
            return taken.Error;
        }

        List<Result> steps = [fragrance.ChangeVariantCodes(variantId, sku.Value, ean.Value)];
        if (price.Value is { } p)
        {
            steps.Add(fragrance.SetVariantPrice(variantId, p, compareAt.Value));
        }

        if (request.Images is not null)
        {
            steps.Add(fragrance.SetVariantImages(variantId, images.Value));
        }

        var failed = steps.Find(s => s.IsFailure);
        if (failed is not null)
        {
            return failed.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return fragrance.Variants.First(v => v.Id == variantId).ToDto();
    }
}

internal static class UpdateVariantEndpoint
{
    public static void Map(RouteGroupBuilder admin) =>
        admin.MapPut("/fragrances/{id:guid}/variants/{variantId:guid}",
                async (Guid id, Guid variantId, UpdateVariantRequest request, UpdateVariantHandler handler, CancellationToken ct) =>
                    (await handler.HandleAsync(id, variantId, request, ct)).ToOk())
            .WithValidation<UpdateVariantRequest>()
            .WithName("UpdateVariant");
}
