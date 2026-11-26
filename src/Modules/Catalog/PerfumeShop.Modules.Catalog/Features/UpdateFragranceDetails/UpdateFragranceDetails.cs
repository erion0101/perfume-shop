using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Catalog.Domain.Common;
using PerfumeShop.Modules.Catalog.Domain.Fragrances;
using PerfumeShop.Modules.Catalog.Features.Shared;
using PerfumeShop.Modules.Catalog.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Catalog.Features.UpdateFragranceDetails;

/// <summary>
/// Full replacement of the editable profile: Albanian description and manual corrections of
/// imported data. Lists that are omitted become empty. <c>Slug</c> empty = keep the current one.
/// </summary>
internal sealed record UpdateFragranceDetailsRequest(
    string Name,
    string? Slug,
    Concentration Concentration,
    Gender Gender,
    OlfactiveFamily? OlfactiveFamily,
    int? LaunchYear,
    List<string>? Perfumers,
    string? Description,
    Longevity? Longevity,
    Sillage? Sillage,
    List<Season>? Seasons,
    List<Occasion>? Occasions,
    NotePyramidInput? Notes,
    List<AccordInput>? Accords,
    List<SimilarFragranceInput>? SimilarTo);

internal sealed class UpdateFragranceDetailsValidator : AbstractValidator<UpdateFragranceDetailsRequest>
{
    public UpdateFragranceDetailsValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Fragrance.NameMaxLength);
        RuleFor(x => x.Slug).MaximumLength(Slug.MaxLength);
        RuleFor(x => x.Concentration).IsInEnum();
        RuleFor(x => x.Gender).IsInEnum();
        RuleFor(x => x.OlfactiveFamily).IsInEnum();
        RuleFor(x => x.Longevity).IsInEnum();
        RuleFor(x => x.Sillage).IsInEnum();
        RuleFor(x => x.Description).MaximumLength(10_000);
        RuleFor(x => x.Perfumers).Must(p => p is null || p.Count <= Fragrance.MaxPerfumers);
        RuleForEach(x => x.Perfumers).NotEmpty().MaximumLength(Fragrance.PerfumerMaxLength);
        RuleFor(x => x.Accords).Must(a => a is null || a.Count <= Fragrance.MaxAccords);
        RuleForEach(x => x.Accords).ChildRules(a =>
        {
            a.RuleFor(x => x.Name).NotEmpty().MaximumLength(Accord.NameMaxLength);
            a.RuleFor(x => x.Percentage).InclusiveBetween(1, 100);
        });
        RuleFor(x => x.SimilarTo).Must(s => s is null || s.Count <= Fragrance.MaxSimilarTo);
    }
}

internal sealed class UpdateFragranceDetailsHandler(CatalogDbContext db) : IHandler
{
    public async Task<Result<FragranceAdminDto>> HandleAsync(Guid id, UpdateFragranceDetailsRequest request, CancellationToken cancellationToken)
    {
        var loaded = await db.LoadAsync(id, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var fragrance = loaded.Value;
        var name = request.Name.Trim();

        if (await db.Fragrances.AnyAsync(
                f => f.Id != id && f.BrandId == fragrance.BrandId && f.Name == name && f.Concentration == request.Concentration,
                cancellationToken))
        {
            return CatalogFeatureErrors.FragranceExists;
        }

        var slug = string.IsNullOrWhiteSpace(request.Slug)
            ? Result<Slug>.Success(fragrance.Slug)
            : await SlugResolver.ResolveAsync(request.Slug, () => fragrance.Slug, (s, ct) => db.FragranceSlugTakenAsync(s, id, ct), cancellationToken);
        if (slug.IsFailure)
        {
            return slug.Error;
        }

        var notes = CatalogInputs.ToNotePyramid(request.Notes);
        var accords = CatalogInputs.ToAccords(request.Accords);
        var similar = CatalogInputs.ToReferences(request.SimilarTo);
        foreach (var input in new Result[] { notes, accords, similar })
        {
            if (input.IsFailure)
            {
                return input.Error;
            }
        }

        var seasons = (request.Seasons ?? []).Aggregate(Season.None, (all, s) => all | s);
        var occasions = (request.Occasions ?? []).Aggregate(Occasion.None, (all, o) => all | o);

        fragrance.SetNotes(notes.Value);
        Result[] steps =
        [
            fragrance.Rename(name, slug.Value),
            fragrance.ChangeClassification(request.Concentration, request.Gender),
            fragrance.UpdateDetails(request.OlfactiveFamily, request.LaunchYear, request.Perfumers ?? [], request.Description),
            fragrance.UpdateProfile(request.Longevity, request.Sillage, seasons, occasions),
            fragrance.SetAccords(accords.Value),
            fragrance.SetSimilarTo(similar.Value),
        ];

        // Nothing is saved when a step fails; the tracked changes are dropped with the request scope.
        var failed = Array.Find(steps, s => s.IsFailure);
        if (failed is not null)
        {
            return failed.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await db.ToAdminDtoAsync(fragrance, cancellationToken);
    }
}

internal static class UpdateFragranceDetailsEndpoint
{
    public static void Map(RouteGroupBuilder admin) =>
        admin.MapPut("/fragrances/{id:guid}", async (Guid id, UpdateFragranceDetailsRequest request, UpdateFragranceDetailsHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(id, request, ct)).ToOk())
            .WithValidation<UpdateFragranceDetailsRequest>()
            .WithName("UpdateFragranceDetails");
}
