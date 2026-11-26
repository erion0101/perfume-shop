using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PerfumeShop.Modules.Customers.Domain.Common;
using PerfumeShop.Modules.Customers.Domain.Customers;
using PerfumeShop.Modules.Customers.Domain.Verification;
using PerfumeShop.Modules.Customers.Features.Shared;
using PerfumeShop.Modules.Customers.Infrastructure;
using PerfumeShop.Modules.Customers.Infrastructure.Auth;
using PerfumeShop.Modules.Notifications.Contracts;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Customers.Features.RequestOtp;

/// <param name="Phone">E.164 or local Kosovo format: "+383 44 123 456", "044123456".</param>
internal sealed record RequestOtpRequest(string Phone);

internal sealed record RequestOtpResponse(string Phone, DateTime ExpiresAtUtc, int CodeLength);

internal sealed class RequestOtpValidator : AbstractValidator<RequestOtpRequest>
{
    public RequestOtpValidator() => RuleFor(x => x.Phone).NotEmpty().MaximumLength(30);
}

/// <summary>
/// Sends a 6-digit code by SMS. Limits: N codes per phone and M per IP within the window
/// (Customers:Otp). A new code invalidates the previous one.
/// </summary>
internal sealed class RequestOtpHandler(
    CustomersDbContext db,
    OtpCodes codes,
    ISmsSender sms,
    IOptions<OtpOptions> options,
    TimeProvider timeProvider) : IHandler
{
    public async Task<Result<RequestOtpResponse>> HandleAsync(RequestOtpRequest request, string? ipAddress, CancellationToken cancellationToken)
    {
        var phone = PhoneNumber.Create(request.Phone);
        if (phone.IsFailure)
        {
            return phone.Error;
        }

        var settings = options.Value;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        string code;
        PhoneVerification verification;

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await db.LockPhoneAsync(phone.Value, cancellationToken);

            if (await db.Customers.AnyAsync(c => c.Phone == phone.Value && c.Status == CustomerStatus.Blocked, cancellationToken))
            {
                return CustomerErrors.Blocked;
            }

            var windowStart = now.AddMinutes(-settings.RateLimitWindowMinutes);
            var perPhone = await db.PhoneVerifications.CountAsync(v => v.Phone == phone.Value && v.CreatedAtUtc >= windowStart, cancellationToken);
            var perIp = ipAddress is null
                ? 0
                : await db.PhoneVerifications.CountAsync(v => v.IpAddress == ipAddress && v.CreatedAtUtc >= windowStart, cancellationToken);
            if (perPhone >= settings.MaxCodesPerPhone || perIp >= settings.MaxCodesPerIp)
            {
                return OtpErrors.RateLimited;
            }

            var pending = await db.PhoneVerifications
                .Where(v => v.Phone == phone.Value && v.ConsumedAtUtc == null && v.InvalidatedAtUtc == null)
                .ToListAsync(cancellationToken);
            pending.ForEach(v => v.Invalidate(now));

            code = OtpCodes.Generate();
            var id = Guid.CreateVersion7();
            verification = PhoneVerification.Issue(
                id, phone.Value, codes.Hash(id, phone.Value, code), now,
                TimeSpan.FromMinutes(settings.CodeLifetimeMinutes), settings.MaxAttempts, ipAddress);
            db.PhoneVerifications.Add(verification);

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        var sent = await sms.SendAsync(
            phone.Value.Value,
            $"PerfumeShop: kodi juaj i hyrjes është {code}. Vlen {settings.CodeLifetimeMinutes} minuta. Mos ia tregoni askujt.",
            cancellationToken);
        if (!sent.Succeeded)
        {
            verification.Invalidate(timeProvider.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync(CancellationToken.None);
            return OtpErrors.SmsFailed;
        }

        return new RequestOtpResponse(phone.Value.Masked(), verification.ExpiresAtUtc, OtpCodes.Length);
    }
}

internal static class RequestOtpEndpoint
{
    public static void Map(RouteGroupBuilder auth) =>
        auth.MapPost("/otp/request", async (RequestOtpRequest request, HttpContext http, RequestOtpHandler handler, CancellationToken ct) =>
            {
                var result = await handler.HandleAsync(request, http.ClientIp(), ct);
                return result.IsSuccess ? (Results<Accepted<RequestOtpResponse>, ProblemHttpResult>)TypedResults.Accepted((string?)null, result.Value) : result.Error.ToProblem();
            })
            .WithValidation<RequestOtpRequest>()
            .WithName("RequestOtp")
            .Produces<RequestOtpResponse>(StatusCodes.Status202Accepted);
}
