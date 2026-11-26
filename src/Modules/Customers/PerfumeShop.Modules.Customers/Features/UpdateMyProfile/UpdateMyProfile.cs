using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PerfumeShop.Modules.Customers.Domain.Customers;
using PerfumeShop.Modules.Customers.Features.Shared;
using PerfumeShop.Modules.Customers.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Auth;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Customers.Features.UpdateMyProfile;

/// <summary>The phone number cannot be changed here: it is the login and was verified by OTP.</summary>
internal sealed record UpdateMyProfileRequest(string? FullName, string? Email);

internal sealed class UpdateMyProfileValidator : AbstractValidator<UpdateMyProfileRequest>
{
    public UpdateMyProfileValidator()
    {
        RuleFor(x => x.FullName).MaximumLength(Customer.FullNameMaxLength);
        RuleFor(x => x.Email).MaximumLength(Customer.EmailMaxLength);
    }
}

internal sealed class UpdateMyProfileHandler(CustomersDbContext db) : IHandler
{
    public async Task<Result<CustomerProfileDto>> HandleAsync(Guid customerId, UpdateMyProfileRequest request, CancellationToken cancellationToken)
    {
        var customer = await db.LoadActiveAsync(customerId, cancellationToken);
        if (customer.IsFailure)
        {
            return customer.Error;
        }

        var updated = customer.Value.UpdateProfile(request.FullName, request.Email);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return customer.Value.ToProfileDto();
    }
}

internal static class UpdateMyProfileEndpoint
{
    public static void Map(RouteGroupBuilder me) =>
        me.MapPut("/", async (ClaimsPrincipal user, UpdateMyProfileRequest request, UpdateMyProfileHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(user.GetCustomerId()!.Value, request, ct)).ToOk())
            .WithValidation<UpdateMyProfileRequest>()
            .WithName("UpdateMyProfile");
}
