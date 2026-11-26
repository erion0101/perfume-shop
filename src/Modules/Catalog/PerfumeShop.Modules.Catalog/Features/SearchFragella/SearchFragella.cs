using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Catalog.Infrastructure;
using PerfumeShop.Modules.Catalog.Infrastructure.Fragella;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Catalog.Features.SearchFragella;

internal sealed record SearchFragellaQuery(string? Q);

/// <param name="ImageUrl">Fragella's picture, for recognising the result only. Shop photos come from the distributor.</param>
internal sealed record FragellaSearchItemDto(
    string FragellaId,
    string Name,
    string Brand,
    string? Year,
    string? Gender,
    string? OilType,
    string? ImageUrl,
    Guid? ImportedFragranceId);

internal sealed record SearchFragellaResponse(bool FromCache, IReadOnlyList<FragellaSearchItemDto> Items);

internal sealed class SearchFragellaValidator : AbstractValidator<SearchFragellaQuery>
{
    public SearchFragellaValidator() =>
        RuleFor(x => x.Q).NotEmpty().Must(q => q!.Trim().Length >= 3).WithMessage("Search needs at least 3 characters.").MaximumLength(80);
}

internal sealed class SearchFragellaHandler(CatalogDbContext db, FragellaSearchService fragella) : IHandler
{
    public async Task<Result<SearchFragellaResponse>> HandleAsync(SearchFragellaQuery query, CancellationToken cancellationToken)
    {
        var outcome = await fragella.SearchAsync(query.Q!, cancellationToken);
        if (outcome.IsFailure)
        {
            return outcome.Error;
        }

        var ids = outcome.Value.Items.Select(i => i.FragellaId).ToList();
        var imported = await db.Fragrances.AsNoTracking()
            .Where(f => f.FragellaId != null && ids.Contains(f.FragellaId))
            .ToDictionaryAsync(f => f.FragellaId!, f => f.Id, cancellationToken);

        return new SearchFragellaResponse(
            outcome.Value.FromCache,
            [.. outcome.Value.Items.Select(item => ToDto(item, imported.GetValueOrDefault(item.FragellaId)))]);
    }

    private static FragellaSearchItemDto ToDto(FragellaFragranceSnapshot item, Guid importedId)
    {
        using var document = JsonDocument.Parse(item.Raw);
        var root = document.RootElement;
        return new FragellaSearchItemDto(
            item.FragellaId,
            item.Name,
            item.Brand,
            FragellaJson.String(root, "Year"),
            FragellaJson.String(root, "Gender"),
            FragellaJson.String(root, "OilType"),
            FragellaJson.String(root, "Image URL"),
            importedId == Guid.Empty ? null : importedId);
    }
}

internal static class SearchFragellaEndpoint
{
    public static void Map(RouteGroupBuilder admin) =>
        admin.MapGet("/fragella/search", async ([AsParameters] SearchFragellaQuery query, SearchFragellaHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(query, ct)).ToOk())
            .WithValidation<SearchFragellaQuery>()
            .WithName("SearchFragella")
            .WithDescription("Searches Fragella (cached for 30 days in the database, counted against the monthly budget).");
}
