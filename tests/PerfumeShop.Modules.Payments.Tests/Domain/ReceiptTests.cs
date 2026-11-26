using Microsoft.Extensions.Logging.Abstractions;
using PerfumeShop.Modules.Payments.Domain;
using PerfumeShop.Modules.Payments.Infrastructure;
using PerfumeShop.Modules.Payments.Infrastructure.Fiscal;
using static PerfumeShop.Modules.Payments.Tests.TestHelpers;

namespace PerfumeShop.Modules.Payments.Tests.Domain;

public sealed class ReceiptTests
{
    private static FiscalReceipt Sale(decimal deliveryFee = 2.00m) =>
        FiscalReceipt.Sale("PS-R-2026-000001", Guid.NewGuid(), "PS-2026-000042", "CashOnDelivery", ReceiptContent.FromOrder(Order(deliveryFee))).Ok();

    // ---------- VAT ----------

    [Theory]
    [InlineData(118.00, 18.00)]
    [InlineData(39.90, 6.09)]   // 6.0864… → 6.09
    [InlineData(79.80, 12.17)]  // 12.1729… → 12.17
    [InlineData(2.00, 0.31)]    // 0.3050… → 0.31 (half away from zero)
    [InlineData(0.59, 0.09)]    // 0.0900
    [InlineData(0, 0)]
    public void Vat_is_the_18_percent_part_of_the_gross_price(decimal gross, decimal vat) =>
        Assert.Equal(vat, ReceiptVat.Of(gross));

    [Fact]
    public void Vat_is_rounded_per_line_and_summed_like_the_order()
    {
        var receipt = Sale();

        Assert.Equal([12.17m, 1.91m, 0.31m], receipt.Lines.Select(l => l.VatAmount));
        Assert.Equal((94.30m, 14.39m), (receipt.Total, receipt.VatTotal));
        var group = Assert.Single(receipt.VatBreakdown);
        Assert.Equal((0.18m, 79.91m, 14.39m, 94.30m), (group.Rate, group.Net, group.Vat, group.Gross));
        Assert.Equal(group.Gross, group.Net + group.Vat);
    }

    [Fact]
    public void Lines_describe_the_product_size_batches_and_delivery()
    {
        var lines = Sale().Lines;

        Assert.Equal(["Lattafa Khamrah 100 ml", "Ajmal Dahn Al Oudh 3 ml", "Transporti"], lines.Select(l => l.Description));
        Assert.Equal(("LAT-KHA-100", 2m, 39.90m, 79.80m, "B2391×2"), (lines[0].Sku, lines[0].Quantity, lines[0].UnitPrice, lines[0].LineTotal, lines[0].Batches));
        Assert.Null(lines[1].Batches);
    }

    [Fact]
    public void Free_delivery_has_no_delivery_line() =>
        Assert.DoesNotContain(Sale(deliveryFee: 0).Lines, l => l.Description == "Transporti");

    // ---------- storno ----------

    [Fact]
    public void Storno_needs_an_issued_sale()
    {
        FiscalReceipt.Storno("PS-R-2026-000002", Sale()).Fails("Payments.Receipt.StornoNeedsIssuedSale");
    }

    [Fact]
    public void Storno_is_the_exact_negative_of_the_sale_and_links_to_it()
    {
        var sale = Sale();
        sale.MarkIssued("development", "NONFISCAL-PS-R-2026-000001", false, Now);

        var storno = FiscalReceipt.Storno("PS-R-2026-000002", sale).Ok();

        Assert.Equal((ReceiptKind.Storno, sale.Id, "PS-R-2026-000001"), (storno.Kind, storno.OriginalReceiptId!.Value, storno.OriginalInternalNumber));
        Assert.Equal((-94.30m, -14.39m), (storno.Total, storno.VatTotal));
        Assert.Equal(sale.Lines.Select(l => -l.Quantity), storno.Lines.Select(l => l.Quantity));
        Assert.Equal(0m, sale.Total + storno.Total);
        Assert.Equal(-79.91m, storno.VatBreakdown.Single().Net);
    }

    [Fact]
    public void An_issued_receipt_is_never_voided_or_issued_twice()
    {
        var sale = Sale();
        sale.MarkIssued("development", "NF-1", false, Now);

        sale.Void("x").Fails("Payments.Receipt.AlreadyIssued");
        sale.MarkIssued("development", "NF-2", false, Now).Fails("Payments.Receipt.NotIssuable");
        Assert.False(sale.CanBeIssued);
    }

    // ---------- providers ----------

    [Fact]
    public async Task Development_provider_marks_receipts_non_fiscal()
    {
        var result = await new DevelopmentFiscalReceiptService(NullLogger<DevelopmentFiscalReceiptService>.Instance)
            .IssueAsync(Request(), TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.False(result.IsFiscal);
        Assert.Equal("NONFISCAL-PS-R-2026-000001", result.FiscalNumber);
        Assert.Contains("NOT A FISCAL RECEIPT", result.ResponseJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unconfigured_provider_fails_loudly()
    {
        var result = await new UnconfiguredFiscalReceiptService(NullLogger<UnconfiguredFiscalReceiptService>.Instance)
            .IssueAsync(Request(), TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(UnconfiguredFiscalReceiptService.Message, result.Error);
    }

    [Fact]
    public void Printed_receipt_shows_the_non_fiscal_banner_and_vat_breakdown()
    {
        var sale = Sale();
        sale.MarkIssued("development", "NONFISCAL-PS-R-2026-000001", false, Now);

        var html = System.Net.WebUtility.HtmlDecode(ReceiptPrint.Render(sale, new BusinessInfo()));

        Assert.Contains(ReceiptPrint.NonFiscalBanner, html, StringComparison.Ordinal);
        Assert.Contains("18%", html, StringComparison.Ordinal);
        Assert.Contains("79.91", html, StringComparison.Ordinal);
        Assert.Contains("Batch: B2391×2", html, StringComparison.Ordinal);
        Assert.DoesNotContain("NUK ËSHTË LËSHUAR", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Printed_receipt_that_failed_says_so()
    {
        var sale = Sale();
        sale.MarkFailed("unconfigured", "no provider");

        Assert.Contains("NUK ËSHTË LËSHUAR", System.Net.WebUtility.HtmlDecode(ReceiptPrint.Render(sale, new BusinessInfo())), StringComparison.Ordinal);
    }

    private static FiscalReceiptRequest Request() =>
        new(Guid.NewGuid(), "PS-R-2026-000001", "Sale", "PS-2026-000042", null, "PerfumeShop", "000000000", [], [], 94.30m, 14.39m, "EUR", "CashOnDelivery", Now);
}
