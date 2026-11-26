using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PerfumeShop.Modules.Payments.Domain;
using PerfumeShop.Modules.Payments.Features.Receipts;
using PerfumeShop.Modules.Payments.Infrastructure;
using PerfumeShop.Modules.Payments.Infrastructure.Fiscal;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Payments.Features.Payments;

internal sealed record PaymentDto(
    Guid Id,
    Guid OrderId,
    string OrderNumber,
    string Method,
    string Status,
    decimal ExpectedAmount,
    decimal? CollectedAmount,
    decimal? CourierFee,
    decimal? NetReceived,
    string Currency,
    string? CourierName,
    string? TrackingNumber,
    DateTime PlacedAtUtc,
    DateTime? ShippedAtUtc,
    DateTime? CollectedAtUtc,
    Guid? SettlementId,
    string? SettlementReference,
    DateTime? SettledAtUtc);

internal sealed record PaymentWithReceiptsDto(PaymentDto Payment, IReadOnlyList<ReceiptDto> Receipts);

/// <param name="From">Placed on or after (Kosovo day).</param>
/// <param name="Courier">Part of the courier name.</param>
internal sealed record ListPaymentsQuery(string? Status, DateOnly? From, DateOnly? To, string? Courier, string? OrderNumber, int Page = 1, int PageSize = 50);

internal sealed class ListPaymentsValidator : AbstractValidator<ListPaymentsQuery>
{
    public ListPaymentsValidator()
    {
        RuleFor(x => x.Status).IsEnumName(typeof(PaymentStatus), caseSensitive: false).When(x => x.Status is not null);
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).When(x => x.From is not null && x.To is not null);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}

/// <param name="Expected">Cash to collect for parcels shipped that day.</param>
/// <param name="Collected">Cash booked as collected that day.</param>
/// <param name="Fees">Courier COD fees on statements imported that day.</param>
/// <param name="Net">What reached us on statements imported that day (collected − fees).</param>
/// <param name="OutstandingAtCourier">Now: shipped, not collected yet.</param>
internal sealed record CourierCashDto(string Courier, int ShippedCount, decimal Expected, int CollectedCount, decimal Collected, decimal Fees, decimal Net, decimal OutstandingAtCourier);

/// <param name="OutstandingAtCouriers">Shipped, cash not collected yet (all couriers).</param>
internal sealed record PaymentTotalsDto(DateOnly Today, decimal CollectedToday, decimal CollectedThisWeek, DateOnly WeekStart, decimal OutstandingAtCouriers, int FailedReceipts);

internal sealed record DailyCashSummaryDto(DateOnly Date, string Currency, IReadOnlyList<CourierCashDto> Couriers, CourierCashDto Total, int FailedReceipts);

internal sealed class PaymentsHandler(PaymentsDbContext db, IOptions<BusinessInfo> business) : IHandler
{
    private TimeZoneInfo Zone => TimeZoneInfo.FindSystemTimeZoneById(business.Value.TimeZone);

    public async Task<Result<PaymentWithReceiptsDto>> GetByOrderAsync(Guid orderId, CancellationToken ct)
    {
        var payment = await db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
        if (payment is null)
        {
            return Error.NotFound("Payments.Payment.NotFound", "No payment for this order (yet). Events are processed every few seconds.");
        }

        var receipts = await db.Receipts.AsNoTracking().Include(r => r.Attempts).Where(r => r.OrderId == orderId).OrderBy(r => r.CreatedAtUtc).ToListAsync(ct);
        return new PaymentWithReceiptsDto(payment.ToDto(), [.. receipts.Select(r => r.ToDto())]);
    }

    public async Task<PagedResponse<PaymentDto>> ListAsync(ListPaymentsQuery query, CancellationToken ct)
    {
        var payments = db.Payments.AsNoTracking();
        if (Enum.TryParse<PaymentStatus>(query.Status, ignoreCase: true, out var status))
        {
            payments = payments.Where(p => p.Status == status);
        }

        if (query.From is { } from)
        {
            var start = StartUtc(from);
            payments = payments.Where(p => p.PlacedAtUtc >= start);
        }

        if (query.To is { } to)
        {
            var end = StartUtc(to.AddDays(1));
            payments = payments.Where(p => p.PlacedAtUtc < end);
        }

        if (!string.IsNullOrWhiteSpace(query.Courier))
        {
            var courier = query.Courier.Trim().ToLowerInvariant();
            payments = payments.Where(p => p.CourierName != null && p.CourierName.ToLower().Contains(courier));
        }

        if (!string.IsNullOrWhiteSpace(query.OrderNumber))
        {
            var number = query.OrderNumber.Trim().ToUpperInvariant();
            payments = payments.Where(p => p.OrderNumber.Contains(number));
        }

        var total = await payments.CountAsync(ct);
        var page = await payments.OrderByDescending(p => p.PlacedAtUtc).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new PagedResponse<PaymentDto>([.. page.Select(p => p.ToDto())], query.Page, query.PageSize, total);
    }

