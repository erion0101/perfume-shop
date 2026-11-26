using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PerfumeShop.Modules.Administration.Contracts;
using PerfumeShop.Modules.Administration.Domain;
using PerfumeShop.Modules.Administration.Features;
using PerfumeShop.Modules.Administration.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Audit;
using PerfumeShop.Shared.Infrastructure.Auth;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Infrastructure.Persistence;

namespace PerfumeShop.Modules.Administration;

/// <summary>Back-office administration: the business profile (receipts, slips, SMS) and the audit log.</summary>
public sealed class AdministrationModule : IModule
{
    public string Name => AdministrationDbContext.SchemaName;

    public void Register(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddModuleDbContext<AdministrationDbContext>(configuration, AdministrationDbContext.SchemaName);
        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<IBusinessProfileApi, BusinessProfileApi>();
        services.AddHandlersFromAssembly(typeof(AdministrationModule).Assembly);
        services.AddValidatorsFromAssembly(typeof(AdministrationModule).Assembly, includeInternalTypes: true);
    }

    public async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await services.MigrateIfEnabledAsync<AdministrationDbContext>(cancellationToken);

        // First start: take the (placeholder) values from configuration; afterwards Admin edits them in the panel.
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AdministrationDbContext>();
        if (!await db.BusinessProfiles.AnyAsync(cancellationToken))
        {
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>().GetSection("Payments:Business");
            db.BusinessProfiles.Add(BusinessProfile.Seed(config["Name"], config["Address"], config["Phone"], config["TaxNumber"]));
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // another instance seeded it at the same moment
            }
        }
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var staff = endpoints.MapGroup("/api/admin").RequireAuthorization(Policies.Staff).WithTags("Administration (staff)");
        var admin = endpoints.MapGroup("/api/admin").RequireAuthorization(Policies.Admin).WithTags("Administration (admin)");
        BusinessProfileEndpoint.Map(staff, admin);
        AuditEndpoint.Map(admin);
    }
}
