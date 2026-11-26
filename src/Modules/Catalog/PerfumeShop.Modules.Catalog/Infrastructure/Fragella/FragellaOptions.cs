using System.ComponentModel.DataAnnotations;

namespace PerfumeShop.Modules.Catalog.Infrastructure.Fragella;

/// <summary>
/// Section "Fragella". The API key is a secret: set it with
/// <c>dotnet user-secrets set "Fragella:ApiKey" "..."</c> locally and the
/// <c>Fragella__ApiKey</c> environment variable in production. Never in appsettings.
/// </summary>
internal sealed class FragellaOptions
{
    public const string SectionName = "Fragella";

    [Required]
    public Uri BaseUrl { get; set; } = new("https://api.fragella.com/api/v1/");

    public string? ApiKey { get; set; }

    /// <summary>Hard stop below the plan's 5,000 requests/month, so we never hit the provider's cut-off.</summary>
    [Range(0, 1_000_000)]
    public int MonthlyRequestLimit { get; set; } = 4_500;

    [Range(1, 365)]
    public int SearchCacheDays { get; set; } = 30;

    /// <summary>Results per search; Fragella allows at most 10.</summary>
    [Range(1, 10)]
    public int SearchResultLimit { get; set; } = 10;

    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 15;
}
