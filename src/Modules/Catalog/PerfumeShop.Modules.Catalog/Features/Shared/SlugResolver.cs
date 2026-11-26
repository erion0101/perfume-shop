using PerfumeShop.Modules.Catalog.Domain.Common;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Catalog.Features.Shared;

internal static class SlugResolver
{
    /// <summary>
    /// An explicitly requested slug must be free (409 otherwise). A generated slug gets
    /// "-2", "-3", … appended until it is free.
    /// </summary>
    public static async Task<Result<Slug>> ResolveAsync(
        string? requested,
        Func<Result<Slug>> generate,
        Func<Slug, CancellationToken, Task<bool>> isTaken,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var explicitSlug = Slug.Create(requested);
            if (explicitSlug.IsFailure)
            {
                return explicitSlug;
            }

            return await isTaken(explicitSlug.Value, cancellationToken) ? CatalogFeatureErrors.SlugTaken : explicitSlug;
        }

        var candidate = generate();
        if (candidate.IsFailure || !await isTaken(candidate.Value, cancellationToken))
        {
            return candidate;
        }

        for (var n = 2; n <= 100; n++)
        {
            var next = candidate.Value.WithSuffix(n);
            if (next.IsFailure || !await isTaken(next.Value, cancellationToken))
            {
                return next;
            }
        }

        return CatalogFeatureErrors.SlugExhausted;
    }
}
