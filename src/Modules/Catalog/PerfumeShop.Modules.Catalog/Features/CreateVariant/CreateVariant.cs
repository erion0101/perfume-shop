using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PerfumeShop.Modules.Catalog.Domain.Fragrances;
using PerfumeShop.Modules.Catalog.Features.Shared;
using PerfumeShop.Modules.Catalog.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Catalog.Features.CreateVariant;

/// <summary>New variants start inactive. Price is optional here but required before activation.</summary>
internal sealed record CreateVariantRequest(
    string Sku,
    VariantType Type,
    SizeInput? Size,
    string? Ean,
    decimal? Price,
    decimal? CompareAtPrice,
    List<ImageInput>? Images);

internal sealed class CreateVariantValidator : AbstractValidator<CreateVariantRequest>
{
    public CreateVariantValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(Sku.MaxLength);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Ean).MaximumLength(20);
        RuleFor(x => x.Size!.Value).GreaterThan(0).LessThanOrEqualTo(VariantSize.MaxValue).When(x => x.Size is not null);
        RuleFor(x => x.Size!.Unit).IsInEnum().When(x => x.Size is not null);
        VariantPriceRules.Apply(this, x => x.Price, x => x.CompareAtPrice, x => x.Images);
    }
}

internal static class VariantPriceRules
{
    public static void Apply<T>(
        AbstractValidator<T> validator,
        System.Linq.Expressions.Expression<Func<T, decimal?>> price,
        System.Linq.Expressions.Expression<Func<T, decimal?>> compareAt,
        System.Linq.Expressions.Expression<Func<T, List<ImageInput>?>> images)
    {
        var getPrice = price.Compile();
        var getCompareAt = compareAt.Compile();

        validator.RuleFor(price).GreaterThan(0).PrecisionScale(10, 2, ignoreTrailingZeros: true);
        validator.RuleFor(compareAt)
            .Must((request, value) => getPrice(request) is { } p && value > p)
            .When(request => getCompareAt(request) is not null)
            .WithMessage("Compare-at price needs a price and must be greater than it.");
        validator.RuleFor(images).Must(i => i is null || i.Count <= FragranceVariant.MaxImages);
    }
}

internal sealed class CreateVariantHandler(CatalogDbContext db) : IHandler
{
    public async Task<Result<VariantDto>> HandleAsync(Guid fragranceId, CreateVariantRequest request, CancellationToken cancellationToken)
    {
        var loaded = await db.LoadAsync(fragranceId, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var fragrance = loaded.Value;
        var sku = Sku.Create(request.Sku);
        var ean = CatalogInputs.ToEan(request.Ean);
        var size = CatalogInputs.ToSize(request.Size);
        var price = CatalogInputs.ToEuro(request.Price);
        var compareAt = CatalogInputs.ToEuro(request.CompareAtPrice);
        var images = CatalogInputs.ToImages(request.Images);
        foreach (var input in new Result[] { sku, ean, size, price, compareAt, images })
        {
            if (input.IsFailure)
            {
                return input.Error;
            }
        }

        var taken = await VariantCodes.CheckGloballyFreeAsync(db, sku.Value, ean.Value, exceptVariantId: null, cancellationToken);
        if (taken.IsFailure)
        {
            return taken.Error;
        }

        var variant = fragrance.AddVariant(sku.Value, request.Type, size.Value, ean.Value);
        if (variant.IsFailure)
        {
            return variant.Error;
        }

        if (price.Value is { } p)
        {
            var priced = fragrance.SetVariantPrice(variant.Value.Id, p, compareAt.Value);
            if (priced.IsFailure)
            {
                return priced.Error;
            }
        }

        var withImages = fragrance.SetVariantImages(variant.Value.Id, images.Value);
        if (withImages.IsFailure)
        {
            return withImages.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return variant.Value.ToDto();
    }
}

internal static class CreateVariantEndpoint
{
    public static void Map(RouteGroupBuilder admin) =>
        admin.MapPost("/fragrances/{id:guid}/variants", async (Guid id, CreateVariantRequest request, CreateVariantHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(id, request, ct)).ToCreated(v => $"/api/admin/catalog/fragrances/{id}/variants/{v.Id}"))
            .WithValidation<CreateVariantRequest>()
            .WithName("CreateVariant")
            .Produces<VariantDto>(StatusCodes.Status201Created);
}
