using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PerfumeShop.Modules.Orders.Contracts;
using PerfumeShop.Modules.Payments.Contracts;
using PerfumeShop.Modules.Shipping.Domain;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Shipping.Infrastructure;

/// <summary>
/// One shipment status change plus what it means for the order (through <see cref="IOrdersApi"/>):
/// Delivered → order Delivered; ReturnedToWarehouse → order Refused (stock back into its batches).
/// A courier-reported refusal (Refused) does not touch the order: the parcel is still on its way back.
/// Handing a parcel to the courier needs its issued sale receipt, or a staff override with a reason (logged).
/// </summary>
internal sealed class ShipmentWorkflow(ShippingDbContext db, IOrdersApi orders, IPaymentsApi payments, TimeProvider timeProvider, ILogger<ShipmentWorkflow> logger, PerfumeShop.Shared.Infrastructure.Audit.IAuditLog audit)
{
    /// <summary>Receipt state per order: "Issued", "Pending", "Failed", "Voided" or "Missing" (not created yet).</summary>
    public async Task<IReadOnlyDictionary<Guid, string>> ReceiptStatesAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken)
    {
        var receipts = await payments.GetSaleReceiptsAsync(orderIds, cancellationToken);
        return orderIds.Distinct().ToDictionary(id => id, id => receipts.TryGetValue(id, out var r) ? r.Status : "Missing");
    }

    public static bool IsIssued(string receiptState) => receiptState == "Issued";

    /// <param name="receiptOverrideReason">Staff override: hand over although the sale receipt is not issued.</param>
    public async Task<Result<(Shipment Shipment, TransitionOutcome Outcome)>> MoveAsync(
        Guid shipmentId, ShipmentStatus to, string actorId, UpdateSource source, string? note, CancellationToken cancellationToken,
        string? receiptOverrideReason = null)
    {
        var shipment = await db.Shipments.Include(s => s.History).FirstOrDefaultAsync(s => s.Id == shipmentId, cancellationToken);
        if (shipment is null)
        {
            return Error.NotFound("Shipping.Shipment.NotFound", "Shipment not found.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (to == ShipmentStatus.HandedOver && shipment.Status == ShipmentStatus.Created)
        {
            var receipt = (await ReceiptStatesAsync([shipment.OrderId], cancellationToken))[shipment.OrderId];
            if (!IsIssued(receipt))
            {
                if (receiptOverrideReason is null)
                {
                    db.ChangeTracker.Clear();
                    return ShippingErrors.ReceiptMissing([(shipment.TrackingNumber, shipment.OrderNumber, receipt)]);
                }

                var overridden = shipment.OverrideMissingReceipt(receiptOverrideReason, actorId, now);
                if (overridden.IsFailure)
                {
                    db.ChangeTracker.Clear();
                    return overridden.Error;
                }

                logger.LogWarning(
                    "Parcel {Tracking} ({Order}) handed over WITHOUT an issued fiscal receipt (receipt {ReceiptState}) by {Actor}: {Reason}",
                    shipment.TrackingNumber, shipment.OrderNumber, receipt, actorId, shipment.ReceiptOverrideReason);
                note = $"PA KUPON FISKAL (receipt {receipt}) – override: {shipment.ReceiptOverrideReason}" + (string.IsNullOrWhiteSpace(note) ? string.Empty : $" · {note.Trim()}");
            }
        }

        var moved = shipment.MoveTo(to, actorId, source, note, now);
        if (moved.IsFailure)
        {
            db.ChangeTracker.Clear();
            return moved.Error;
        }

        if (moved.Value == TransitionOutcome.Applied)
        {
            var orderUpdate = to switch
            {
                ShipmentStatus.Delivered => await orders.MarkDeliveredAsync(shipment.OrderId, actorId, cancellationToken),
                ShipmentStatus.ReturnedToWarehouse => await orders.MarkRefusedAsync(
                    shipment.OrderId, shipment.History.LastOrDefault(h => h.ToStatus == ShipmentStatus.Refused)?.Note ?? "Parcel refused", actorId, cancellationToken),
                _ => null,
            };
            if (orderUpdate is { IsFailure: true })
            {
                db.ChangeTracker.Clear();
                return orderUpdate.Error;
            }

            await db.SaveChangesAsync(cancellationToken);
            if (to == ShipmentStatus.HandedOver && shipment.ReceiptOverrideReason is not null)
            {
                await audit.RecordAsync(new PerfumeShop.Shared.Infrastructure.Audit.AuditEntry(
                    "Shipping.Shipment.HandedOverWithoutReceipt", "Shipment", shipment.Id.ToString(),
                    $"{shipment.TrackingNumber} ({shipment.OrderNumber}) pa kupon fiskal: {shipment.ReceiptOverrideReason}",
                    new Dictionary<string, object?> { ["reason"] = shipment.ReceiptOverrideReason, ["orderNumber"] = shipment.OrderNumber }), cancellationToken);
            }
        }

        return (shipment, moved.Value);
    }
}
