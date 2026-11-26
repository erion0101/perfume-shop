using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PerfumeShop.Modules.Customers.Infrastructure.Auth;
using PerfumeShop.Shared.Infrastructure.Http;

namespace PerfumeShop.Modules.Customers.Features.Logout;

internal sealed record LogoutRequest(string RefreshToken);

internal sealed class LogoutValidator : AbstractValidator<LogoutRequest>
{
    public LogoutValidator() => RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(200);
}

/// <summary>
/// Revokes the session's refresh tokens. Always 204 (unknown tokens reveal nothing).
/// The access token stays valid until it expires (max Auth:Jwt:AccessTokenMinutes).
/// </summary>
internal static class LogoutEndpoint
{
    public static void Map(RouteGroupBuilder auth) =>
        auth.MapPost("/logout", async (LogoutRequest request, TokenService tokens, CancellationToken ct) =>
            {
                await tokens.RevokeAsync(request.RefreshToken, ct);
                return TypedResults.NoContent();
            })
            .WithValidation<LogoutRequest>()
            .WithName("Logout");
}
