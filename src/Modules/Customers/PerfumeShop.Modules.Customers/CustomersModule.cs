using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PerfumeShop.Modules.Customers.Contracts;
using PerfumeShop.Modules.Customers.Features.BlockCustomer;
using PerfumeShop.Modules.Customers.Features.GetCustomer;
using PerfumeShop.Modules.Customers.Features.GetMyProfile;
using PerfumeShop.Modules.Customers.Features.ListCustomers;
using PerfumeShop.Modules.Customers.Features.Logout;
using PerfumeShop.Modules.Customers.Features.ManageMyAddresses;
using PerfumeShop.Modules.Customers.Features.RefreshToken;
using PerfumeShop.Modules.Customers.Features.RequestOtp;
using PerfumeShop.Modules.Customers.Features.StaffLogin;
using PerfumeShop.Modules.Customers.Features.UpdateMyProfile;
using PerfumeShop.Modules.Customers.Features.VerifyOtp;
using PerfumeShop.Modules.Customers.Infrastructure;
using PerfumeShop.Modules.Customers.Infrastructure.Auth;
using PerfumeShop.Modules.Customers.Infrastructure.Identity;
using PerfumeShop.Shared.Infrastructure.Auth;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Infrastructure.Persistence;

namespace PerfumeShop.Modules.Customers;

public sealed class CustomersModule : IModule
{
    public string Name => CustomersDbContext.SchemaName;

    public void Register(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddModuleDbContext<CustomersDbContext>(configuration, CustomersDbContext.SchemaName);

        services.AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = false; // customers have no email
                options.Password.RequiredLength = 12;
                options.Password.RequireNonAlphanumeric = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<CustomersDbContext>();

        services.AddOptions<OtpOptions>()
            .BindConfiguration(OtpOptions.SectionName)
            .Validate(o => environment.IsDevelopment() || SecretKeys.IsValid(o.HashKey),
                $"Customers:Otp:HashKey must be configured with at least {SecretKeys.MinimumBytes} bytes outside Development.")
            .Validate(o => o.CodeLifetimeMinutes is >= 1 and <= 30 && o.MaxAttempts is >= 1 and <= 10
                && o.MaxCodesPerPhone >= 1 && o.MaxCodesPerIp >= 1 && o.RateLimitWindowMinutes >= 1, "Customers:Otp limits are out of range.")
            .ValidateOnStart();
        services.AddOptions<SeedAdminOptions>().BindConfiguration(SeedAdminOptions.SectionName);

        services.AddSingleton<OtpCodes>();
        services.AddScoped<TokenService>();
        services.AddScoped<ICustomersApi, CustomersApi>();

        services.AddHandlersFromAssembly(typeof(CustomersModule).Assembly);
        services.AddValidatorsFromAssembly(typeof(CustomersModule).Assembly, includeInternalTypes: true);
    }

    public async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await services.MigrateIfEnabledAsync<CustomersDbContext>(cancellationToken);
        await IdentitySeeder.SeedAsync(services, cancellationToken);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/api/auth").AllowAnonymous().WithTags("Auth");
        RequestOtpEndpoint.Map(auth);
        VerifyOtpEndpoint.Map(auth);
        StaffLoginEndpoint.Map(auth);
        PerfumeShop.Modules.Customers.Features.StaffSession.StaffSessionEndpoint.Map(auth);
        RefreshTokenEndpoint.Map(auth);
        LogoutEndpoint.Map(auth);

        var me = endpoints.MapGroup("/api/customers/me").RequireAuthorization(Policies.Customer).WithTags("My account");
        GetMyProfileEndpoint.Map(me);
        UpdateMyProfileEndpoint.Map(me);
        ManageMyAddressesEndpoint.Map(me);

        var admin = endpoints.MapGroup("/api/admin/customers").RequireAuthorization(Policies.Staff).WithTags("Customers (admin)");
        ListCustomersEndpoint.Map(admin);
        GetCustomerEndpoint.Map(admin);
        BlockCustomerEndpoint.Map(admin);
    }
}