    /// <summary>One Kosovo calendar day: shipped (to collect) vs collected vs net received, per courier.</summary>
    /// <summary>Dashboard money figures (Kosovo days; the week starts on Monday).</summary>
    public async Task<PaymentTotalsDto> TotalsAsync(TimeProvider time, CancellationToken ct)
    {
        var today = Today(time);
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var (todayStart, weekStart) = (StartUtc(today), StartUtc(monday));
        var collected = await db.Payments.AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Collected && p.CollectedAtUtc >= weekStart)
            .Select(p => new { p.CollectedAtUtc, Amount = p.CollectedAmount ?? 0 })
            .ToListAsync(ct);
        var outstanding = await db.Payments.AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Expected && p.ShippedAtUtc != null)
            .SumAsync(p => (decimal?)p.ExpectedAmount, ct) ?? 0;
        var failed = await db.Receipts.CountAsync(r => r.Status == ReceiptStatus.Failed, ct);
        return new PaymentTotalsDto(
            today,
            collected.Where(c => c.CollectedAtUtc >= todayStart).Sum(c => c.Amount),
            collected.Sum(c => c.Amount),
            monday,
            outstanding,
            failed);
    }

    public async Task<DailyCashSummaryDto> DailySummaryAsync(DateOnly day, CancellationToken ct)
    {
        var start = StartUtc(day);
        var end = StartUtc(day.AddDays(1));
        var rows = await db.Payments.AsNoTracking()
            .Where(p => (p.ShippedAtUtc >= start && p.ShippedAtUtc < end)
                || (p.CollectedAtUtc >= start && p.CollectedAtUtc < end)
                || (p.SettledAtUtc >= start && p.SettledAtUtc < end)
                || (p.Status == PaymentStatus.Expected && p.ShippedAtUtc != null))
            .ToListAsync(ct);

        static bool In(DateTime? at, DateTime s, DateTime e) => at >= s && at < e;
        CourierCashDto Summarize(string courier, IEnumerable<Payment> group)
        {
            var list = group.ToList();
            var shipped = list.Where(p => In(p.ShippedAtUtc, start, end)).ToList();
            var collected = list.Where(p => In(p.CollectedAtUtc, start, end)).ToList();
            var settled = list.Where(p => In(p.SettledAtUtc, start, end)).ToList();
            return new CourierCashDto(courier,
                shipped.Count, shipped.Sum(p => p.ExpectedAmount),
                collected.Count, collected.Sum(p => p.CollectedAmount ?? 0),
                settled.Sum(p => p.CourierFee ?? 0), settled.Sum(p => p.NetReceived ?? 0),
                list.Where(p => p.Status == PaymentStatus.Expected && p.ShippedAtUtc != null).Sum(p => p.ExpectedAmount));
        }

        var couriers = rows.GroupBy(p => p.CourierName ?? "(no courier)").OrderBy(g => g.Key).Select(g => Summarize(g.Key, g)).ToList();
        var failed = await db.Receipts.CountAsync(r => r.Status == ReceiptStatus.Failed, ct);
        return new DailyCashSummaryDto(day, FiscalReceipt.Currency, couriers, Summarize("Total", rows), failed);
    }

    private DateTime StartUtc(DateOnly day) => TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), Zone);

    internal DateOnly Today(TimeProvider time) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(time.GetUtcNow().UtcDateTime, Zone));
}

internal static class PaymentMappings
{
    public static PaymentDto ToDto(this Payment p) => new(
        p.Id, p.OrderId, p.OrderNumber, p.Method, p.Status.ToString(), p.ExpectedAmount, p.CollectedAmount, p.CourierFee, p.NetReceived, FiscalReceipt.Currency,
        p.CourierName, p.TrackingNumber, p.PlacedAtUtc, p.ShippedAtUtc, p.CollectedAtUtc, p.SettlementId, p.SettlementReference, p.SettledAtUtc);
}

internal static class PaymentsEndpoint
{
    public static void Map(RouteGroupBuilder staff)
    {
        staff.MapGet("/orders/{orderId:guid}", async (Guid orderId, PaymentsHandler h, CancellationToken ct) => (await h.GetByOrderAsync(orderId, ct)).ToOk())
            .WithName("GetPaymentByOrder");

        staff.MapGet("/", async ([AsParameters] ListPaymentsQuery query, PaymentsHandler h, CancellationToken ct) => TypedResults.Ok(await h.ListAsync(query, ct)))
            .WithValidation<ListPaymentsQuery>()
            .WithName("ListPayments");

        staff.MapGet("/totals", async (PaymentsHandler h, TimeProvider time, CancellationToken ct) => TypedResults.Ok(await h.TotalsAsync(time, ct)))
            .WithName("PaymentTotals")
            .WithDescription("Collected today and this week (Kosovo time), cash still at couriers, failed receipts.");

        staff.MapGet("/daily-summary", async (DateOnly? date, PaymentsHandler h, TimeProvider time, CancellationToken ct) =>
                TypedResults.Ok(await h.DailySummaryAsync(date ?? h.Today(time), ct)))
            .WithName("DailyCashSummary")
            .WithDescription("Per courier for one Kosovo day: expected (shipped), collected, fees, net received, still at the courier.");
    }
}
