using PerfumeShop.Modules.Inventory.Contracts;
using PerfumeShop.Modules.Orders.Domain;
using PerfumeShop.Modules.Orders.Features.Shared;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Orders.Infrastructure;

/// <summary>
/// Staff status changes, shared by the Orders staff endpoints and <see cref="OrdersApi"/> (used by Shipping).
/// The aggregate decides whether a transition is allowed; Inventory follows:
/// Confirm → ConfirmAsync, Ship → CommitAsync (batch codes stored on the lines), Cancel/Refused → ReleaseAsync.
/// Inventory calls are idempotent, so a retry after a failed save is safe.
/// </summary>
internal sealed class OrderTransitions(OrdersDbContext db, IInventoryApi inventory, TimeProvider timeProvider)
{
    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    public Task<Result<Order>> ConfirmAsync(Guid id, OrderActor actor, CancellationToken ct) =>
        ChangeAsync(id, ct, async order =>
        {
            var confirmed = order.Confirm(actor, Now);
            return confirmed.IsFailure ? confirmed : await inventory.ConfirmAsync(LineIds(order), ct);
        });

    public Task<Result<Order>> CancelAsync(Guid id, OrderActor actor, string? reason, CancellationToken ct) =>
        ChangeAsync(id, ct, async order =>
        {
            var cancelled = order.Cancel(actor, reason, Now);
            return cancelled.IsFailure ? cancelled : await inventory.ReleaseAsync(LineIds(order), $"Order cancelled: {order.CancellationReason}", ct);
        });

    public Task<Result<Order>> ShipAsync(Guid id, OrderActor actor, string? courierName, string? trackingNumber, CancellationToken ct) =>
        ChangeAsync(id, ct, async order =>
        {
            var allowed = order.CanShip(actor, courierName, trackingNumber);
            if (allowed.IsFailure)
            {
                return allowed;
            }

            var committed = await inventory.CommitAsync(LineIds(order), ct);
            if (committed.IsFailure)
            {
                return committed;
            }

            var batches = committed.Value.ToDictionary(
                c => c.OrderLineId,
                c => (IReadOnlyList<LineBatch>)[.. c.Batches.Select(b => new LineBatch(b.BatchCode, b.Quantity))]);
            return order.Ship(actor, courierName, trackingNumber, batches, Now);
        });

    public Task<Result<Order>> MarkDeliveredAsync(Guid id, OrderActor actor, CancellationToken ct) =>
        ChangeAsync(id, ct, order => Task.FromResult(order.MarkDelivered(actor, Now)));

    /// <summary>Refused parcel back in the warehouse: the units go back into the batches they were shipped from.</summary>
    public Task<Result<Order>> MarkRefusedAsync(Guid id, OrderActor actor, string? reason, CancellationToken ct) =>
        ChangeAsync(id, ct, async order =>
        {
            var refused = order.MarkRefused(actor, reason, Now);
            return refused.IsFailure ? refused : await inventory.ReleaseAsync(LineIds(order), "Parcel refused by the customer", ct);
        });

    public Task<Result<Order>> MarkCashCollectedAsync(Guid id, OrderActor actor, decimal amount, string? note, CancellationToken ct) =>
        ChangeAsync(id, ct, order => Task.FromResult(order.MarkCashCollected(actor, amount, note, Now)));

    private async Task<Result<Order>> ChangeAsync(Guid id, CancellationToken ct, Func<Order, Task<Result>> change)
    {
        var order = await db.LoadOrderAsync(id, ct);
        if (order.IsFailure)
        {
            return order.Error;
        }

        var result = await change(order.Value);
        if (result.IsFailure)
        {
            db.ChangeTracker.Clear(); // nothing is saved
            return result.Error;
        }

        await db.SaveChangesAsync(ct);
        return order.Value;
    }

    private static Guid[] LineIds(Order order) => [.. order.Lines.Select(l => l.Id)];
}
