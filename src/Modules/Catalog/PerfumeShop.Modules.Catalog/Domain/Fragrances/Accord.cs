using System.Text.Json.Serialization;
using PerfumeShop.Modules.Catalog.Domain.Common;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Catalog.Domain.Fragrances;

/// <summary>Main accord with its strength, e.g. ("amber", 100), ("warm spicy", 74).</summary>
internal sealed record Accord
{
    public const int NameMaxLength = 50;

    [JsonConstructor]
    private Accord(string name, int percentage)
    {
        Name = name;
        Percentage = percentage;
    }

    public string Name { get; }

    public int Percentage { get; }

    public static Result<Accord> Create(string? name, int percentage)
    {
        name = name?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(name) || name.Length > NameMaxLength)
        {
            return Result<Accord>.Failure(ScentErrors.AccordNameInvalid);
        }

        return percentage is < 1 or > 100
            ? Result<Accord>.Failure(ScentErrors.AccordPercentageInvalid)
            : Result<Accord>.Success(new Accord(name, percentage));
    }
}
