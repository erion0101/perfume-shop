using System.Text.Json.Serialization;
using PerfumeShop.Modules.Catalog.Domain.Common;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Catalog.Domain.Fragrances;

/// <summary>A single note, e.g. "Cinnamon". Names are kept in English as imported.</summary>
internal sealed record Note
{
    public const int NameMaxLength = 100;

    [JsonConstructor]
    private Note(string name, string? imageUrl)
    {
        Name = name;
        ImageUrl = imageUrl;
    }

    public string Name { get; }

    public string? ImageUrl { get; }

    public static Result<Note> Create(string? name, string? imageUrl = null)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > NameMaxLength)
        {
            return Result<Note>.Failure(ScentErrors.NoteNameInvalid);
        }

        imageUrl = string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl.Trim();
        if (imageUrl is not null && !HttpUrl.IsValid(imageUrl))
        {
            return Result<Note>.Failure(ScentErrors.NoteImageUrlInvalid);
        }

        return Result<Note>.Success(new Note(name, imageUrl));
    }
}
