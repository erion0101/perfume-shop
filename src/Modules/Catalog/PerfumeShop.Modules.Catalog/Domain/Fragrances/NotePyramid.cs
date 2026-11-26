using System.Text.Json.Serialization;
using PerfumeShop.Modules.Catalog.Domain.Common;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Catalog.Domain.Fragrances;

/// <summary>Top, heart and base notes. Stored as jsonb on the fragrance.</summary>
internal sealed record NotePyramid
{
    public const int MaxNotesPerLayer = 30;

    public static readonly NotePyramid Empty = new([], [], []);

    [JsonConstructor]
    private NotePyramid(IReadOnlyList<Note> top, IReadOnlyList<Note> heart, IReadOnlyList<Note> @base)
    {
        Top = top;
        Heart = heart;
        Base = @base;
    }

    public IReadOnlyList<Note> Top { get; }

    public IReadOnlyList<Note> Heart { get; }

    public IReadOnlyList<Note> Base { get; }

    [JsonIgnore]
    public bool IsEmpty => Top.Count == 0 && Heart.Count == 0 && Base.Count == 0;

    public static Result<NotePyramid> Create(IEnumerable<Note> top, IEnumerable<Note> heart, IEnumerable<Note> @base)
    {
        Note[][] layers = [[.. top], [.. heart], [.. @base]];

        foreach (var layer in layers)
        {
            if (layer.Length > MaxNotesPerLayer)
            {
                return Result<NotePyramid>.Failure(ScentErrors.TooManyNotes);
            }

            if (layer.DistinctBy(note => note.Name, StringComparer.OrdinalIgnoreCase).Count() != layer.Length)
            {
                return Result<NotePyramid>.Failure(ScentErrors.DuplicateNote);
            }
        }

        return Result<NotePyramid>.Success(new NotePyramid(layers[0], layers[1], layers[2]));
    }
}
