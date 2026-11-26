using System.Globalization;
using System.Net;
using System.Text;
using PerfumeShop.Modules.Shipping.Domain;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Shipping.Infrastructure.Couriers;

/// <summary>
/// CSV in, CSV + printable HTML out. Expected files:
/// <list type="bullet">
/// <item>Status updates: <c>tracking_number;status;note</c> — status Delivered/Dorëzuar, Refused/Refuzuar/Kthyer, InTransit/Në rrugë, HandedOver/Marrë.</item>
/// <item>Remittance: <c>tracking_number;collected_amount;fee</c> — amounts in EUR, decimal point or comma.</item>
/// </list>
/// </summary>
internal sealed class ManualCourierAdapter : ICourierAdapter
{
    private static readonly string[] TrackingColumns = ["tracking_number", "tracking", "awb", "nr_gjurmimit", "kodi"];
    private static readonly string[] StatusColumns = ["status", "statusi", "gjendja"];
    private static readonly string[] NoteColumns = ["note", "shenim", "shënim", "koment"];
    private static readonly string[] AmountColumns = ["collected_amount", "amount", "shuma", "cod", "paguar"];
    private static readonly string[] FeeColumns = ["fee", "komisioni", "tarifa"];

    public string Code => Courier.ManualAdapter;

    public static ShipmentStatus? MapStatus(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "handedover" or "handed_over" or "picked_up" or "pickedup" or "marrë" or "marre" => ShipmentStatus.HandedOver,
        "intransit" or "in_transit" or "në rrugë" or "ne rruge" or "në_rrugë" => ShipmentStatus.InTransit,
        "delivered" or "dorëzuar" or "dorezuar" => ShipmentStatus.Delivered,
        "refused" or "refuzuar" or "returned" or "kthyer" or "rejected" => ShipmentStatus.Refused,
        _ => null,
    };

    public Result<IReadOnlyList<CourierStatusUpdate>> ParseStatusUpdates(string csv)
    {
        var rows = Csv.Read(csv);
        if (rows.Count == 0)
        {
            return Error.Validation("Shipping.Csv.Empty", "The file has no data rows (expected header: tracking_number;status;note).");
        }

        if (rows.All(r => r.Get(TrackingColumns) is null) || rows.All(r => r.Get(StatusColumns) is null))
        {
            return Error.Validation("Shipping.Csv.Columns", "Missing columns: tracking_number and status are required.");
        }

        return rows.Select(r =>
        {
            var raw = r.Get(StatusColumns) ?? string.Empty;
            return new CourierStatusUpdate(r.LineNumber, (r.Get(TrackingColumns) ?? string.Empty).ToUpperInvariant(), raw, MapStatus(raw), r.Get(NoteColumns));
        }).ToList();
    }

    public Result<IReadOnlyList<RemittanceLine>> ParseRemittance(string csv)
    {
        var rows = Csv.Read(csv);
        if (rows.Count == 0)
        {
            return Error.Validation("Shipping.Csv.Empty", "The file has no data rows (expected header: tracking_number;collected_amount;fee).");
        }

        List<RemittanceLine> lines = [];
        List<string> errors = [];
        foreach (var row in rows)
        {
            var tracking = row.Get(TrackingColumns);
            if (tracking is null)
            {
                errors.Add($"line {row.LineNumber}: tracking number missing");
                continue;
            }

            if (!Csv.TryParseAmount(row.Get(AmountColumns), out var amount) || amount < 0)
            {
                errors.Add($"line {row.LineNumber}: collected amount missing or invalid");
                continue;
            }

            var feeText = row.Get(FeeColumns);
            decimal fee = 0;
            if (feeText is not null && (!Csv.TryParseAmount(feeText, out fee) || fee < 0))
            {
                errors.Add($"line {row.LineNumber}: fee invalid");
                continue;
            }

            lines.Add(new RemittanceLine(row.LineNumber, tracking.ToUpperInvariant(), decimal.Round(amount, 2), decimal.Round(fee, 2)));
        }

        return errors.Count > 0
            ? Error.Validation("Shipping.Csv.Invalid", "The statement could not be read: " + string.Join("; ", errors.Take(20)))
            : lines;
    }

