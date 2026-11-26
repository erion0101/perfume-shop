using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PerfumeShop.Modules.Orders.Contracts;
using PerfumeShop.Modules.Shipping.Domain;
using PerfumeShop.Modules.Shipping.Features.Shared;
using PerfumeShop.Modules.Shipping.Infrastructure;
using PerfumeShop.Modules.Shipping.Infrastructure.Couriers;
using PerfumeShop.Shared.Infrastructure.Audit;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Shipping.Features.Shipments;

internal sealed record ListShipmentsQuery(string? Status, Guid? CourierId, string? Tracking, string? OrderNumber, int Page = 1, int PageSize = 50);

internal sealed record StatusNoteRequest(string? Note);

/// <param name="OverrideMissingReceipt">Hand over although the sale receipt is not issued (requires <paramref name="OverrideReason"/>; logged).</param>
internal sealed record HandOverRequest(string? Note, bool OverrideMissingReceipt = false, string? OverrideReason = null);

/// <param name="Note">Required when the amount differs from the COD amount.</param>
internal sealed record ManualCashRequest(decimal Amount, string? Note);

internal sealed class ListShipmentsValidator : AbstractValidator<ListShipmentsQuery>
{
    public ListShipmentsValidator()
    {
        RuleFor(x => x.Status).IsEnumName(typeof(ShipmentStatus), caseSensitive: false).When(x => x.Status is not null);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}

internal sealed class ShipmentsHandler(ShippingDbContext db, ShipmentWorkflow workflow, IOrdersApi orders, ShopDetails shop) : IHandler
{
    public async Task<PagedResponse<ShipmentDto>> ListAsync(ListShipmentsQuery query, CancellationToken ct)
    {
        var shipments = db.Shipments.AsNoTracking();
        if (Enum.TryParse<ShipmentStatus>(query.Status, ignoreCase: true, out var status))
        {
            shipments = shipments.Where(s => s.Status == status);
        }

        if (query.CourierId is { } courierId)
        {
            shipments = shipments.Where(s => s.CourierId == courierId);
        }

        if (!string.IsNullOrWhiteSpace(query.Tracking))
        {
            var tracking = query.Tracking.Trim().ToUpperInvariant();
            shipments = shipments.Where(s => s.TrackingNumber.Contains(tracking));
        }

        if (!string.IsNullOrWhiteSpace(query.OrderNumber))
        {
            var number = query.OrderNumber.Trim().ToUpperInvariant();
            shipments = shipments.Where(s => s.OrderNumber.Contains(number));
        }

        var total = await shipments.CountAsync(ct);
        var page = await shipments.Include(s => s.History)
            .OrderByDescending(s => s.CreatedAtUtc)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .ToListAsync(ct);
        return new PagedResponse<ShipmentDto>([.. page.Select(s => s.ToDto())], query.Page, query.PageSize, total);
    }

    public async Task<Result<ShipmentDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var shipment = await db.Shipments.AsNoTracking().Include(s => s.History).FirstOrDefaultAsync(s => s.Id == id, ct);
        return shipment is null ? ShippingFeatureErrors.ShipmentNotFound : shipment.ToDto();
    }

    public async Task<Result<ShipmentDto>> MoveAsync(Guid id, ShipmentStatus to, string actorId, string? note, CancellationToken ct, string? receiptOverrideReason = null)
    {
        var moved = await workflow.MoveAsync(id, to, actorId, UpdateSource.Manual, note, ct, receiptOverrideReason);
        return moved.IsSuccess ? moved.Value.Shipment.ToDto() : moved.Error;
    }

    /// <summary>
    /// Cash recorded by hand (e.g. after a statement mismatch was checked with the courier):
    /// the shipment counts as settled and the order becomes CashCollected.
    /// </summary>
    public async Task<Result<ShipmentDto>> CashCollectedAsync(Guid id, ManualCashRequest request, string actorId, TimeProvider time, CancellationToken ct)
    {
        var shipment = await db.Shipments.Include(s => s.History).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (shipment is null)
        {
            return ShippingFeatureErrors.ShipmentNotFound;
        }

        var settled = shipment.SettleManually(request.Amount, request.Note, actorId, time.GetUtcNow().UtcDateTime);
        if (settled.IsFailure)
        {
            return settled.Error;
        }

        var collected = await orders.MarkCashCollectedAsync(shipment.OrderId, request.Amount, request.Note ?? "Cash collected (manual)", actorId, ct);
        if (collected.IsFailure)
        {
            return collected.Error;
        }

        await db.SaveChangesAsync(ct);
        return shipment.ToDto();
    }

