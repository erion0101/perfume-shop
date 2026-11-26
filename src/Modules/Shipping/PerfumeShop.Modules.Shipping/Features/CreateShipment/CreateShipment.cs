using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Orders.Contracts;
using PerfumeShop.Modules.Shipping.Domain;
using PerfumeShop.Modules.Shipping.Features.Shared;
using PerfumeShop.Modules.Shipping.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Shipping.Features.CreateShipment;

/// <param name="TrackingNumber">The courier's waybill / tracking number written on the label.</param>
internal sealed record CreateShipmentRequest(Guid OrderId, Guid CourierId, string TrackingNumber, int ParcelCount = 1, int? WeightGrams = null, string? Note = null);

internal sealed record CreateShipmentResult(ShipmentDto Shipment, bool Replayed);

internal sealed class CreateShipmentValidator : AbstractValidator<CreateShipmentRequest>
{
    public CreateShipmentValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.CourierId).NotEmpty();
        RuleFor(x => x.TrackingNumber).NotEmpty().MaximumLength(Shipment.TextMaxLength);
        RuleFor(x => x.ParcelCount).InclusiveBetween(1, 10);
        RuleFor(x => x.WeightGrams).InclusiveBetween(1, 30_000).When(x => x.WeightGrams is not null);
        RuleFor(x => x.Note).MaximumLength(500);
    }
}

/// <summary>
/// Staff books a parcel for a confirmed order. Shipping owns courier + tracking and tells Orders the order
/// is shipped (which takes the stock out, oldest batches first). Repeating the same request returns the same shipment.
/// </summary>
internal sealed class CreateShipmentHandler(ShippingDbContext db, IOrdersApi orders, TimeProvider timeProvider) : IHandler
{
    public async Task<Result<CreateShipmentResult>> HandleAsync(CreateShipmentRequest request, string actorId, CancellationToken ct)
    {
        var tracking = request.TrackingNumber.Trim().ToUpperInvariant();

        var existing = await db.Shipments.AsNoTracking().Include(s => s.History).FirstOrDefaultAsync(s => s.OrderId == request.OrderId, ct);
        if (existing is not null)
        {
            return existing.CourierId == request.CourierId && existing.TrackingNumber == tracking
                ? new CreateShipmentResult(existing.ToDto(), Replayed: true)
                : ShippingFeatureErrors.OrderAlreadyHasShipment;
        }

        var order = await orders.GetOrderAsync(request.OrderId, ct);
        if (order is null)
        {
            return ShippingFeatureErrors.OrderNotFound;
        }

        // Shipped with this tracking number = shipped through the Orders endpoint (or our save failed earlier): adopt it.
        var adopt = order.Status == "Shipped" && order.TrackingNumber == tracking;
        if (order.Status != "Confirmed" && !adopt)
        {
            return ShippingFeatureErrors.OrderNotConfirmed;
        }

        var courier = await db.Couriers.FirstOrDefaultAsync(c => c.Id == request.CourierId, ct);
        if (courier is null)
        {
            return ShippingFeatureErrors.CourierNotFound;
        }

        if (await db.Shipments.AnyAsync(s => s.TrackingNumber == tracking, ct))
        {
            return ShippingFeatureErrors.TrackingTaken;
        }

        var address = order.DeliveryAddress;
        var shipment = Shipment.Create(
            order.OrderId, order.OrderNumber, order.CustomerId, courier, tracking, order.Total, request.ParcelCount, request.WeightGrams,
            new Recipient(order.CustomerName, order.CustomerPhone, address.CountryCode, address.City, address.Street, address.Notes),
            request.Note, actorId, timeProvider.GetUtcNow().UtcDateTime);
        if (shipment.IsFailure)
        {
            return shipment.Error;
        }

        var shipped = await orders.MarkShippedAsync(order.OrderId, courier.Name, tracking, actorId, ct);
        if (shipped.IsFailure)
        {
            return shipped.Error;
        }

        db.Shipments.Add(shipment.Value);
        await db.SaveChangesAsync(ct);
        return new CreateShipmentResult(shipment.Value.ToDto(), Replayed: false);
    }
}

internal static class CreateShipmentEndpoint
{
    public static void Map(RouteGroupBuilder staff) =>
        staff.MapPost("/shipments", async (CreateShipmentRequest request, ClaimsPrincipal user, CreateShipmentHandler handler, CancellationToken ct) =>
            {
                var result = await handler.HandleAsync(request, user.Actor(), ct);
                if (result.IsFailure)
                {
                    return result.Error.ToProblem();
                }

                return result.Value.Replayed
                    ? TypedResults.Ok(result.Value.Shipment)
                    : (IResult)TypedResults.Created($"/api/admin/shipping/shipments/{result.Value.Shipment.Id}", result.Value.Shipment);
            })
            .WithValidation<CreateShipmentRequest>()
            .WithName("CreateShipment")
            .Produces<ShipmentDto>(StatusCodes.Status201Created);
}
