using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Payments.Contracts;
using PerfumeShop.Modules.Payments.Domain;

namespace PerfumeShop.Modules.Payments.Infrastructure;

internal sealed class PaymentsApi(PaymentsDbContext db) : IPaymentsApi
{
    public async Task<PaymentSummaryDto?> GetPaymentForOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var p = await db.Payments.AsNoTracking().FirstOrDefaultAsync(x => x.OrderId == orderId, cancellationToken);
        return p is null ? null : new PaymentSummaryDto(p.Id, p.OrderId, p.OrderNumber, p.Method, p.Status.ToString(),
            p.ExpectedAmount, p.CollectedAmount, p.CourierFee, p.NetReceived, p.SettlementId);
    }

    public async Task<IReadOnlyList<ReceiptSummaryDto>> GetReceiptsForOrderAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        await db.Receipts.AsNoTracking()
            .Where(r => r.OrderId == orderId)
            .OrderBy(r => r.CreatedAtUtc)
            .Select(r => new ReceiptSummaryDto(r.Id, r.Kind.ToString(), r.Status.ToString(), r.InternalNumber, r.FiscalNumber, r.IsFiscal, r.Total, r.IssuedAtUtc))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, ReceiptSummaryDto>> GetSaleReceiptsAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default)
    {
        if (orderIds.Count == 0)
        {
            return new Dictionary<Guid, ReceiptSummaryDto>();
        }

        var ids = orderIds.Distinct().ToList();
        return await db.Receipts.AsNoTracking()
            .Where(r => ids.Contains(r.OrderId) && r.Kind == ReceiptKind.Sale)
            .ToDictionaryAsync(
                r => r.OrderId,
                r => new ReceiptSummaryDto(r.Id, r.Kind.ToString(), r.Status.ToString(), r.InternalNumber, r.FiscalNumber, r.IsFiscal, r.Total, r.IssuedAtUtc),
                cancellationToken);
    }
}