    /// <summary>Printable packing slip with the lines, quantities and batch codes from Orders.</summary>
    public async Task<Result<string>> PackingSlipAsync(Guid id, CancellationToken ct)
    {
        var shipment = await db.Shipments.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (shipment is null)
        {
            return ShippingFeatureErrors.ShipmentNotFound;
        }

        var order = await orders.GetOrderAsync(shipment.OrderId, ct);
        return order is null ? ShippingFeatureErrors.OrderNotFound : PackingSlips.Render(shipment, order, await shop.GetAsync(ct));
    }
}

internal static class ShipmentsEndpoint
{
    public static void Map(RouteGroupBuilder staff)
    {
        staff.MapGet("/shipments", async ([AsParameters] ListShipmentsQuery query, ShipmentsHandler h, CancellationToken ct) => TypedResults.Ok(await h.ListAsync(query, ct)))
            .WithValidation<ListShipmentsQuery>()
            .WithName("ListShipments");

        staff.MapGet("/shipments/{id:guid}", async (Guid id, ShipmentsHandler h, CancellationToken ct) => (await h.GetAsync(id, ct)).ToOk())
            .WithName("GetShipment");

        staff.MapGet("/shipments/{id:guid}/packing-slip", async (Guid id, ShipmentsHandler h, CancellationToken ct) =>
            {
                var html = await h.PackingSlipAsync(id, ct);
                return html.IsSuccess ? TypedResults.Content(html.Value, "text/html; charset=utf-8") : (IResult)html.Error.ToProblem();
            })
            .Produces<string>(StatusCodes.Status200OK, "text/html")
            .WithName("GetPackingSlip");

        staff.MapPost("/shipments/{id:guid}/cash-collected", async (Guid id, ManualCashRequest request, ClaimsPrincipal user, ShipmentsHandler h, TimeProvider time, CancellationToken ct) =>
                (await h.CashCollectedAsync(id, request, user.Actor(), time, ct)).ToOk())
            .Audited("Shipping.Shipment.CashRecorded", "Shipment", "Para të regjistruara manualisht")
            .WithName("RecordCashCollected")
            .WithDescription("Manual COD booking, e.g. for a statement line with a wrong amount (note required when the amount differs).");

        staff.MapPost("/shipments/{id:guid}/handed-over", async (Guid id, HandOverRequest? request, ClaimsPrincipal user, ShipmentsHandler h, CancellationToken ct) =>
                request is { OverrideMissingReceipt: false, OverrideReason: not null }
                    ? Error.Validation("Shipping.Shipment.OverrideFlagMissing", "Set overrideMissingReceipt = true to use an override reason.").ToProblem()
                    : (await h.MoveAsync(id, ShipmentStatus.HandedOver, user.Actor(), request?.Note, ct,
                        request?.OverrideMissingReceipt == true ? request.OverrideReason ?? string.Empty : null)).ToOk())
            .Audited("Shipping.Shipment.HandedOver", "Shipment", "Pakoja u dorëzua te korrieri")
            .WithName("HandOverShipment")
            .WithDescription("Needs an issued sale receipt; otherwise 409, or overrideMissingReceipt = true with a reason (logged).");
        MapStep(staff, "in-transit", ShipmentStatus.InTransit, "MarkShipmentInTransit");
        MapStep(staff, "delivered", ShipmentStatus.Delivered, "MarkShipmentDelivered");
        MapStep(staff, "refused", ShipmentStatus.Refused, "MarkShipmentRefused");
        MapStep(staff, "returned", ShipmentStatus.ReturnedToWarehouse, "MarkShipmentReturned");
    }

    private static void MapStep(RouteGroupBuilder staff, string path, ShipmentStatus to, string name) =>
        staff.MapPost($"/shipments/{{id:guid}}/{path}", async (Guid id, StatusNoteRequest? request, ClaimsPrincipal user, ShipmentsHandler h, CancellationToken ct) =>
                (await h.MoveAsync(id, to, user.Actor(), request?.Note, ct)).ToOk())
            .Audited($"Shipping.Shipment.{to}", "Shipment", $"Statusi i pakos: {to}")
            .WithName(name);
}
