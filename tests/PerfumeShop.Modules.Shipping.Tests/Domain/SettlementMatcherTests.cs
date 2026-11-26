using PerfumeShop.Modules.Shipping.Domain;
using static PerfumeShop.Modules.Shipping.Tests.TestHelpers;

namespace PerfumeShop.Modules.Shipping.Tests.Domain;

public sealed class SettlementMatcherTests
{
    private static readonly Courier Posta = Courier();

    private static ShipmentForMatching Shipment(string tracking, ShipmentStatus status = ShipmentStatus.Delivered, bool settled = false, decimal cod = 41.90m, Guid? courierId = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), tracking, "PS-2026-000001", courierId ?? Posta.Id, status, settled, cod);

    private static SettlementLineStatus Classify(RemittanceLine line, params ShipmentForMatching[] shipments) =>
        SettlementMatcher.Classify(Posta, [line], shipments.ToDictionary(s => s.TrackingNumber)).Single().Status;

    [Fact]
    public void Exact_amount_for_a_delivered_parcel_is_matched()
    {
        var result = SettlementMatcher.Classify(Posta, [new(2, "PK1", 41.90m, 1.42m)], new Dictionary<string, ShipmentForMatching> { ["PK1"] = Shipment("PK1") }).Single();

        Assert.Equal(SettlementLineStatus.Matched, result.Status);
        Assert.Equal(1.42m, result.ExpectedFee);
        Assert.Null(result.Note);
    }

    [Fact]
    public void In_transit_parcels_can_be_paid_before_we_hear_they_were_delivered() =>
        Assert.Equal(SettlementLineStatus.Matched, Classify(new(2, "PK1", 41.90m, 1.42m), Shipment("PK1", ShipmentStatus.InTransit)));

    [Fact]
    public void A_fee_different_from_the_courier_terms_is_noted_but_still_matched()
    {
        var result = SettlementMatcher.Classify(Posta, [new(2, "PK1", 41.90m, 3.00m)], new Dictionary<string, ShipmentForMatching> { ["PK1"] = Shipment("PK1") }).Single();

        Assert.Equal(SettlementLineStatus.Matched, result.Status);
        Assert.Contains("differs from the expected 1.42", result.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Wrong_amount_is_a_mismatch() =>
        Assert.Equal(SettlementLineStatus.AmountMismatch, Classify(new(2, "PK1", 40.00m, 1.42m), Shipment("PK1")));

    [Fact]
    public void Unknown_or_other_couriers_tracking_numbers_are_unknown()
    {
        Assert.Equal(SettlementLineStatus.UnknownTracking, Classify(new(2, "NOPE", 10m, 0m)));
        Assert.Equal(SettlementLineStatus.UnknownTracking, Classify(new(2, "PK1", 41.90m, 0m), Shipment("PK1", courierId: Guid.NewGuid())));
    }

    [Fact]
    public void Already_settled_parcels_are_never_counted_twice() =>
        Assert.Equal(SettlementLineStatus.AlreadySettled, Classify(new(2, "PK1", 41.90m, 1.42m), Shipment("PK1", settled: true)));

    [Theory]
    [InlineData((int)ShipmentStatus.Refused)]
    [InlineData((int)ShipmentStatus.ReturnedToWarehouse)]
    [InlineData((int)ShipmentStatus.Created)]
    public void Parcels_without_cash_are_not_payable(int status) =>
        Assert.Equal(SettlementLineStatus.NotPayable, Classify(new(2, "PK1", 41.90m, 1.42m), Shipment("PK1", (ShipmentStatus)status)));

    [Fact]
    public void A_tracking_number_twice_in_one_file_counts_once()
    {
        var results = SettlementMatcher.Classify(Posta, [new(2, "PK1", 41.90m, 1.42m), new(3, "pk1", 41.90m, 1.42m)],
            new Dictionary<string, ShipmentForMatching>(StringComparer.OrdinalIgnoreCase) { ["PK1"] = Shipment("PK1") });

        Assert.Equal([SettlementLineStatus.Matched, SettlementLineStatus.DuplicateInFile], results.Select(r => r.Status));
    }
}
