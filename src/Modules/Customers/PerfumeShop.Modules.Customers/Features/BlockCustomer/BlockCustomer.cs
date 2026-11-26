using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Customers.Domain.Customers;
using PerfumeShop.Modules.Customers.Features.Shared;
using PerfumeShop.Modules.Customers.Infrastructure;
using PerfumeShop.Modules.Customers.Infrastructure.Auth;
using PerfumeShop.Modules.Customers.Infrastructure.Identity;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Customers.Features.BlockCustomer;

/// <param name="Reason">Shown to staff, e.g. "Refuzoi 3 porosi COD".</param>
internal sealed record BlockCustomerRequest(string Reason);

internal sealed class BlockCustomerValidator : AbstractValidator<BlockCustomerRequest>
{
    public BlockCustomerValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(Customer.BlockReasonMaxLength);
}

/// <summary>
/// Blocking signs the customer out everywhere (refresh tokens revoked) and stops new OTP codes,
/// logins and profile changes. A still-valid access token expires within minutes.
/// </summary>
internal sealed class BlockCustomerHandler(CustomersDbContext db, TokenService tokens, TimeProvider timeProvider) : IHandler
{
    public async Task<Result<CustomerAdminDto>> BlockAsync(Guid id, BlockCustomerRequest request, CancellationToken cancellationToken)
    {
        var customer = await db.LoadAsync(id, cancellationToken);
        if (customer.IsFailure)
        {
            return customer.Error;
        }

        var blocked = customer.Value.Block(request.Reason, timeProvider.GetUtcNow().UtcDateTime);
        if (blocked.IsFailure)
        {
            return blocked.Error;
        }

        await db.SaveChangesAsync(cancellationToken);

        var userIds = await db.Users.Where(u => u.CustomerId == id).Select(u => u.Id).ToListAsync(cancellationToken);
        foreach (var userId in userIds)
        {
            await tokens.RevokeAllForUserAsync(userId, RevocationReasons.Blocked, cancellationToken);
        }

        return customer.Value.ToAdminDto();
    }

    public async Task<Result<CustomerAdminDto>> UnblockAsync(Guid id, CancellationToken cancellationToken)
    {
        var customer = await db.LoadAsync(id, cancellationToken);
        if (customer.IsFailure)
        {
            return customer.Error;
        }

        var unblocked = customer.Value.Unblock();
        if (unblocked.IsFailure)
        {
            return unblocked.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return customer.Value.ToAdminDto();
    }
}

internal static class BlockCustomerEndpoint
{
    public static void Map(RouteGroupBuilder admin)
    {
        admin.MapPost("/{id:guid}/block", async (Guid id, BlockCustomerRequest request, BlockCustomerHandler handler, CancellationToken ct) =>
                (await handler.BlockAsync(id, request, ct)).ToOk())
            .WithValidation<BlockCustomerRequest>()
            .WithName("BlockCustomer");

        admin.MapPost("/{id:guid}/unblock", async (Guid id, BlockCustomerHandler handler, CancellationToken ct) =>
                (await handler.UnblockAsync(id, ct)).ToOk())
            .WithName("UnblockCustomer");
    }
}
