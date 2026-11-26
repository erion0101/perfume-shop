namespace PerfumeShop.Modules.Administration.Contracts;

/// <summary>The shop's own details, printed on receipts and packing slips and used in SMS. Edited by Admin.</summary>
public interface IBusinessProfileApi
{
    /// <summary>Cached for a few seconds; always returns a profile (seeded from configuration on first start).</summary>
    Task<BusinessProfileDto> GetAsync(CancellationToken cancellationToken = default);
}

/// <param name="TaxNumber">NUI / fiscal number of the business (Kosovo).</param>
/// <param name="PlaceholderFields">Fields still holding a placeholder ("shopName", "address", "phone", "taxNumber"); empty when everything is real.</param>
public sealed record BusinessProfileDto(
    string ShopName,
    string Address,
    string Phone,
    string? Email,
    string TaxNumber,
    IReadOnlyList<string> PlaceholderFields,
    DateTime UpdatedAtUtc,
    string? UpdatedBy)
{
    public bool HasPlaceholders => PlaceholderFields.Count > 0;
}
