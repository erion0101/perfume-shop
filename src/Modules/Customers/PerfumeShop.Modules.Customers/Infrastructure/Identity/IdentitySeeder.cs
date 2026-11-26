using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PerfumeShop.Modules.Customers.Infrastructure.Auth;
using PerfumeShop.Shared.Infrastructure.Auth;

namespace PerfumeShop.Modules.Customers.Infrastructure.Identity;

/// <summary>
/// Creates the roles and, when Auth:SeedAdmin is configured, the first Admin account.
/// Idempotent: an existing account keeps its password; it only gets the Admin role back.
/// </summary>
internal static class IdentitySeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var roles = services.GetRequiredService<RoleManager<AppRole>>();
        var users = services.GetRequiredService<UserManager<AppUser>>();
        var options = services.GetRequiredService<IOptions<SeedAdminOptions>>().Value;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(IdentitySeeder));
        var time = services.GetRequiredService<TimeProvider>();

        foreach (var role in Roles.All)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await roles.RoleExistsAsync(role))
            {
                EnsureSucceeded(await roles.CreateAsync(new AppRole(role)), $"create role {role}");
            }
        }

        if (string.IsNullOrWhiteSpace(options.Email) || string.IsNullOrWhiteSpace(options.Password))
        {
            logger.LogInformation("Auth:SeedAdmin is not configured; no admin account seeded.");
            return;
        }

        var admin = await users.FindByEmailAsync(options.Email);
        if (admin is null)
        {
            admin = new AppUser
            {
                UserName = options.Email,
                Email = options.Email,
                EmailConfirmed = true,
                DisplayName = options.DisplayName ?? "Administrator",
                CreatedAtUtc = time.GetUtcNow().UtcDateTime,
            };
            EnsureSucceeded(await users.CreateAsync(admin, options.Password), "create the seed admin (check Auth:SeedAdmin:Password strength)");
            logger.LogInformation("Seeded admin account {Email}.", options.Email);
        }

        if (!await users.IsInRoleAsync(admin, Roles.Admin))
        {
            EnsureSucceeded(await users.AddToRoleAsync(admin, Roles.Admin), "grant the Admin role");
        }
    }

    private static void EnsureSucceeded(IdentityResult result, string action)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Could not {action}: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        }
    }
}
