using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PerfumeShop.Modules.Payments.Domain;
using PerfumeShop.Modules.Payments.Infrastructure;
using PerfumeShop.Modules.Payments.Infrastructure.Fiscal;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Payments.Features.Receipts;

internal sealed record ReceiptAttemptDto(DateTime AtUtc, string Provider, bool Succeeded, string? Error, string Request, string? Response);

internal sealed record ReceiptDto(
    Guid Id,
    string InternalNumber,
    string Kind,
    string Status,
    Guid OrderId,
    string OrderNumber,
    Guid? OriginalReceiptId,
    string? OriginalInternalNumber,
    bool IsFiscal,
    string? FiscalNumber,
    string? Provider,
    decimal Total,
    decimal VatTotal,
    string Currency,
    IReadOnlyList<ReceiptLine> Lines,
    IReadOnlyList<VatGroup> VatBreakdown,
    DateTime CreatedAtUtc,
    DateTime? IssuedAtUtc,
    string? LastError,
    string? VoidReason,
    IReadOnlyList<ReceiptAttemptDto> Attempts);

internal sealed record ListReceiptsQuery(string? Status, string? Kind, string? OrderNumber, int Page = 1, int PageSize = 50);

internal sealed class ListReceiptsValidator : AbstractValidator<ListReceiptsQuery>
{
    public ListReceiptsValidator()
    {
        RuleFor(x => x.Status).IsEnumName(typeof(ReceiptStatus), caseSensitive: false).When(x => x.Status is not null);
        RuleFor(x => x.Kind).IsEnumName(typeof(ReceiptKind), caseSensitive: false).When(x => x.Kind is not null);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}

internal static class ReceiptMappings
{
    public static ReceiptDto ToDto(this FiscalReceipt r) => new(
        r.Id, r.InternalNumber, r.Kind.ToString(), r.Status.ToString(), r.OrderId, r.OrderNumber, r.OriginalReceiptId, r.OriginalInternalNumber,
        r.IsFiscal, r.FiscalNumber, r.Provider, r.Total, r.VatTotal, FiscalReceipt.Currency, r.Lines, r.VatBreakdown, r.CreatedAtUtc, r.IssuedAtUtc,
        r.LastError, r.VoidReason,
        [.. r.Attempts.OrderBy(a => a.AttemptedAtUtc).Select(a => new ReceiptAttemptDto(a.AttemptedAtUtc, a.Provider, a.Succeeded, a.Error, a.RequestJson, a.ResponseJson))]);
}

internal sealed class ReceiptsHandler(PaymentsDbContext db, ReceiptIssuer issuer, IOptions<BusinessInfo> business) : IHandler
{
    private static readonly Error NotFound = Error.NotFound("Payments.Receipt.NotFound", "Receipt not found.");

    public async Task<PagedResponse<ReceiptDto>> ListAsync(ListReceiptsQuery query, CancellationToken ct)
    {
        var receipts = db.Receipts.AsNoTracking();
        if (Enum.TryParse<ReceiptStatus>(query.Status, ignoreCase: true, out var status))
        {
            receipts = receipts.Where(r => r.Status == status);
        }

        if (Enum.TryParse<ReceiptKind>(query.Kind, ignoreCase: true, out var kind))
        {
            receipts = receipts.Where(r => r.Kind == kind);
        }

        if (!string.IsNullOrWhiteSpace(query.OrderNumber))
        {
            var number = query.OrderNumber.Trim().ToUpperInvariant();
            receipts = receipts.Where(r => r.OrderNumber.Contains(number));
        }

        var total = await receipts.CountAsync(ct);
        var page = await receipts.Include(r => r.Attempts).OrderByDescending(r => r.CreatedAtUtc)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new PagedResponse<ReceiptDto>([.. page.Select(r => r.ToDto())], query.Page, query.PageSize, total);
    }

    public async Task<Result<ReceiptDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var receipt = await db.Receipts.AsNoTracking().Include(r => r.Attempts).FirstOrDefaultAsync(r => r.Id == id, ct);
        return receipt is null ? NotFound : receipt.ToDto();
    }

    public async Task<Result<string>> PrintAsync(Guid id, CancellationToken ct)
    {
        var receipt = await db.Receipts.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        return receipt is null ? NotFound : ReceiptPrint.Render(receipt, business.Value);
    }

    /// <summary>Sends a Failed (or stuck Pending) receipt to the provider again; the previous attempts stay in the history.</summary>
    public async Task<Result<ReceiptDto>> RetryAsync(Guid id, CancellationToken ct)
    {
        var receipt = await db.Receipts.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (receipt is null)
        {
            return NotFound;
        }

        if (!receipt.CanBeIssued)
        {
            return Error.Conflict("Payments.Receipt.NotRetryable", $"Receipt {receipt.InternalNumber} is {receipt.Status}; only Failed or Pending receipts can be retried.");
        }

        var issued = await issuer.IssueAsync(id, ct);
        return issued.IsSuccess ? issued.Value.ToDto() : issued.Error;
    }
}

internal static class ReceiptsEndpoint
{
    public static void Map(RouteGroupBuilder staff)
    {
        staff.MapGet("/receipts", async ([AsParameters] ListReceiptsQuery query, ReceiptsHandler h, CancellationToken ct) => TypedResults.Ok(await h.ListAsync(query, ct)))
            .WithValidation<ListReceiptsQuery>()
            .WithName("ListReceipts");

        staff.MapGet("/receipts/{id:guid}", async (Guid id, ReceiptsHandler h, CancellationToken ct) => (await h.GetAsync(id, ct)).ToOk())
            .WithName("GetReceipt");

        staff.MapGet("/receipts/{id:guid}/print", async (Guid id, ReceiptsHandler h, CancellationToken ct) =>
            {
                var html = await h.PrintAsync(id, ct);
                return html.IsSuccess ? TypedResults.Content(html.Value, "text/html; charset=utf-8") : (IResult)html.Error.ToProblem();
            })
            .Produces<string>(StatusCodes.Status200OK, "text/html")
            .WithName("PrintReceipt");

        staff.MapPost("/receipts/{id:guid}/retry", async (Guid id, ReceiptsHandler h, CancellationToken ct) => (await h.RetryAsync(id, ct)).ToOk())
            .WithName("RetryFailedReceipt");
    }
}
