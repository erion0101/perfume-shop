using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Catalog.Domain.Brands;
using PerfumeShop.Modules.Catalog.Domain.Common;
using PerfumeShop.Modules.Catalog.Features.Shared;
using PerfumeShop.Modules.Catalog.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Audit;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Catalog.Features.UpdateBrand;

/// <param name="Slug">Optional. When empty the current slug is kept, so existing URLs keep working.</param>
internal sealed record UpdateBrandRequest(string Name, string? Slug, string? CountryCode, string? LogoUrl, string? Description);

internal sealed class UpdateBrandValidator : AbstractValidator<UpdateBrandRequest>
{
    public UpdateBrandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Brand.NameMaxLength);
        RuleFor(x => x.Slug).MaximumLength(Slug.MaxLength);
        RuleFor(x => x.CountryCode).Length(2).When(x => !string.IsNullOrWhiteSpace(x.CountryCode));
        RuleFor(x => x.LogoUrl).MaximumLength(HttpUrl.MaxLength);
    }
}

internal sealed class UpdateBrandHandler(CatalogDbContext db) : IHandler
{
    public async Task<Result<BrandDto>> HandleAsync(Guid id, UpdateBrandRequest request, CancellationToken cancellationToken)
    {
        var brand = await db.Brands.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (brand is null)
        {
            return CatalogFeatureErrors.BrandNotFound;
        }

        var sameName = await db.FindBrandByNameAsync(request.Name, cancellationToken);
        if (sameName is not null && sameName.Id != id)
        {
            return CatalogFeatureErrors.BrandNameTaken;
        }

        var slug = string.IsNullOrWhiteSpace(request.Slug)
            ? Result<Slug>.Success(brand.Slug)
            : await SlugResolver.ResolveAsync(request.Slug, () => brand.Slug, (s, ct) => db.BrandSlugTakenAsync(s, id, ct), cancellationToken);
        if (slug.IsFailure)
        {
            return slug.Error;
        }

        var renamed = brand.Rename(request.Name, slug.Value);
        if (renamed.IsFailure)
        {
            return renamed.Error;
        }

        var details = brand.UpdateDetails(request.CountryCode, request.LogoUrl, request.Description);
        if (details.IsFailure)
        {
            return details.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        var count = await db.Fragrances.CountAsync(f => f.BrandId == id, cancellationToken);
        return brand.ToDto(count);
    }
}

internal static class UpdateBrandEndpoint
{
    public static void Map(RouteGroupBuilder admin) =>
        admin.MapPut("/brands/{id:guid}", async (Guid id, UpdateBrandRequest request, UpdateBrandHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(id, request, ct)).ToOk())
            .WithValidation<UpdateBrandRequest>()
            .Audited("Catalog.Brand.Updated", "Brand", "Marka u ndryshua")
            .WithName("UpdateBrand");
}
