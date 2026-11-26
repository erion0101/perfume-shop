using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PerfumeShop.Modules.Orders.Contracts;
using PerfumeShop.Modules.Shipping.Domain;
using PerfumeShop.Modules.Shipping.Features.Shared;
using PerfumeShop.Modules.Shipping.Infrastructure;
using PerfumeShop.Modules.Shipping.Infrastructure.Couriers;
using PerfumeShop.Shared.Infrastructure.Audit;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Shipping.Features.Settlements;

internal sealed record SettlementLineDto(int LineNumber, string TrackingNumber, string? OrderNumber, decimal CollectedAmount, decimal? ExpectedAmount,
    decimal Fee, decimal? ExpectedFee, string Status, string? Note);

/// <summary>A parcel the courier delivered but has not paid for within its remittance days.</summary>
internal sealed record UnpaidDeliveryDto(Guid ShipmentId, string OrderNumber, string TrackingNumber, decimal CodAmount, DateTime DeliveredAtUtc, int DaysSinceDelivery);

/// <param name="Mismatches">Every line that was not booked, grouped by reason.</param>
/// <param name="DeliveredButUnpaid">This courier's delivered parcels still unpaid after its remittance days.</param>
internal sealed record SettlementDto(
    Guid Id,
    Guid CourierId,
    string CourierName,
    string Reference,
    DateTime ImportedAtUtc,
    string ImportedBy,
    bool Replayed,
    decimal TotalCollected,
    decimal TotalFees,
    decimal NetPaid,
    int MatchedCount,
    decimal MatchedAmount,
    IReadOnlyDictionary<string, int> Mismatches,
    IReadOnlyList<SettlementLineDto> Lines,
    IReadOnlyList<UnpaidDeliveryDto> DeliveredButUnpaid);

