using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Customers.Domain.Common;
using PerfumeShop.Modules.Customers.Domain.Customers;
using PerfumeShop.Modules.Customers.Features.Shared;
using PerfumeShop.Modules.Customers.Infrastructure;
using PerfumeShop.Modules.Customers.Infrastructure.Auth;
using PerfumeShop.Modules.Customers.Infrastructure.Identity;
using PerfumeShop.Shared.Infrastructure.Auth;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Customers.Features.VerifyOtp;

internal sealed record VerifyOtpRequest(string Phone, string Code);

internal sealed class VerifyOtpValidator : AbstractValidator<VerifyOtpRequest>
{
    public VerifyOtpValidator()
    {
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Code).NotEmpty().Matches("^[0-9]{6}$").WithMessage("The code has 6 digits.");
    }
}

/// <summary>
/// Checks the code and signs the customer in. The first successful login creates the customer
/// and its login account in the same transaction.
/// </summary>
internal sealed class VerifyOtpHandler(
    CustomersDbContext db,
    OtpCodes codes,
    UserManager<AppUser> users,
    TokenService tokens,
    TimeProvider timeProvider) : IHandler
{
    public async Task<Result<LoginResponse>> HandleAsync(VerifyOtpRequest request, string? ipAddress, CancellationToken cancellationToken)
    {
        var phone = PhoneNumber.Create(request.Phone);
        if (phone.IsFailure)
        {
            return phone.Error;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Same lock as RequestOtp: parallel guesses cannot bypass the attempt limit.
        await db.LockPhoneAsync(phone.Value, cancellationToken);

        var verification = await db.PhoneVerifications
            .Where(v => v.Phone == phone.Value && v.ConsumedAtUtc == null && v.InvalidatedAtUtc == null)
            .OrderByDescending(v => v.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (verification is null)
        {
            return OtpErrors.InvalidCode;
        }

        var outcome = verification.Verify(codes.Matches(verification.Id, phone.Value, request.Code, verification.CodeHash), now);
        await db.SaveChangesAsync(cancellationToken); // the attempt counts even when the code is wrong
        if (outcome.IsFailure)
        {
            await transaction.CommitAsync(cancellationToken);
            return outcome.Error;
        }

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Phone == phone.Value, cancellationToken);
        var isNew = customer is null;
        if (customer is null)
        {
            customer = Customer.Register(phone.Value, now);
            db.Customers.Add(customer);
            await db.SaveChangesAsync(cancellationToken);
        }
        else if (customer.IsBlocked)
        {
            await transaction.CommitAsync(cancellationToken);
            return CustomerErrors.Blocked;
        }

        var user = await users.Users.FirstOrDefaultAsync(u => u.CustomerId == customer.Id, cancellationToken)
            ?? await CreateLoginAsync(customer, now);

        customer.RecordLogin(now);
        await db.SaveChangesAsync(cancellationToken);
        var issued = await tokens.IssueAsync(user, ipAddress, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return LoginResponse.From(issued, customer.Id, isNew);
    }

    private async Task<AppUser> CreateLoginAsync(Customer customer, DateTime now)
    {
        var user = new AppUser
        {
            UserName = customer.Phone.Value,
            PhoneNumber = customer.Phone.Value,
            PhoneNumberConfirmed = true,
            CustomerId = customer.Id,
            CreatedAtUtc = now,
        };

        Ensure(await users.CreateAsync(user));
        Ensure(await users.AddToRoleAsync(user, Roles.Customer));
        return user;
    }

    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("Could not create the customer login: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }
}

internal static class VerifyOtpEndpoint
{
    public static void Map(RouteGroupBuilder auth) =>
        auth.MapPost("/otp/verify", async (VerifyOtpRequest request, HttpContext http, VerifyOtpHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(request, http.ClientIp(), ct)).ToOk())
            .WithValidation<VerifyOtpRequest>()
            .WithName("VerifyOtp");
}