    public ManifestFile BuildManifest(Courier courier, DateOnly date, IReadOnlyList<Shipment> shipments, IReadOnlyDictionary<Guid, string> receiptStates, ShopInfo shop)
    {
        var ordered = shipments.OrderBy(s => s.Recipient.City).ThenBy(s => s.OrderNumber).ToList();
        string Receipt(Shipment s) => receiptStates.GetValueOrDefault(s.OrderId, "Missing") == "Issued"
            ? "OK"
            : $"{NoReceipt} ({receiptStates.GetValueOrDefault(s.OrderId, "Missing")})";
        var withoutReceipt = ordered.Count(s => Receipt(s) != "OK");

        string[] headers = ["Porosia", "Nr. gjurmimit", "Marrësi", "Telefoni", "Qyteti", "Adresa", "Pagesë në dorëzim (EUR)", "Pako", "Kupon fiskal", "Shënim"];
        var csv = Csv.Write(headers, ordered.Select(s => (IReadOnlyList<string?>)
        [
            s.OrderNumber, s.TrackingNumber, s.Recipient.FullName, s.Recipient.Phone, s.Recipient.City,
            Address(s), Csv.Amount(s.CodAmount), s.ParcelCount.ToString(CultureInfo.InvariantCulture), Receipt(s), Notes(s),
        ]));

        var total = ordered.Sum(s => s.CodAmount);
        var html = new StringBuilder();
        html.Append(HtmlPage.Start($"Lista e dorëzimit – {courier.Name} – {date:dd.MM.yyyy}"));
        html.Append(CultureInfo.InvariantCulture, $"""
            <header>
              <div><h1>Lista e dorëzimit</h1><p>{E(shop.Name)} · {E(shop.Address)} · {E(shop.Phone)}</p></div>
              <div class="right"><p><strong>Korrieri:</strong> {E(courier.Name)}</p><p><strong>Data:</strong> {date:dd.MM.yyyy}</p>
              <p><strong>Pako:</strong> {ordered.Sum(s => s.ParcelCount)} · <strong>Gjithsej për t'u mbledhur:</strong> {total:0.00} €</p></div>
            </header>
            <p class="warning">Përmban parfum – lëng i ndezshëm, UN1266, klasa 3. Vetëm transport rrugor.</p>
            {(withoutReceipt == 0 ? string.Empty : $"<p class=\"warning no-receipt\">KUJDES: {withoutReceipt} pako PA KUPON FISKAL të lëshuar. Mos i dorëzoni pa leje (override me arsye).</p>")}
            <table>
              <thead><tr><th>#</th><th>Porosia</th><th>Nr. gjurmimit</th><th>Marrësi</th><th>Telefoni</th><th>Qyteti</th><th>Adresa</th><th class="num">COD €</th><th>Pako</th><th>Kupon fiskal</th><th>Shënim</th></tr></thead>
              <tbody>
            """);
        var index = 1;
        foreach (var s in ordered)
        {
            var receipt = Receipt(s);
            var rowClass = receipt == "OK" ? string.Empty : " class=\"no-receipt\"";
            html.Append(CultureInfo.InvariantCulture, $"<tr{rowClass}><td>{index++}</td><td>{E(s.OrderNumber)}</td><td>{E(s.TrackingNumber)}</td><td>{E(s.Recipient.FullName)}</td><td>{E(s.Recipient.Phone)}</td><td>{E(s.Recipient.City)}</td><td>{E(Address(s))}</td><td class=\"num\">{s.CodAmount:0.00}</td><td>{s.ParcelCount}</td><td>{(receipt == "OK" ? "OK" : $"<strong>{E(receipt)}</strong>")}</td><td>{E(Notes(s))}</td></tr>\n");
        }

        html.Append(CultureInfo.InvariantCulture, $"""
              </tbody>
              <tfoot><tr><td colspan="7">Gjithsej</td><td class="num">{total:0.00}</td><td>{ordered.Sum(s => s.ParcelCount)}</td><td></td><td></td></tr></tfoot>
            </table>
            <div class="signatures"><div>Dorëzoi (dyqani): ____________________</div><div>Pranoi (korrieri): ____________________</div></div>
            """);
        html.Append(HtmlPage.End);

        return new ManifestFile($"manifest-{Slug(courier.Name)}-{date:yyyy-MM-dd}.csv", csv, html.ToString());
    }

    /// <summary>Printed for parcels whose sale receipt is not issued.</summary>
    public const string NoReceipt = "PA KUPON";

    private static string Address(Shipment s) => $"{s.Recipient.Street}" + (s.Recipient.Notes is { } n ? $" ({n})" : string.Empty);

    private static string? Notes(Shipment s) => s.Note;

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static string Slug(string value) =>
        new string([.. value.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')]).Trim('-');
}

/// <summary>Shared print styles (A4, black on white).</summary>
internal static class HtmlPage
{
    public static string Start(string title) => $$"""
        <!doctype html>
        <html lang="sq"><head><meta charset="utf-8"><title>{{WebUtility.HtmlEncode(title)}}</title>
        <style>
          @page { size: A4; margin: 12mm; }
          body { font-family: Arial, Helvetica, sans-serif; font-size: 11pt; color: #000; }
          header { display: flex; justify-content: space-between; border-bottom: 2px solid #000; margin-bottom: 8px; }
          h1 { font-size: 18pt; margin: 0 0 4px; } p { margin: 2px 0; } .right { text-align: right; }
          table { width: 100%; border-collapse: collapse; margin-top: 8px; }
          th, td { border: 1px solid #555; padding: 4px 6px; text-align: left; vertical-align: top; }
          th { background: #eee; } .num { text-align: right; white-space: nowrap; }
          tfoot td { font-weight: bold; }
          .warning { border: 2px solid #000; padding: 4px 8px; font-weight: bold; }
          .no-receipt td, p.no-receipt { background: #fdd; }
          .cod { font-size: 20pt; font-weight: bold; border: 3px solid #000; padding: 8px; text-align: center; margin: 12px 0; }
          .notice { font-size: 14pt; font-weight: bold; text-align: center; margin: 8px 0; }
          .signatures { display: flex; justify-content: space-between; margin-top: 40px; }
          @media print { .noprint { display: none; } }
        </style></head><body>
        <p class="noprint"><button onclick="window.print()">Printo</button></p>
        """;

    public const string End = "</body></html>";
}
