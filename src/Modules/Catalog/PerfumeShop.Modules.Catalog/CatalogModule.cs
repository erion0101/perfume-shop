using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PerfumeShop.Modules.Catalog.Contracts;
using PerfumeShop.Modules.Catalog.Features.ChangeVariantStatus;
using PerfumeShop.Modules.Catalog.Features.CreateBrand;
using PerfumeShop.Modules.Catalog.Features.CreateVariant;
using PerfumeShop.Modules.Catalog.Features.DeleteBrand;
using PerfumeShop.Modules.Catalog.Features.GetBrand;
using PerfumeShop.Modules.Catalog.Features.GetFragellaUsage;
using PerfumeShop.Modules.Catalog.Features.GetFragranceAdmin;
using PerfumeShop.Modules.Catalog.Features.GetFragranceBySlug;
using PerfumeShop.Modules.Catalog.Features.ImportFromFragella;
using PerfumeShop.Modules.Catalog.Features.ListBrands;
using PerfumeShop.Modules.Catalog.Features.ListFragrances;
using PerfumeShop.Modules.Catalog.Features.ListFragrancesAdmin;
using PerfumeShop.Modules.Catalog.Features.PublishFragrance;
using PerfumeShop.Modules.Catalog.Features.SearchFragella;
using PerfumeShop.Modules.Catalog.Features.Shop;
using PerfumeShop.Modules.Catalog.Features.UpdateBrand;
using PerfumeShop.Modules.Catalog.Features.UpdateFragranceDetails;
using PerfumeShop.Modules.Catalog.Features.UpdateVariant;
using PerfumeShop.Modules.Catalog.Features.UploadImage;
using PerfumeShop.Modules.Catalog.Infrastructure.Fragella;
using PerfumeShop.Modules.Catalog.Infrastructure.Images;
using PerfumeShop.Modules.Catalog.Infrastructure;
using PerfumeShop.Modules.Catalog.Infrastructure.Search;
using PerfumeShop.Modules.Catalog.Infrastructure.Storefront;
using PerfumeShop.Shared.Infrastructure.Auth;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Infrastructure.Persistence;

namespace PerfumeShop.Modules.Catalog;

public sealed class CatalogModule : IModule
{
    public const string AdminRoutePrefix = "/api/admin/catalog";
    public const string PublicRoutePrefix = "/api/catalog";

    public string Name => CatalogDbContext.SchemaName;

    public void Register(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddModuleDbContext<CatalogDbContext>(configuration, CatalogDbContext.SchemaName);

        services.AddOptions<FragellaOptions>()
            .BindConfiguration(FragellaOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<IFragellaClient, FragellaClient>((sp, http) =>
        {
            var options = sp.GetRequiredService<IOptions<FragellaOptions>>().Value;
            http.BaseAddress = options.BaseUrl;
            http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            if (!string.IsNullOrWhiteSpace(options.ApiKey))
            {
                http.DefaultRequestHeaders.Add(FragellaClient.ApiKeyHeader, options.ApiKey);
            }
        });

        services.AddScoped<IFragellaBudget, FragellaBudget>();
        services.AddScoped<FragellaSearchService>();
        services.AddScoped<ICatalogApi, CatalogApi>();
        services.AddScoped<ICatalogSearch, PostgresTrigramCatalogSearch>();
        services.AddOptions<ImageStorageOptions>().BindConfiguration(ImageStorageOptions.SectionName);
        services.AddOptions<StorefrontOptions>()
            .BindConfiguration(StorefrontOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddHttpClient(StorefrontRevalidationJob.HttpClientName, http => http.Timeout = TimeSpan.FromSeconds(10));
        services.AddScoped<StorefrontRevalidationJob>();

        // Local disk storage is for Development only; production needs Cloudflare R2 (not implemented yet).
        if (environment.IsDevelopment())
        {
            services.AddSingleton<IImageStorage, LocalImageStorage>();
        }
        else
        {
            services.AddSingleton<IImageStorage, UnconfiguredImageStorage>();
        }

        services.AddHandlersFromAssembly(typeof(CatalogModule).Assembly);
        services.AddValidatorsFromAssembly(typeof(CatalogModule).Assembly, includeInternalTypes: true);
    }

    public async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await services.MigrateIfEnabledAsync<CatalogDbContext>(cancellationToken);
        await MoodBackfill.RunAsync(services, cancellationToken);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup(AdminRoutePrefix)
            .RequireAuthorization(Policies.Admin)
            .WithTags("Catalog (admin)")
            .RevalidatesStorefront();

        // Staff read the catalog (e.g. to pick the variant when receiving stock); every change stays Admin-only.
        var staffRead = endpoints.MapGroup(AdminRoutePrefix)
            .RequireAuthorization(Policies.Staff)
            .WithTags("Catalog (admin)");

        ListBrandsEndpoint.Map(staffRead);
        GetBrandEndpoint.Map(admin);
        CreateBrandEndpoint.Map(admin);
        UpdateBrandEndpoint.Map(admin);
        DeleteBrandEndpoint.Map(admin);

        SearchFragellaEndpoint.Map(admin);
        GetFragellaUsageEndpoint.Map(admin);
        ImportFromFragellaEndpoint.Map(admin);

        ListFragrancesAdminEndpoint.Map(staffRead);
        GetFragranceAdminEndpoint.Map(staffRead);
        UpdateFragranceDetailsEndpoint.Map(admin);
        PublishFragranceEndpoint.Map(admin);

        CreateVariantEndpoint.Map(admin);
        UpdateVariantEndpoint.Map(admin);
        ChangeVariantStatusEndpoint.Map(admin);
        UploadImageEndpoint.Map(admin, endpoints);

        var shop = endpoints.MapGroup(PublicRoutePrefix).WithTags("Catalog");

        ListFragrancesEndpoint.Map(shop);
        GetFragranceBySlugEndpoint.Map(shop);
        ShopCatalogEndpoints.Map(shop);
    }
}
