using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PerfumeShop.Shared.Infrastructure.Jobs;

namespace PerfumeShop.Modules.Catalog.Infrastructure.Storefront;

/// <summary>
/// Section "Storefront". The shop's pages are statically generated (ISR); after a catalog change the API asks the
/// storefront to rebuild them (<c>POST {RevalidateUrl}</c> with the secret header). Unset URL: nothing is sent and the
/// pages refresh on their own timer. The secret is a secret: user-secrets / env vars only.
/// </summary>
internal sealed class StorefrontOptions : IValidatableObject
{
    public const string SectionName = "Storefront";
    public const string SecretHeader = "x-revalidate-secret";
    public const int MinSecretLength = 32;

    /// <summary>e.g. https://saharascents.com/api/revalidate</summary>
    public Uri? RevalidateUrl { get; set; }

    public string? RevalidateSecret { get; set; }

    public bool IsConfigured => RevalidateUrl is not null;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RevalidateUrl is not null && (RevalidateSecret is null || RevalidateSecret.Length < MinSecretLength))
        {
            yield return new ValidationResult(
                $"Storefront:RevalidateSecret must be at least {MinSecretLength} characters when Storefront:RevalidateUrl is set.",
                [nameof(RevalidateSecret)]);
        }
    }
}

/// <summary>Cache tags the storefront uses; "catalog" covers home, listing, brand and product pages.</summary>
internal static class StorefrontTags
{
    public const string Catalog = "catalog";
}

internal sealed class StorefrontRevalidationJob(IHttpClientFactory httpClients, IOptions<StorefrontOptions> options, ILogger<StorefrontRevalidationJob> logger)
{
    public const string HttpClientName = "storefront-revalidation";

    /// <summary>Throws on failure so the job queue retries (the storefront may be restarting during a deploy).</summary>
    public async Task SendAsync(string[] tags)
    {
        var settings = options.Value;
        if (!settings.IsConfigured)
        {
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, settings.RevalidateUrl) { Content = JsonContent.Create(new { tags }) };
        request.Headers.Add(StorefrontOptions.SecretHeader, settings.RevalidateSecret);
        using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Storefront revalidation returned {(int)response.StatusCode}.", null, response.StatusCode);
        }

        logger.LogInformation("Storefront revalidated: {Tags}", string.Join(", ", tags));
    }
}

internal static class StorefrontRevalidationExtensions
{
    /// <summary>
    /// After every successful change (non-GET, 2xx) on these endpoints, queue a storefront revalidation.
    /// The request never waits for the storefront.
    /// </summary>
    public static TBuilder RevalidatesStorefront<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var result = await next(context);
            var http = context.HttpContext;
            if (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method))
            {
                return result;
            }

            var status = result is IStatusCodeHttpResult { StatusCode: { } code } ? code : http.Response.StatusCode;
            if (status is >= 200 and < 300 && http.RequestServices.GetRequiredService<IOptions<StorefrontOptions>>().Value.IsConfigured)
            {
                http.RequestServices.GetRequiredService<IBackgroundJobQueue>()
                    .Enqueue<StorefrontRevalidationJob>(job => job.SendAsync(new[] { StorefrontTags.Catalog }));
            }

            return result;
        });
}
