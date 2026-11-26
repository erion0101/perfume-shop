using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Catalog.Contracts;
using PerfumeShop.Modules.Catalog.Domain.Fragrances;

namespace PerfumeShop.Modules.Catalog.Infrastructure;

internal sealed class CatalogApi(CatalogDbContext db) : ICatalogApi
{
    public async Task<VariantPriceDto?> GetVariantPriceAsync(Guid variantId, CancellationToken cancellationToken = default)
    {
        var row = await (from v in db.Variants.AsNoTracking()
                         join f in db.Fragrances on v.FragranceId equals f.Id
                         where v.Id == variantId
                         select new { v.Price, v.CompareAtPrice, v.IsActive, f.Status })
            .FirstOrDefaultAsync(cancellationToken);

        return row?.Price is null
            ? null
            : new VariantPriceDto(
                variantId,
                row.Price.Amount,
                row.Price.Currency,
                row.CompareAtPrice?.Amount,
                row.IsActive && row.Status == FragranceStatus.Published);
    }

    public async Task<VariantSummaryDto?> GetVariantSummaryAsync(Guid variantId, CancellationToken cancellationToken = default) =>
        (await GetVariantSummariesAsync([variantId], cancellationToken)).GetValueOrDefault(variantId);

    public async Task<IReadOnlyDictionary<Guid, VariantSummaryDto>> GetVariantSummariesAsync(IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken = default)
    {
        var ids = variantIds.Distinct().ToList();
        var rows = await (from variant in db.Variants.AsNoTracking()
                          join f in db.Fragrances on variant.FragranceId equals f.Id
                          join b in db.Brands on f.BrandId equals b.Id
                          where ids.Contains(variant.Id)
                          select new SummaryRow(variant, f.Name, f.Slug, f.Concentration, f.Status, b.Name))
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.Variant.Id, ToSummary);
    }

    private sealed record SummaryRow(
        Domain.Fragrances.FragranceVariant Variant,
        string Name,
        Domain.Common.Slug Slug,
        Concentration Concentration,
        FragranceStatus Status,
        string BrandName);

    private static VariantSummaryDto ToSummary(SummaryRow row)
    {
        var v = row.Variant;
        return new VariantSummaryDto(
            v.Id,
            v.FragranceId,
            v.Sku.Value,
            v.Ean?.Value,
            row.BrandName,
            row.Name,
            row.Slug.Value,
            row.Concentration.ToString(),
            v.Type.ToString(),
            v.Size?.Value,
            v.Size?.Unit.ToString(),
            v.Price?.Amount,
            v.Price?.Currency,
            v.IsActive,
            v.IsActive && v.Price is not null && row.Status == FragranceStatus.Published,
            v.Images.FirstOrDefault()?.Url);
    }
}
