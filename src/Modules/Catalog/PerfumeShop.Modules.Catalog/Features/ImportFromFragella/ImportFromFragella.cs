using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Catalog.Domain.Brands;
using PerfumeShop.Modules.Catalog.Domain.Common;
using PerfumeShop.Modules.Catalog.Domain.Fragrances;
using PerfumeShop.Modules.Catalog.Features.Shared;
using PerfumeShop.Modules.Catalog.Infrastructure;
using PerfumeShop.Modules.Catalog.Infrastructure.Fragella;
using PerfumeShop.Shared.Infrastructure.Audit;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Catalog.Features.ImportFromFragella;

/// <param name="FragellaId">The <c>fragellaId</c> of a search result.</param>
/// <param name="Concentration">Overrides Fragella's OilType (required when Fragella has none).</param>
/// <param name="Gender">Overrides Fragella's gender.</param>
/// <param name="BrandId">Attach to this existing brand instead of matching/creating one by name.</param>
internal sealed record ImportFromFragellaRequest(string FragellaId, Concentration? Concentration, Gender? Gender, Guid? BrandId);

internal sealed class ImportFromFragellaValidator : AbstractValidator<ImportFromFragellaRequest>
{
    public ImportFromFragellaValidator()
    {
        RuleFor(x => x.FragellaId).NotEmpty().MaximumLength(Fragrance.FragellaIdMaxLength);
        RuleFor(x => x.Concentration).IsInEnum();
        RuleFor(x => x.Gender).IsInEnum();
    }
}

/// <summary>
/// Creates a DRAFT fragrance from a stored Fragella search result. Costs no API call.
/// No variants are created: the admin adds sizes, prices, EANs and photos afterwards.
/// </summary>
internal sealed class ImportFromFragellaHandler(CatalogDbContext db, TimeProvider timeProvider) : IHandler
{
    public async Task<Result<FragranceAdminDto>> HandleAsync(ImportFromFragellaRequest request, CancellationToken cancellationToken)
    {
        var fragellaId = request.FragellaId.Trim();

        var existingId = await db.Fragrances.Where(f => f.FragellaId == fragellaId).Select(f => (Guid?)f.Id).FirstOrDefaultAsync(cancellationToken);
        if (existingId is { } id)
        {
            return CatalogFeatureErrors.AlreadyImported(id);
        }

        var snapshot = await db.FragellaFragrances.AsNoTracking().FirstOrDefaultAsync(s => s.FragellaId == fragellaId, cancellationToken);
        if (snapshot is null)
        {
            return CatalogFeatureErrors.FragellaNotSearched;
        }

        var data = FragellaMapper.Map(snapshot.Raw);
        if (data is null)
        {
            return CatalogFeatureErrors.FragellaPayloadInvalid;
        }

        var concentration = request.Concentration ?? data.Concentration;
        if (concentration is null)
        {
            return CatalogFeatureErrors.ConcentrationUnknown;
        }

        var gender = request.Gender ?? data.Gender;
        if (gender is null)
        {
            return CatalogFeatureErrors.GenderUnknown;
        }

        var brand = await ResolveBrandAsync(request.BrandId, data, cancellationToken);
        if (brand.IsFailure)
        {
            return brand.Error;
        }

        var name = data.Name.Trim();
        if (await db.Fragrances.AnyAsync(
                f => f.BrandId == brand.Value.Id && f.Name == name && f.Concentration == concentration.Value,
                cancellationToken))
        {
            return CatalogFeatureErrors.FragranceExists;
        }

        var slug = await SlugResolver.ResolveAsync(
            null,
            () => Fragrance.SuggestSlug(brand.Value.Name, name, concentration.Value),
            (s, ct) => db.FragranceSlugTakenAsync(s, null, ct),
            cancellationToken);
        if (slug.IsFailure)
        {
            return slug.Error;
        }

        var created = Fragrance.Create(brand.Value.Id, name, slug.Value, concentration.Value, gender.Value);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var fragrance = created.Value;
        fragrance.SetNotes(data.Notes);
        Result[] steps =
        [
            fragrance.UpdateDetails(null, data.Year, data.Perfumers, description: null),
            fragrance.UpdateProfile(data.Longevity, data.Sillage, data.Seasons, data.Occasions),
            fragrance.SetAccords(data.Accords),
            fragrance.LinkToFragella(data.FragellaId, snapshot.Raw, timeProvider.GetUtcNow().UtcDateTime),
        ];
        var failed = Array.Find(steps, s => s.IsFailure);
        if (failed is not null)
        {
            return failed.Error;
        }

        db.Fragrances.Add(fragrance);
        await db.SaveChangesAsync(cancellationToken);
        return fragrance.ToAdminDto(brand.Value);
    }

    private async Task<Result<Brand>> ResolveBrandAsync(Guid? brandId, FragellaFragranceData data, CancellationToken cancellationToken)
    {
        if (brandId is { } explicitId)
        {
            var chosen = await db.Brands.FirstOrDefaultAsync(b => b.Id == explicitId, cancellationToken);
            return chosen is null ? CatalogFeatureErrors.BrandNotFound : chosen;
        }

        if (await db.FindBrandByNameAsync(data.Brand, cancellationToken) is { } existing)
        {
            return existing;
        }

        var slug = await SlugResolver.ResolveAsync(null, () => Slug.Create(data.Brand), (s, ct) => db.BrandSlugTakenAsync(s, null, ct), cancellationToken);
        if (slug.IsFailure)
        {
            return slug.Error;
        }

        var brand = Brand.Create(data.Brand, slug.Value);
        if (brand.IsFailure)
        {
            return brand.Error;
        }

        brand.Value.UpdateDetails(data.CountryCode, logoUrl: null, description: null);
        db.Brands.Add(brand.Value);
        return brand;
    }
}

internal static class ImportFromFragellaEndpoint
{
    public static void Map(RouteGroupBuilder admin) =>
        admin.MapPost("/fragrances/import", async (ImportFromFragellaRequest request, ImportFromFragellaHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(request, ct)).ToCreated(f => $"/api/admin/catalog/fragrances/{f.Id}"))
            .WithValidation<ImportFromFragellaRequest>()
            .Audited("Catalog.Fragrance.Imported", "Fragrance", "Import nga Fragella")
            .WithName("ImportFromFragella")
            .Produces<FragranceAdminDto>(StatusCodes.Status201Created);
}
