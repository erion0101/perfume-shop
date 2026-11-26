using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Catalog.Infrastructure.Images;

/// <summary>Section "Catalog:Images".</summary>
internal sealed class ImageStorageOptions
{
    public const string SectionName = "Catalog:Images";

    /// <summary>Development: folder (relative to the content root) for uploaded product photos.</summary>
    public string LocalPath { get; set; } = "App_Data/media";

    public int MaxBytes { get; set; } = 5 * 1024 * 1024;
}

internal sealed record StoredImage(string Url, string ContentType, long Bytes);

/// <summary>Stores product photos (from the distributor). Cloudflare R2 + resizing later; see docs/before-production.md.</summary>
internal interface IImageStorage
{
    Task<Result<StoredImage>> SaveAsync(Stream content, string extension, string contentType, CancellationToken cancellationToken);

    /// <summary>Development only: the stored file for <c>GET /media/catalog/...</c>; null when unknown.</summary>
    string? Resolve(string year, string fileName);
}

internal static class ImageFormats
{
    /// <summary>Detects JPEG, PNG and WebP from the first bytes (the declared content type is not trusted).</summary>
    public static (string Extension, string ContentType)? Detect(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF)
        {
            return ("jpg", "image/jpeg");
        }

        if (head.Length >= 8 && head[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return ("png", "image/png");
        }

        if (head.Length >= 12 && head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8))
        {
            return ("webp", "image/webp");
        }

        return null;
    }

    public static string ContentTypeOf(string extension) => extension switch
    {
        "jpg" => "image/jpeg",
        "png" => "image/png",
        "webp" => "image/webp",
        _ => "application/octet-stream",
    };
}

/// <summary>DEVELOPMENT ONLY: files on the API's disk, served by the API. Not for production (no CDN, lost on redeploy).</summary>
internal sealed partial class LocalImageStorage(IHostEnvironment environment, IOptions<ImageStorageOptions> options, TimeProvider timeProvider) : IImageStorage
{
    private string Root => Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.LocalPath));

    public async Task<Result<StoredImage>> SaveAsync(Stream content, string extension, string contentType, CancellationToken cancellationToken)
    {
        var year = timeProvider.GetUtcNow().Year.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var folder = Path.Combine(Root, year);
        Directory.CreateDirectory(folder);
        var name = $"{Guid.CreateVersion7():N}.{extension}";
        await using (var file = File.Create(Path.Combine(folder, name)))
        {
            await content.CopyToAsync(file, cancellationToken);
        }

        return new StoredImage($"/media/catalog/{year}/{name}", contentType, new FileInfo(Path.Combine(folder, name)).Length);
    }

    public string? Resolve(string year, string fileName)
    {
        if (!Year().IsMatch(year) || !FileName().IsMatch(fileName))
        {
            return null; // no path traversal: only our own generated names
        }

        var path = Path.Combine(Root, year, fileName);
        return File.Exists(path) ? path : null;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^\d{4}$")]
    private static partial System.Text.RegularExpressions.Regex Year();

    [System.Text.RegularExpressions.GeneratedRegex(@"^[0-9a-f]{32}\.(jpg|png|webp)$")]
    private static partial System.Text.RegularExpressions.Regex FileName();
}

/// <summary>Outside Development until R2 is configured: uploads fail loudly (503).</summary>
internal sealed class UnconfiguredImageStorage(ILogger<UnconfiguredImageStorage> logger) : IImageStorage
{
    public Task<Result<StoredImage>> SaveAsync(Stream content, string extension, string contentType, CancellationToken cancellationToken)
    {
        logger.LogError("No image storage is configured (Cloudflare R2); the upload was rejected.");
        return Task.FromResult<Result<StoredImage>>(Error.Unavailable("Catalog.Image.StorageNotConfigured", "Image storage is not configured yet."));
    }

    public string? Resolve(string year, string fileName) => null;
}
