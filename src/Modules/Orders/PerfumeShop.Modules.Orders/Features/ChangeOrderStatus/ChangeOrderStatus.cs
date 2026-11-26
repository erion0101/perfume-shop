using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PerfumeShop.Modules.Orders.Domain;
using PerfumeShop.Modules.Orders.Features.Shared;
using PerfumeShop.Modules.Orders.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Audit;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Orders.Features.ChangeOrderStatus;

internal sealed record ReasonRequest(string? Reason);

/// <summary>
/// Staff steps that stay in Orders: confirm (after the phone call) and cancel (until shipped).
/// Ship / delivered / refused / cash collected belong to Shipping, which calls <c>IOrdersApi</c>.
/// </summary>
internal sealed class ChangeOrderStatusHandler(OrderTransitions transitions) : IHandler
{
    public Task<Result<OrderAdminDto>> ConfirmAsync(Guid id, OrderActor actor, CancellationToken ct) => Map(transitions.ConfirmAsync(id, actor, ct));

    public Task<Result<OrderAdminDto>> CancelAsync(Guid id, OrderActor actor, string? reason, CancellationToken ct) => Map(transitions.CancelAsync(id, actor, reason, ct));

    private static async Task<Result<OrderAdminDto>> Map(Task<Result<Order>> change)
    {
        var result = await change;
        return result.IsSuccess ? result.Value.ToAdminDto() : result.Error;
    }
}

internal static class ChangeOrderStatusEndpoint
{
    public static void Map(RouteGroupBuilder staff)
    {
        staff.MapPost("/{id:guid}/confirm", async (Guid id, ClaimsPrincipal user, ChangeOrderStatusHandler h, CancellationToken ct) =>
                (await h.ConfirmAsync(id, user.StaffActor(), ct)).ToOk())
            .Audited("Orders.Order.Confirmed", "Order", "Porosia u konfirmua")
            .WithName("ConfirmOrder");

        staff.MapPost("/{id:guid}/cancel", async (Guid id, ReasonRequest request, ClaimsPrincipal user, ChangeOrderStatusHandler h, CancellationToken ct) =>
                (await h.CancelAsync(id, user.StaffActor(), request.Reason, ct)).ToOk())
            .Audited("Orders.Order.Cancelled", "Order", "Porosia u anulua nga stafi")
            .WithName("CancelOrder");
    }
}
