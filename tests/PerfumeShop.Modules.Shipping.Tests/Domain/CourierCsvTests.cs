using PerfumeShop.Modules.Shipping.Domain;
using PerfumeShop.Modules.Shipping.Infrastructure;
using PerfumeShop.Modules.Shipping.Infrastructure.Couriers;
using static PerfumeShop.Modules.Shipping.Tests.TestHelpers;

namespace PerfumeShop.Modules.Shipping.Tests.Domain;

public sealed class CourierCsvTests
{
    private readonly ManualCourierAdapter _adapter = new();

    private static string Sample(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", name));

    [Fact]
    public void The_status_sample_in_the_repo_parses()
    {
        var updates = _adapter.ParseStatusUpdates(Sample("courier-status-update.csv")).Ok();

        Assert.Equal(
            [("PK2026000001XK", ShipmentStatus.Delivered), ("PK2026000002XK", ShipmentStatus.Refused), ("PK2026000003XK", ShipmentStatus.InTransit), ("PK2026999999XK", ShipmentStatus.Delivered)],
            updates.Select(u => (u.TrackingNumber, u.Status!.Value)));
        Assert.Equal("Klienti nuk ishte në shtëpi; refuzoi në telefon", updates[1].Note); // quoted field with the delimiter inside
        Assert.Equal(2, updates[0].LineNumber);
    }

    [Fact]
    public void The_remittance_sample_in_the_repo_parses_with_decimal_commas()
    {
        var lines = _adapter.ParseRemittance(Sample("courier-remittance.csv")).Ok();

        Assert.Equal([("PK2026000001XK", 41.90m, 1.50m), ("PK2026000003XK", 38.00m, 1.50m), ("PK2026999999XK", 25.00m, 1.50m)],
            lines.Select(l => (l.TrackingNumber, l.CollectedAmount, l.Fee)));
    }

    [Fact]
    public void Comma_files_with_bom_and_english_headers_parse_too()
    {
        var csv = "﻿Tracking,Amount,Fee\r\n\"pk1\",\"1,234.50\",2.00\r\n\r\nPK2,40,\r\n";

        var lines = _adapter.ParseRemittance(csv).Ok();

        Assert.Equal([("PK1", 1234.50m, 2.00m), ("PK2", 40m, 0m)], lines.Select(l => (l.TrackingNumber, l.CollectedAmount, l.Fee)));
    }

    [Fact]
    public void Bad_rows_are_reported_with_their_line_numbers()
    {
        var result = _adapter.ParseRemittance("tracking_number;collected_amount\nPK1;abc\n;10\n");

        result.Fails("Shipping.Csv.Invalid");
        Assert.Contains("line 2", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("line 3", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_status_words_are_kept_for_the_report()
    {
        var update = _adapter.ParseStatusUpdates("tracking_number;status\nPK1;lost in space\n").Ok().Single();

        Assert.Null(update.Status);
        Assert.Equal("lost in space", update.RawStatus);
    }

    [Fact]
    public void The_same_file_with_other_line_endings_has_the_same_hash() =>
        Assert.Equal(Csv.Hash("a;b\r\n1;2\r\n"), Csv.Hash("﻿a;b\n1;2\n"));

    [Fact]
    public void Manifest_lists_parcels_with_cod_totals_in_csv_and_html()
    {
        var courier = Courier();
        var shipments = new[] { CreateShipment(courier, "PK1", 41.90m).Ok(), CreateShipment(courier, "PK2", 20.00m).Ok() };

        var receipts = new Dictionary<Guid, string> { [shipments[0].OrderId] = "Issued", [shipments[1].OrderId] = "Failed" };

        var manifest = _adapter.BuildManifest(courier, new DateOnly(2026, 11, 26), shipments, receipts, new ShopInfo());
        var csv = System.Text.Encoding.UTF8.GetString(manifest.Csv);

        Assert.StartsWith("﻿Porosia;Nr. gjurmimit;Marrësi", csv, StringComparison.Ordinal);
        Assert.Contains("PK1;Arbër Krasniqi;+38344123456;Lipjan;Rr. Adem Jashari 12 (kati 2);41.90;1;OK;", csv, StringComparison.Ordinal);
        Assert.Contains("20.00;1;PA KUPON (Failed);", csv, StringComparison.Ordinal);
        Assert.Contains("KUJDES: 1 pako PA KUPON FISKAL", manifest.Html, StringComparison.Ordinal);
        Assert.Contains("61.90", manifest.Html, StringComparison.Ordinal);
        Assert.Contains("UN1266", manifest.Html, StringComparison.Ordinal);
        Assert.Equal("manifest-posta-e-kosov-s-2026-11-26.csv", manifest.FileName);
    }

    [Fact]
    public void Html_output_is_encoded()
    {
        var courier = Courier();
        var shipment = Shipment.Create(Guid.NewGuid(), "PS-1", Guid.NewGuid(), courier, "PK9", 10m, 1, null,
            new Recipient("<script>alert(1)</script>", "+38344123456", "XK", "Lipjan", "Rr. 1", null), null, "s", Now).Ok();

        var html = _adapter.BuildManifest(courier, new DateOnly(2026, 11, 26), [shipment], new Dictionary<Guid, string>(), new ShopInfo()).Html;

        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
    }
}
