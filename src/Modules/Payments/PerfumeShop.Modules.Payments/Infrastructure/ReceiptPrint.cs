using System.Globalization;
using System.Net;
using System.Text;
using PerfumeShop.Modules.Payments.Domain;
using PerfumeShop.Modules.Payments.Infrastructure.Fiscal;

namespace PerfumeShop.Modules.Payments.Infrastructure;

/// <summary>Printable receipt for the packing step (A5/thermal friendly). Non-fiscal receipts are marked clearly.</summary>
internal static class ReceiptPrint
{
    public const string NonFiscalBanner = "JO FISKAL – NON-FISCAL – VETËM PËR TESTIM";

    public static string Render(FiscalReceipt r, BusinessInfo business)
    {
        static string E(string? v) => WebUtility.HtmlEncode(v ?? string.Empty);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(business.TimeZone);
        var issued = r.IssuedAtUtc is { } at ? TimeZoneInfo.ConvertTimeFromUtc(at, zone).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) : "—";
        var title = r.Kind == ReceiptKind.Storno ? "KUPON STORNO" : "KUPON";

        var html = new StringBuilder();
        html.Append(CultureInfo.InvariantCulture, $$"""
            <!doctype html>
            <html lang="sq"><head><meta charset="utf-8"><title>{{E(title)}} {{E(r.InternalNumber)}}</title>
            <style>
              @page { size: A5; margin: 8mm; }
              body { font-family: "Courier New", monospace; font-size: 10pt; color: #000; max-width: 120mm; }
              h1 { font-size: 14pt; text-align: center; margin: 4px 0; } p { margin: 2px 0; } .c { text-align: center; }
              table { width: 100%; border-collapse: collapse; } td, th { padding: 2px 0; vertical-align: top; text-align: left; }
              .n { text-align: right; white-space: nowrap; } .t td { border-top: 1px dashed #000; font-weight: bold; }
              .banner { border: 3px solid #c00; color: #c00; font-weight: bold; text-align: center; padding: 6px; margin: 6px 0; font-size: 12pt; }
              .small { font-size: 8pt; } @media print { .noprint { display: none; } }
            </style></head><body>
            <p class="noprint"><button onclick="window.print()">Printo</button></p>
            """);
        if (!r.IsFiscal)
        {
            html.Append(CultureInfo.InvariantCulture, $"<div class=\"banner\">{NonFiscalBanner}</div>\n");
        }

        if (r.Status != ReceiptStatus.Issued)
        {
            html.Append(CultureInfo.InvariantCulture, $"<div class=\"banner\">NUK ËSHTË LËSHUAR – status: {E(r.Status.ToString())}</div>\n");
        }

        html.Append(CultureInfo.InvariantCulture, $"""
            <h1>{E(business.Name)}</h1>
            <p class="c">{E(business.Address)} · Tel: {E(business.Phone)}</p>
            <p class="c">NUI: {E(business.TaxNumber)}</p>
            <h1>{E(title)}</h1>
            <p>Nr.: {E(r.InternalNumber)}{(r.FiscalNumber is { } fiscal ? $" · Nr. fiskal: {E(fiscal)}" : string.Empty)}</p>
            {(r.OriginalInternalNumber is { } original ? $"<p>Anulon kuponin: {E(original)}</p>" : string.Empty)}
            <p>Porosia: {E(r.OrderNumber)} · Data: {issued}</p>
            <table><thead><tr><th>Artikulli</th><th class="n">Sasia</th><th class="n">Çmimi</th><th class="n">Vlera</th></tr></thead><tbody>
            """);
        foreach (var line in r.Lines)
        {
            html.Append(CultureInfo.InvariantCulture, $"<tr><td>{E(line.Description)}{(line.Batches is { } b ? $"<br><span class=\"small\">Batch: {E(b)}</span>" : string.Empty)}</td><td class=\"n\">{line.Quantity:0.##}</td><td class=\"n\">{line.UnitPrice:0.00}</td><td class=\"n\">{line.LineTotal:0.00}</td></tr>\n");
        }

        html.Append(CultureInfo.InvariantCulture, $"""
            <tr class="t"><td colspan="3">GJITHSEJ (EUR)</td><td class="n">{r.Total:0.00}</td></tr>
            </tbody></table>
            <table><thead><tr><th>TVSH</th><th class="n">Pa TVSH</th><th class="n">TVSH</th><th class="n">Me TVSH</th></tr></thead><tbody>
            """);
        foreach (var vat in r.VatBreakdown)
        {
            html.Append(CultureInfo.InvariantCulture, $"<tr><td>{vat.Rate * 100:0}%</td><td class=\"n\">{vat.Net:0.00}</td><td class=\"n\">{vat.Vat:0.00}</td><td class=\"n\">{vat.Gross:0.00}</td></tr>\n");
        }

        html.Append(CultureInfo.InvariantCulture, $"""
            </tbody></table>
            <p>Pagesa: para në dorë në dorëzim (COD)</p>
            <p class="c small">Faleminderit!</p>
            </body></html>
            """);
        return html.ToString();
    }
}
