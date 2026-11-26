using System.Text.Json.Serialization;
using PerfumeShop.Modules.Catalog.Domain.Common;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Catalog.Domain.Fragrances;

/// <summary>
/// "Smells like" reference, e.g. Lattafa Khamrah → Kilian Angels' Share.
/// <see cref="FragranceId"/> is set when the referenced fragrance is also in our catalog.
/// </summary>
internal sealed record FragranceReference
{
    [JsonConstructor]
    private FragranceReference(string brandName, string name, Guid? fragranceId)
    {
        BrandName = brandName;
        Name = name;
        FragranceId = fragranceId;
    }

    public string BrandName { get; }

    public string Name { get; }

    public Guid? FragranceId { get; }

    public static Result<FragranceReference> Create(string? brandName, string? name, Guid? fragranceId = null)
    {
        brandName = brandName?.Trim();
        name = name?.Trim();

        if (string.IsNullOrEmpty(brandName) || brandName.Length > 100
            || string.IsNullOrEmpty(name) || name.Length > Fragrance.NameMaxLength
            || fragranceId == Guid.Empty)
        {
            return Result<FragranceReference>.Failure(ScentErrors.ReferenceInvalid);
        }

        return Result<FragranceReference>.Success(new FragranceReference(brandName, name, fragranceId));
    }
}
