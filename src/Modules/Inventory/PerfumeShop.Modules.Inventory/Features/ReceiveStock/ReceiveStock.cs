using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PerfumeShop.Modules.Catalog.Contracts;
using PerfumeShop.Modules.Inventory.Domain;
using PerfumeShop.Modules.Inventory.Features.Shared;
using PerfumeShop.Modules.Inventory.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Inventory.Features.ReceiveStock;

/// <param name="UnitCost">Purchase price per unit, EUR.</param>
internal sealed record ReceiveStockLine(Guid VariantId, string BatchCode, int Quantity, decimal UnitCost);

/// <summary>One delivery from one supplier invoice; each line becomes a batch.</summary>
internal sealed record ReceiveStockRequest(
    string SupplierName,
    string SupplierInvoiceNumber,
    DateOnly SupplierInvoiceDate,
    DateOnly ReceivedOn,
    List<ReceiveStockLine> Lines);

internal sealed record ReceivedBatchDto(Guid BatchId, Guid VariantId, string BatchCode, int Quantity, int OnHandAfter);

internal sealed record ReceiveStockResponse(Guid ReceiptId, IReadOnlyList<ReceivedBatchDto> Batches);

internal sealed class ReceiveStockValidator : AbstractValidator<ReceiveStockRequest>
{
    public ReceiveStockValidator()
    {
        RuleFor(x => x.SupplierName).NotEmpty().MaximumLength(StockBatch.SupplierMaxLength);
        RuleFor(x => x.SupplierInvoiceNumber).NotEmpty().MaximumLength(StockBatch.InvoiceNumberMaxLength);
        RuleFor(x => x.SupplierInvoiceDate).NotEmpty();
        RuleFor(x => x.ReceivedOn).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty().Must(l => l.Count <= 200);
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.VariantId).NotEmpty();
            line.RuleFor(l => l.BatchCode).NotEmpty().MaximumLength(StockBatch.BatchCodeMaxLength);
            line.RuleFor(l => l.Quantity).InclusiveBetween(1, StockItem.MaxQuantity);
            line.RuleFor(l => l.UnitCost).GreaterThanOrEqualTo(0).PrecisionScale(10, 2, ignoreTrailingZeros: true);
        });
    }
}

/// <summary>Goods receipt. Variants must exist in Catalog; their stock item is created on first receipt.</summary>
internal sealed class ReceiveStockHandler(
    InventoryDbContext db,
    StockTransactions transactions,
    ICatalogApi catalog,
    IOptions<InventoryOptions> options,
    TimeProvider timeProvider) : IHandler
{
    public async Task<Result<ReceiveStockResponse>> HandleAsync(ReceiveStockRequest request, string actor, CancellationToken cancellationToken)
    {
        var variantIds = request.Lines.Select(l => l.VariantId).Distinct().ToList();
        var known = await catalog.GetVariantSummariesAsync(variantIds, cancellationToken);
        var unknown = variantIds.Where(id => !known.ContainsKey(id)).ToList();
        if (unknown.Count > 0)
        {
            return InventoryFeatureErrors.UnknownVariants(unknown);
        }

        var receipt = new GoodsReceipt(Guid.CreateVersion7(), request.SupplierName, request.SupplierInvoiceNumber, request.SupplierInvoiceDate, request.ReceivedOn);

        return await transactions.RunAsync<ReceiveStockResponse>(async ct =>
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var items = await db.StockItems.Include(i => i.Batches).Where(i => variantIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
            List<ReceivedBatchDto> received = [];

            foreach (var line in request.Lines)
            {
                if (!items.TryGetValue(line.VariantId, out var item))
                {
                    item = StockItem.Create(line.VariantId, options.Value.DefaultLowStockThreshold).Value;
                    db.StockItems.Add(item);
                    items[item.Id] = item;
                }

                var cost = Money.Euro(line.UnitCost);
                if (cost.IsFailure)
                {
                    return InventoryErrors.UnitCostInvalid;
                }

                var batch = item.Receive(receipt, line.BatchCode, line.Quantity, cost.Value, actor, now);
                if (batch.IsFailure)
                {
                    return batch.Error;
                }

                received.Add(new ReceivedBatchDto(batch.Value.Id, item.Id, batch.Value.BatchCode, line.Quantity, item.OnHand));
            }

            await db.SaveChangesAsync(ct);
            return new ReceiveStockResponse(receipt.ReceiptId, received);
        }, cancellationToken);
    }
}

internal static class ReceiveStockEndpoint
{
    public static void Map(RouteGroupBuilder admin) =>
        admin.MapPost("/receipts", async (ReceiveStockRequest request, ClaimsPrincipal user, ReceiveStockHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(request, user.Actor(), ct)).ToCreated(r => $"/api/admin/inventory/receipts/{r.ReceiptId}"))
            .WithValidation<ReceiveStockRequest>()
            .WithName("ReceiveStock")
            .Produces<ReceiveStockResponse>(StatusCodes.Status201Created);
}
