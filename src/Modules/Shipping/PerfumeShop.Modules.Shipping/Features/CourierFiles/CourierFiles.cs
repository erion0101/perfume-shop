using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using PerfumeShop.Modules.Shipping.Domain;
using PerfumeShop.Modules.Shipping.Features.Shared;
using PerfumeShop.Modules.Shipping.Infrastructure;
using PerfumeShop.Modules.Shipping.Infrastructure.Couriers;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Shipping.Features.CourierFiles;

/// <param name="WithoutReceipt">Parcels handed over with the staff override (no issued fiscal receipt).</param>
internal sealed record HandoverResult(DateOnly Date, int HandedOver, IReadOnlyList<string> TrackingNumbers, IReadOnlyList<string> WithoutReceipt);

/// <param name="OverrideMissingReceipt">Hand over parcels without an issued sale receipt too (requires <paramref name="OverrideReason"/>; logged per parcel).</param>
internal sealed record HandoverRequest(bool OverrideMissingReceipt = false, string? OverrideReason = null);

/// <param name="Replayed">True when this exact file was imported before: its stored result is returned, nothing is applied again.</param>
internal sealed record StatusImportResult(Guid ImportId, bool Replayed, int Applied, int AlreadyApplied, int Failed, IReadOnlyList<StatusImportLineResult> Lines);

/// <summary>Manifest, handover and courier status files for one courier.</summary>
internal sealed class CourierFilesHandler(
    ShippingDbContext db,
    ShipmentWorkflow workflow,
    ICourierAdapter adapter,
    IOptions<ShopInfo> shop,
    TimeProvider timeProvider) : IHandler
{
    /// <summary>Every parcel still waiting for this courier, booked on or before the given Kosovo day.</summary>
    public async Task<Result<ManifestFile>> ManifestAsync(Guid courierId, DateOnly? date, CancellationToken ct)
    {
        var courier = await db.Couriers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == courierId, ct);
        if (courier is null)
        {
            return ShippingFeatureErrors.CourierNotFound;
        }

        var day = date ?? shop.Value.LocalDay(timeProvider.GetUtcNow().UtcDateTime);
        var shipments = await WaitingAsync(courierId, day, ct);
        var receipts = await workflow.ReceiptStatesAsync([.. shipments.Select(s => s.OrderId)], ct);
        return adapter.BuildManifest(courier, day, shipments, receipts, shop.Value);
    }

    /// <summary>The courier picked up the parcels on the manifest.</summary>
    /// <remarks>All or nothing for missing receipts: without the override nothing is handed over and the parcels are listed.</remarks>
    public async Task<Result<HandoverResult>> HandOverAsync(Guid courierId, DateOnly? date, HandoverRequest? request, string actorId, CancellationToken ct)
    {
        if (request is { OverrideMissingReceipt: false, OverrideReason: not null })
        {
            return Error.Validation("Shipping.Shipment.OverrideFlagMissing", "Set overrideMissingReceipt = true to use an override reason.");
        }

        if (!await db.Couriers.AnyAsync(c => c.Id == courierId, ct))
        {
            return ShippingFeatureErrors.CourierNotFound;
        }

        var day = date ?? shop.Value.LocalDay(timeProvider.GetUtcNow().UtcDateTime);
        var waiting = await WaitingAsync(courierId, day, ct);
        var receipts = await workflow.ReceiptStatesAsync([.. waiting.Select(s => s.OrderId)], ct);
        var missing = waiting.Where(s => !ShipmentWorkflow.IsIssued(receipts[s.OrderId])).ToList();
        var overrideReason = request?.OverrideMissingReceipt == true ? request.OverrideReason ?? string.Empty : null;
        if (missing.Count > 0 && overrideReason?.Trim() is { Length: < 5 or > 500 })
        {
            return ShippingErrors.ReceiptOverrideReasonRequired; // checked first: nothing is handed over
        }

        if (missing.Count > 0 && overrideReason is null)
        {
            return ShippingErrors.ReceiptMissing([.. missing.Select(s => (s.TrackingNumber, s.OrderNumber, receipts[s.OrderId]))]);
        }

        List<string> handedOver = [];
        List<string> withoutReceipt = [];
        foreach (var shipment in waiting)
        {
            var moved = await workflow.MoveAsync(shipment.Id, ShipmentStatus.HandedOver, actorId, UpdateSource.Manual, $"Manifest {day:dd.MM.yyyy}", ct, overrideReason);
            if (moved.IsSuccess)
            {
                handedOver.Add(shipment.TrackingNumber);
                if (moved.Value.Shipment.ReceiptOverrideReason is not null)
                {
                    withoutReceipt.Add(shipment.TrackingNumber);
                }
            }
        }

        return new HandoverResult(day, handedOver.Count, handedOver, withoutReceipt);
    }

    /// <summary>Applies a courier status file. Re-sending the same file returns the stored result.</summary>
    public async Task<Result<StatusImportResult>> ImportStatusesAsync(Guid courierId, string csv, string actorId, CancellationToken ct)
    {
        if (!await db.Couriers.AnyAsync(c => c.Id == courierId, ct))
        {
            return ShippingFeatureErrors.CourierNotFound;
        }

        var hash = Csv.Hash(csv);
        if (await FindImportAsync(courierId, hash, ct) is { } previous)
        {
            return Summary(previous, replayed: true);
        }

        var parsed = adapter.ParseStatusUpdates(csv);
        if (parsed.IsFailure)
        {
            return parsed.Error;
        }

        var tracking = parsed.Value.Select(u => u.TrackingNumber).Distinct().ToList();
        var shipments = await db.Shipments.AsNoTracking()
            .Where(s => s.CourierId == courierId && tracking.Contains(s.TrackingNumber))
            .ToDictionaryAsync(s => s.TrackingNumber, s => s.Id, ct);

        List<StatusImportLineResult> results = [];
        foreach (var update in parsed.Value)
        {
            results.Add(await ApplyAsync(update, shipments, actorId, ct));
        }

        var import = new StatusImport
        {
            CourierId = courierId,
            FileHash = hash,
            ImportedBy = actorId,
            ImportedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            Results = results,
        };
        db.StatusImports.Add(import);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear(); // the same file was imported concurrently; its result wins
            return Summary((await FindImportAsync(courierId, hash, ct))!, replayed: true);
        }

        return Summary(import, replayed: false);
    }

    private async Task<StatusImportLineResult> ApplyAsync(CourierStatusUpdate update, Dictionary<string, Guid> shipments, string actorId, CancellationToken ct)
    {
        StatusImportLineResult Line(string outcome, string? note = null) => new(update.LineNumber, update.TrackingNumber, update.RawStatus, outcome, note);

        if (update.Status is not { } status)
        {
            return Line("InvalidStatus", "Use Delivered, Refused, InTransit or HandedOver.");
        }

        if (!shipments.TryGetValue(update.TrackingNumber, out var shipmentId))
        {
            return Line("UnknownTracking");
        }

        var moved = await workflow.MoveAsync(shipmentId, status, actorId, UpdateSource.CsvImport, update.Note, ct);
        if (moved.IsFailure)
        {
            return Line(moved.Error.Code switch
            {
                "Shipping.Shipment.InvalidTransition" => "InvalidTransition",
                "Shipping.Shipment.ReceiptMissing" => "ReceiptMissing",
                _ => "OrderUpdateFailed",
            }, moved.Error.Message);
        }

        return Line(moved.Value.Outcome == TransitionOutcome.Applied ? "Applied" : "AlreadyApplied");
    }

    private Task<StatusImport?> FindImportAsync(Guid courierId, string hash, CancellationToken ct) =>
        db.StatusImports.AsNoTracking().FirstOrDefaultAsync(i => i.CourierId == courierId && i.FileHash == hash, ct);

    private static StatusImportResult Summary(StatusImport import, bool replayed) => new(
        import.Id,
        replayed,
        import.Results.Count(r => r.Outcome == "Applied"),
        import.Results.Count(r => r.Outcome == "AlreadyApplied"),
        import.Results.Count(r => r.Outcome is not ("Applied" or "AlreadyApplied")),
        import.Results);

    private async Task<List<Shipment>> WaitingAsync(Guid courierId, DateOnly day, CancellationToken ct)
    {
        var until = shop.Value.EndOfLocalDayUtc(day);
        return await db.Shipments.AsNoTracking()
            .Where(s => s.CourierId == courierId && s.Status == ShipmentStatus.Created && s.CreatedAtUtc < until)
            .ToListAsync(ct);
    }
}

