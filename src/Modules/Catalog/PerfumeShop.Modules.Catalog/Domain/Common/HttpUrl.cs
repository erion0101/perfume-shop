namespace PerfumeShop.Modules.Catalog.Domain.Common;

internal static class HttpUrl
{
    public const int MaxLength = 500;

    public static bool IsValid(string? value) =>
        value is { Length: > 0 and <= MaxLength }
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}
