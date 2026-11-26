using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Inventory.Domain;
using PerfumeShop.Modules.Inventory.Features.Shared;
using PerfumeShop.Modules.Inventory.Infrastructure;
using PerfumeShop.Modules.Orders.Contracts;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;

namespace PerfumeShop.Modules.Inventory.Features.TraceBatch;

internal sealed record TraceBatchQuery(string? Code);

/// <param name="OrderNumber">From Orders; null if the order is unknown there.</param>
internal sealed record BatchShipmentDto(Guid OrderId, string? OrderNumber, Guid OrderLineId, int Quantity, string Status, DateTime? CommittedAtUtc);

/// <summary>A batch with its invoice and every order line its units were shipped to.</summary>
internal sealed record BatchTraceDto(Guid VariantId, BatchDto Batch, IReadOnlyList<BatchShipmentDto> ShippedTo);

internal sealed class TraceBatchValidator : AbstractValidator<TraceBatchQuery>
{
    public TraceBatchValidator() => RuleFor(x => x.Code).NotEmpty().MaximumLength(StockBatch.BatchCodeMaxLength);
}

/// <summary>Authenticity / recall: which invoice a batch came from and which orders received it.</summary>
internal sealed class TraceBatchHandler(InventoryDbContext db, IOrdersApi orders) : IHandler
{
    public async Task<IReadOnlyList<BatchTraceDto>> HandleAsync(TraceBatchQuery query, CancellationToken cancellationToken)
    {
        var code = query.Code!.Trim();
        var batches = await db.Batches.AsNoTracking().Where(b => b.BatchCode == code).ToListAsync(cancellationToken);
        var batchIds = batches.Select(b => b.Id).ToList();

        var shipments = await (from a in db.Allocations.AsNoTracking()
                               join r in db.Reservations on a.ReservationId equals r.Id
                               where batchIds.Contains(a.BatchId)
                               select new { a.BatchId, r.OrderId, r.OrderLineId, a.Quantity, r.Status, r.CommittedAtUtc })
            .ToListAsync(cancellationToken);

        var numbers = await orders.GetOrderNumbersAsync([.. shipments.Select(s => s.OrderId)], cancellationToken);
        return [.. batches.Select(b => new BatchTraceDto(
            b.VariantId,
            b.ToDto(),
            [.. shipments.Where(s => s.BatchId == b.Id).Select(s => new BatchShipmentDto(s.OrderId, numbers.GetValueOrDefault(s.OrderId), s.OrderLineId, s.Quantity, s.Status.ToString(), s.CommittedAtUtc))]))];
    }
}

internal static class TraceBatchEndpoint
{
    public static void Map(RouteGroupBuilder admin) =>
        admin.MapGet("/batches", async ([AsParameters] TraceBatchQuery query, TraceBatchHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(query, ct)))
            .WithValidation<TraceBatchQuery>()
            .WithName("TraceBatch");
}
