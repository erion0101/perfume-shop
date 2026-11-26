using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PerfumeShop.Modules.Catalog.Domain.Brands;
using PerfumeShop.Modules.Catalog.Domain.Common;
using PerfumeShop.Modules.Catalog.Features.Shared;
using PerfumeShop.Modules.Catalog.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Catalog.Features.CreateBrand;

/// <param name="Slug">Optional. Generated from the name when empty.</param>
internal sealed record CreateBrandRequest(string Name, string? Slug, string? CountryCode, string? LogoUrl, string? Description);

internal sealed class CreateBrandValidator : AbstractValidator<CreateBrandRequest>
{
    public CreateBrandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Brand.NameMaxLength);
        RuleFor(x => x.Slug).MaximumLength(Slug.MaxLength);
        RuleFor(x => x.CountryCode).Length(2).When(x => !string.IsNullOrWhiteSpace(x.CountryCode));
        RuleFor(x => x.LogoUrl).MaximumLength(HttpUrl.MaxLength);
    }
}

internal sealed class CreateBrandHandler(CatalogDbContext db) : IHandler
{
    public async Task<Result<BrandDto>> HandleAsync(CreateBrandRequest request, CancellationToken cancellationToken)
    {
        if (await db.FindBrandByNameAsync(request.Name, cancellationToken) is not null)
        {
            return CatalogFeatureErrors.BrandNameTaken;
        }

        var slug = await SlugResolver.ResolveAsync(
            request.Slug,
            () => Slug.Create(request.Name),
            (s, ct) => db.BrandSlugTakenAsync(s, null, ct),
            cancellationToken);
        if (slug.IsFailure)
        {
            return slug.Error;
        }

        var brand = Brand.Create(request.Name, slug.Value);
        if (brand.IsFailure)
        {
            return brand.Error;
        }

        var details = brand.Value.UpdateDetails(request.CountryCode, request.LogoUrl, request.Description);
        if (details.IsFailure)
        {
            return details.Error;
        }

        db.Brands.Add(brand.Value);
        await db.SaveChangesAsync(cancellationToken);
        return brand.Value.ToDto(0);
    }
}

internal static class CreateBrandEndpoint
{
    public static void Map(RouteGroupBuilder admin) =>
        admin.MapPost("/brands", async (CreateBrandRequest request, CreateBrandHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(request, ct)).ToCreated(b => $"/api/admin/catalog/brands/{b.Id}"))
            .WithValidation<CreateBrandRequest>()
            .WithName("CreateBrand")
            .Produces<BrandDto>(StatusCodes.Status201Created);
}
