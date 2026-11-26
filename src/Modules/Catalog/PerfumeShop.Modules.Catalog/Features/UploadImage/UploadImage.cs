using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using PerfumeShop.Modules.Catalog.Infrastructure.Images;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Catalog.Features.UploadImage;

/// <summary>The stored photo; put <see cref="Url"/> (absolute) into a variant's images.</summary>
internal sealed record UploadedImageDto(string Url, string ContentType, long Bytes);

internal sealed class UploadImageHandler(IImageStorage storage, IOptions<ImageStorageOptions> options) : IHandler
{
    public static readonly Error Invalid = Error.Validation("Catalog.Image.Invalid", "Upload one JPEG, PNG or WebP image (max 5 MB).");

    public async Task<Result<UploadedImageDto>> HandleAsync(IFormFile? file, HttpRequest request, CancellationToken ct)
    {
        if (file is null || file.Length == 0 || file.Length > options.Value.MaxBytes)
        {
            return Invalid;
        }

        await using var stream = file.OpenReadStream();
        var head = new byte[12];
        var read = await stream.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, ct);
        if (ImageFormats.Detect(head.AsSpan(0, read)) is not { } format)
        {
            return Invalid;
        }

        stream.Position = 0;
        var saved = await storage.SaveAsync(stream, format.Extension, format.ContentType, ct);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        // Variants store absolute URLs. Development storage serves files from this API, under the host the browser used.
        var url = saved.Value.Url.StartsWith('/') ? $"{request.Scheme}://{request.Host}{request.PathBase}{saved.Value.Url}" : saved.Value.Url;
        return new UploadedImageDto(url, saved.Value.ContentType, saved.Value.Bytes);
    }
}

internal static class UploadImageEndpoint
{
    public static void Map(RouteGroupBuilder admin, IEndpointRouteBuilder endpoints)
    {
        admin.MapPost("/images", async (IFormFile? file, HttpRequest request, UploadImageHandler h, CancellationToken ct) => (await h.HandleAsync(file, request, ct)).ToOk())
            .DisableAntiforgery() // JWT bearer API: no cookies, no CSRF
            .WithName("UploadProductImage")
            .WithDescription("multipart/form-data with one 'file' (JPEG, PNG or WebP, max 5 MB). Returns the URL to use in a variant's images.");

        // Development storage only; production images come from R2/CDN URLs.
        endpoints.MapGet("/media/catalog/{year}/{fileName}", Results<PhysicalFileHttpResult, NotFound> (string year, string fileName, IImageStorage storage) =>
                storage.Resolve(year, fileName) is { } path
                    ? TypedResults.PhysicalFile(path, ImageFormats.ContentTypeOf(Path.GetExtension(path).TrimStart('.')), enableRangeProcessing: false)
                    : TypedResults.NotFound())
            .ExcludeFromDescription();
    }
}
