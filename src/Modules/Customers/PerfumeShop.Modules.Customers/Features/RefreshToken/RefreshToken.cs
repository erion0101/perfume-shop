using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PerfumeShop.Modules.Customers.Features.Shared;
using PerfumeShop.Modules.Customers.Infrastructure.Auth;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Customers.Features.RefreshToken;

internal sealed record RefreshTokenRequest(string RefreshToken);

internal sealed class RefreshTokenValidator : AbstractValidator<RefreshTokenRequest>
{
    public RefreshTokenValidator() => RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(200);
}

/// <summary>Rotates the refresh token. Re-using an old one revokes the whole session (theft detection).</summary>
internal sealed class RefreshTokenHandler(TokenService tokens) : IHandler
{
    public async Task<Result<LoginResponse>> HandleAsync(RefreshTokenRequest request, string? ipAddress, CancellationToken cancellationToken)
    {
        var refreshed = await tokens.RefreshAsync(request.RefreshToken, ipAddress, cancellationToken);
        return refreshed.IsSuccess ? LoginResponse.From(refreshed.Value) : refreshed.Error;
    }
}

internal static class RefreshTokenEndpoint
{
    public static void Map(RouteGroupBuilder auth) =>
        auth.MapPost("/refresh", async (RefreshTokenRequest request, HttpContext http, RefreshTokenHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(request, http.ClientIp(), ct)).ToOk())
            .WithValidation<RefreshTokenRequest>()
            .WithName("RefreshToken");
}