internal static class CourierFilesEndpoint
{
    public static void Map(RouteGroupBuilder staff)
    {
        staff.MapGet("/couriers/{id:guid}/manifest", async (Guid id, DateOnly? date, string? format, CourierFilesHandler h, CancellationToken ct) =>
            {
                var manifest = await h.ManifestAsync(id, date, ct);
                if (manifest.IsFailure)
                {
                    return manifest.Error.ToProblem();
                }

                return string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase)
                    ? TypedResults.File(manifest.Value.Csv, "text/csv; charset=utf-8", manifest.Value.FileName)
                    : (IResult)TypedResults.Content(manifest.Value.Html, "text/html; charset=utf-8");
            })
            .Produces<string>(StatusCodes.Status200OK, "text/html", "text/csv")
            .WithName("GetManifest")
            .WithDescription("Daily handover list. format=html (default, printable) or csv.");

        staff.MapPost("/couriers/{id:guid}/handover", async (Guid id, DateOnly? date, HandoverRequest? request, ClaimsPrincipal user, CourierFilesHandler h, CancellationToken ct) =>
                (await h.HandOverAsync(id, date, request, user.Actor(), ct)).ToOk())
            .WithName("HandOverManifest")
            .WithDescription("Every parcel needs an issued sale receipt; otherwise 409 listing them, or overrideMissingReceipt = true with a reason (logged).");

        staff.MapPost("/couriers/{id:guid}/status-imports", async (Guid id, HttpRequest request, ClaimsPrincipal user, CourierFilesHandler h, CancellationToken ct) =>
            {
                var csv = await request.ReadCsvAsync(ct);
                return csv.IsFailure ? csv.Error.ToProblem() : (await h.ImportStatusesAsync(id, csv.Value, user.Actor(), ct)).ToOk();
            })
            .Accepts<string>("text/csv")
            .WithName("ImportCourierStatuses")
            .WithDescription("CSV body: tracking_number;status;note");
    }
}
