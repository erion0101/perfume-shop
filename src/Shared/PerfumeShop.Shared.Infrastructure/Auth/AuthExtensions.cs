using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace PerfumeShop.Shared.Infrastructure.Auth;

public static class AuthExtensions
{
    /// <summary>JWT bearer authentication (tokens issued by the Customers module) + role policies.</summary>
    public static IServiceCollection AddPlatformAuth(this IServiceCollection services, IHostEnvironment environment)
    {
        services.AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.SectionName)
            .Validate(o => environment.IsDevelopment() || SecretKeys.IsValid(o.SigningKey),
                $"Auth:Jwt:SigningKey must be configured with at least {SecretKeys.MinimumBytes} bytes outside Development.")
            .Validate(o => o.AccessTokenMinutes is > 0 and <= 60, "Auth:Jwt:AccessTokenMinutes must be 1-60.")
            .ValidateOnStart();

        services.AddSingleton<JwtSigningKey>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>, JwtSigningKey>((bearer, jwt, key) =>
            {
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Value.Issuer,
                    ValidAudience = jwt.Value.Audience,
                    IssuerSigningKey = key.Key,
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = PlatformClaims.Subject,
                    RoleClaimType = PlatformClaims.Role,
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Admin, policy => policy.RequireRole(Roles.Admin))
            .AddPolicy(Policies.Staff, policy => policy.RequireRole(Roles.Admin, Roles.Staff))
            .AddPolicy(Policies.Customer, policy => policy.RequireRole(Roles.Customer).RequireClaim(PlatformClaims.CustomerId));

        return services;
    }

    public static Guid? GetUserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(PlatformClaims.Subject), out var id) ? id : null;

    public static Guid? GetCustomerId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(PlatformClaims.CustomerId), out var id) ? id : null;
}
