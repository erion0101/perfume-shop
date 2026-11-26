using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PerfumeShop.Modules.Payments.Infrastructure.Fiscal;

/// <summary>Section "Payments:Business": the seller printed on receipts. Placeholders until the company data is final.</summary>
/// <summary>Name, fiscal number, address and phone come from the business profile (Admin → Settings); only TimeZone is configuration.</summary>
internal sealed class BusinessInfo
{
    public const string SectionName = "Payments:Business";

    public string Name { get; set; } = "PerfumeShop";

    /// <summary>NUI / fiscal number of the business (Kosovo).</summary>
    public string TaxNumber { get; set; } = "000000000";

    public string Address { get; set; } = "Lipjan, Kosovë";

    public string Phone { get; set; } = "+383 44 000 000";

    public string TimeZone { get; set; } = "Europe/Belgrade";
}

internal sealed record FiscalLineRequest(string Description, string? Sku, decimal Quantity, decimal UnitPrice, decimal LineTotal, decimal VatRate, decimal VatAmount);

internal sealed record FiscalVatRequest(decimal Rate, decimal Net, decimal Vat, decimal Gross);

/// <summary>Everything a fiscal device/service needs; serialized and stored with every attempt.</summary>
internal sealed record FiscalReceiptRequest(
    Guid ReceiptId,
    string InternalNumber,
    string Kind,
    string OrderNumber,
    string? OriginalFiscalNumber,
    string SellerName,
    string SellerTaxNumber,
    IReadOnlyList<FiscalLineRequest> Lines,
    IReadOnlyList<FiscalVatRequest> VatBreakdown,
    decimal Total,
    decimal VatTotal,
    string Currency,
    string PaymentMethod,
    DateTime RequestedAtUtc);

/// <param name="IsFiscal">False when the receipt has no legal value (development).</param>
/// <param name="ResponseJson">The provider's raw answer, stored for audit.</param>
internal sealed record FiscalIssueResult(bool Succeeded, string? FiscalNumber, bool IsFiscal, string? ResponseJson, string? Error)
{
    public static FiscalIssueResult Failed(string error, string? responseJson = null) => new(false, null, false, responseJson, error);
}

/// <summary>
/// Issues receipts with the fiscal system. The real provider (fiscal device / certified software / tax authority API)
/// is not chosen yet: see docs/before-production.md. Outside Development nothing can be issued until it is.
/// </summary>
internal interface IFiscalReceiptService
{
    string Name { get; }

    Task<FiscalIssueResult> IssueAsync(FiscalReceiptRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// DEVELOPMENT ONLY. Stores the receipt with a "NONFISCAL-…" number and marks it NON-FISCAL: no legal value.
/// Registered only when the environment is Development (see PaymentsModule).
/// </summary>
internal sealed class DevelopmentFiscalReceiptService(ILogger<DevelopmentFiscalReceiptService> logger) : IFiscalReceiptService
{
    public const string ProviderName = "development";

    public string Name => ProviderName;

    public Task<FiscalIssueResult> IssueAsync(FiscalReceiptRequest request, CancellationToken cancellationToken)
    {
        var number = $"NONFISCAL-{request.InternalNumber}";
        logger.LogWarning("DEV receipt {Number} ({Kind}) for {Order}: {Total} EUR. NOT A FISCAL RECEIPT.", number, request.Kind, request.OrderNumber, request.Total);
        var response = JsonSerializer.Serialize(new { provider = ProviderName, fiscalNumber = number, nonFiscal = true, warning = "NOT A FISCAL RECEIPT - development only" });
        return Task.FromResult(new FiscalIssueResult(true, number, IsFiscal: false, response, null));
    }
}

/// <summary>Used outside Development until a real provider exists: every receipt fails loudly (error log + Failed status).</summary>
internal sealed class UnconfiguredFiscalReceiptService(ILogger<UnconfiguredFiscalReceiptService> logger) : IFiscalReceiptService
{
    public const string Message = "No fiscal receipt provider is configured. Receipts cannot be issued outside Development until one is chosen.";

    public string Name => "unconfigured";

    public Task<FiscalIssueResult> IssueAsync(FiscalReceiptRequest request, CancellationToken cancellationToken)
    {
        logger.LogError("Fiscal receipt {Number} for order {Order} NOT issued: {Message}", request.InternalNumber, request.OrderNumber, Message);
        return Task.FromResult(FiscalIssueResult.Failed(Message));
    }
}

/// <summary>The business details as they are now: the profile edited by Admin, plus the configured time zone.</summary>
internal sealed class BusinessDetails(PerfumeShop.Modules.Administration.Contracts.IBusinessProfileApi profile, Microsoft.Extensions.Options.IOptions<BusinessInfo> options)
{
    public async Task<BusinessInfo> GetAsync(CancellationToken cancellationToken)
    {
        var p = await profile.GetAsync(cancellationToken);
        return new BusinessInfo { Name = p.ShopName, TaxNumber = p.TaxNumber, Address = p.Address, Phone = p.Phone, TimeZone = options.Value.TimeZone };
    }
}
