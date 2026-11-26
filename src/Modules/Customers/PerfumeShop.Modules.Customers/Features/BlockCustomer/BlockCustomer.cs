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
using PerfumeShop.Shared.Infrastructure.Audit;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Customers.Features.BlockCustomer;

/// <param name="Reason">Shown to staff, e.g. "Refuzoi 3 porosi COD".</param>
internal sealed record BlockCustomerRequest(string Reason);

internal sealed record UnblockCustomerRequest(string? Reason);

internal sealed class BlockCustomerValidator : AbstractValidator<BlockCustomerRequest>
{
    public BlockCustomerValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(Customer.BlockReasonMaxLength);
}

/// <summary>
/// Blocking signs the customer out everywhere (refresh tokens revoked) and stops new OTP codes,
/// logins and profile changes. A still-valid access token expires within minutes.
/// </summary>
internal sealed class BlockCustomerHandler(CustomersDbContext db, TokenService tokens, TimeProvider timeProvider, IAuditLog audit) : IHandler
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

        await audit.RecordAsync(new AuditEntry("Customers.Customer.Blocked", "Customer", id.ToString(),
            $"Bllokuar: {customer.Value.Phone.Value} – {request.Reason.Trim()}", new Dictionary<string, object?> { ["reason"] = request.Reason.Trim() }), cancellationToken);
        return customer.Value.ToAdminDto();
    }

    public async Task<Result<CustomerAdminDto>> UnblockAsync(Guid id, string? reason, CancellationToken cancellationToken)
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
        await audit.RecordAsync(new AuditEntry("Customers.Customer.Unblocked", "Customer", id.ToString(),
            $"Zhbllokuar: {customer.Value.Phone.Value}{(string.IsNullOrWhiteSpace(reason) ? "" : $" – {reason.Trim()}")}",
            new Dictionary<string, object?> { ["reason"] = reason?.Trim() }), cancellationToken);
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

        admin.MapPost("/{id:guid}/unblock", async (Guid id, UnblockCustomerRequest? request, BlockCustomerHandler handler, CancellationToken ct) =>
                request?.Reason is { Length: > 500 }
                    ? Error.Validation("Customers.Customer.BlockReasonInvalid", "Reason: max 500 characters.").ToProblem()
                    : (await handler.UnblockAsync(id, request?.Reason, ct)).ToOk())
            .WithName("UnblockCustomer");
    }
}