/// <summary>
/// Courier remittance statement (CSV: tracking_number;collected_amount;fee). Lines are matched by tracking number;
/// exact amounts book the order as CashCollected through Orders. Everything else goes to the mismatch report.
/// The file hash makes a re-import return the existing settlement; a parcel already settled is never counted twice.
/// </summary>
internal sealed class SettlementsHandler(
    ShippingDbContext db,
    ShipmentWorkflow workflow,
    IOrdersApi orders,
    ICourierAdapter adapter,
    TimeProvider timeProvider) : IHandler
{
    public async Task<Result<SettlementDto>> ImportAsync(Guid courierId, string? reference, string csv, string actorId, CancellationToken ct)
    {
        var courier = await db.Couriers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == courierId, ct);
        if (courier is null)
        {
            return ShippingFeatureErrors.CourierNotFound;
        }

        var hash = Csv.Hash(csv);
        if (await FindAsync(courierId, hash, ct) is { } previous)
        {
            return await ToDtoAsync(previous, courier, replayed: true, ct);
        }

        var parsed = adapter.ParseRemittance(csv);
        if (parsed.IsFailure)
        {
            return parsed.Error;
        }

        var tracking = parsed.Value.Select(l => l.TrackingNumber).Distinct().ToList();
        var shipments = await db.Shipments.AsNoTracking()
            .Where(s => tracking.Contains(s.TrackingNumber))
            .Select(s => new ShipmentForMatching(s.Id, s.OrderId, s.TrackingNumber, s.OrderNumber, s.CourierId, s.Status, s.SettledAtUtc != null, s.CodAmount))
            .ToListAsync(ct);
        var classified = SettlementMatcher.Classify(courier, parsed.Value, shipments.ToDictionary(s => s.TrackingNumber, StringComparer.OrdinalIgnoreCase));

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var created = Settlement.Create(courier, reference, hash, actorId, classified, now);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var settlement = created.Value;

        // Phase 1: Orders (and delivery, when the statement is the first news of it) for every matched line.
        List<(SettlementLine Line, Guid ShipmentId)> booked = [];
        foreach (var line in settlement.Lines.Where(l => l.Status == SettlementLineStatus.Matched))
        {
            var error = await BookAsync(line, settlement.Reference, actorId, ct);
            if (error is null)
            {
                booked.Add((line, line.ShipmentId!.Value));
            }
            else
            {
                line.MarkOrderUpdateFailed(error);
            }
        }

        // Phase 2: record the settlement and mark the shipments paid, in one transaction.
        var ids = booked.Select(b => b.ShipmentId).ToList();
        var toSettle = await db.Shipments.Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        foreach (var (line, shipmentId) in booked)
        {
            toSettle[shipmentId].Settle(settlement.Id, line.CollectedAmount, line.Fee, now);
        }

        settlement.Complete();
        db.Settlements.Add(settlement);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear(); // the same file was imported concurrently
            return await ToDtoAsync((await FindAsync(courierId, hash, ct))!, courier, replayed: true, ct);
        }

        return await ToDtoAsync(settlement, courier, replayed: false, ct);
    }

    public async Task<Result<SettlementDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var settlement = await db.Settlements.AsNoTracking().Include(s => s.Lines).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (settlement is null)
        {
            return Error.NotFound("Shipping.Settlement.NotFound", "Settlement not found.");
        }

        var courier = await db.Couriers.AsNoTracking().FirstAsync(c => c.Id == settlement.CourierId, ct);
        return await ToDtoAsync(settlement, courier, replayed: false, ct);
    }

    public async Task<IReadOnlyList<SettlementDto>> ListAsync(Guid? courierId, CancellationToken ct)
    {
        var settlements = await db.Settlements.AsNoTracking().Include(s => s.Lines)
            .Where(s => courierId == null || s.CourierId == courierId)
            .OrderByDescending(s => s.ImportedAtUtc).Take(100)
            .ToListAsync(ct);
        return [.. settlements.Select(s => ToDto(s, false, []))];
    }

    /// <returns>null when booked, otherwise the reason.</returns>
    private async Task<string?> BookAsync(SettlementLine line, string reference, string actorId, CancellationToken ct)
    {
        var shipment = await db.Shipments.AsNoTracking().FirstAsync(s => s.Id == line.ShipmentId, ct);
        if (shipment.Status is ShipmentStatus.HandedOver or ShipmentStatus.InTransit)
        {
            var delivered = await workflow.MoveAsync(shipment.Id, ShipmentStatus.Delivered, actorId, UpdateSource.Settlement, $"Paid in statement {reference}", ct);
            if (delivered.IsFailure)
            {
                return delivered.Error.Message;
            }
        }

        var collected = await orders.MarkCashCollectedAsync(shipment.OrderId, line.CollectedAmount, $"Courier statement {reference}", actorId, ct);
        return collected.IsFailure ? collected.Error.Message : null;
    }

    private Task<Settlement?> FindAsync(Guid courierId, string hash, CancellationToken ct) =>
        db.Settlements.AsNoTracking().Include(s => s.Lines).FirstOrDefaultAsync(s => s.CourierId == courierId && s.FileHash == hash, ct);

    private async Task<SettlementDto> ToDtoAsync(Settlement settlement, Courier courier, bool replayed, CancellationToken ct) =>
        ToDto(settlement, replayed, await UnpaidAsync(db, courier, timeProvider.GetUtcNow().UtcDateTime, ct));

    internal static async Task<IReadOnlyList<UnpaidDeliveryDto>> UnpaidAsync(ShippingDbContext db, Courier courier, DateTime nowUtc, CancellationToken ct)
    {
        var dueBefore = nowUtc.AddDays(-courier.RemittanceDays);
        var unpaid = await db.Shipments.AsNoTracking()
            .Where(s => s.CourierId == courier.Id && s.Status == ShipmentStatus.Delivered && s.SettledAtUtc == null && s.DeliveredAtUtc <= dueBefore)
            .OrderBy(s => s.DeliveredAtUtc)
            .ToListAsync(ct);
        return [.. unpaid.Select(s => new UnpaidDeliveryDto(s.Id, s.OrderNumber, s.TrackingNumber, s.CodAmount, s.DeliveredAtUtc!.Value, (int)(nowUtc - s.DeliveredAtUtc!.Value).TotalDays))];
    }

    private static SettlementDto ToDto(Settlement s, bool replayed, IReadOnlyList<UnpaidDeliveryDto> unpaid) => new(
        s.Id, s.CourierId, s.CourierName, s.Reference, s.ImportedAtUtc, s.ImportedBy, replayed,
        s.TotalCollected, s.TotalFees, s.NetPaid, s.MatchedCount, s.MatchedAmount,
        s.Lines.Where(l => l.Status != SettlementLineStatus.Matched).GroupBy(l => l.Status.ToString()).ToDictionary(g => g.Key, g => g.Count()),
        [.. s.Lines.OrderBy(l => l.LineNumber).Select(l => new SettlementLineDto(
            l.LineNumber, l.TrackingNumber, l.OrderNumber, l.CollectedAmount, l.ExpectedAmount, l.Fee, l.ExpectedFee, l.Status.ToString(), l.Note))],
        unpaid);
}

internal static class SettlementsEndpoint
{
    public static void Map(RouteGroupBuilder staff)
    {
        staff.MapPost("/couriers/{id:guid}/settlements", async (Guid id, string? reference, HttpRequest request, ClaimsPrincipal user, SettlementsHandler h, CancellationToken ct) =>
            {
                var csv = await request.ReadCsvAsync(ct);
                return csv.IsFailure ? csv.Error.ToProblem() : (await h.ImportAsync(id, reference, csv.Value, user.Actor(), ct)).ToOk();
            })
            .Accepts<string>("text/csv")
            .Audited("Shipping.Settlement.Imported", "Courier", "Pasqyrë e korrierit e importuar")
            .WithName("ImportSettlement")
            .WithDescription("CSV body: tracking_number;collected_amount;fee. Re-importing the same file returns the same settlement.");

        staff.MapGet("/settlements", async (Guid? courierId, SettlementsHandler h, CancellationToken ct) => TypedResults.Ok(await h.ListAsync(courierId, ct)))
            .WithName("ListSettlements");

        staff.MapGet("/settlements/{id:guid}", async (Guid id, SettlementsHandler h, CancellationToken ct) => (await h.GetAsync(id, ct)).ToOk())
            .WithName("GetSettlement");
    }
}
