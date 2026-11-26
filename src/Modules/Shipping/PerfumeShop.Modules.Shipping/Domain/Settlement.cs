using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Shipping.Domain;

internal enum SettlementLineStatus
{
    /// <summary>Amount equals the COD amount: the order is marked CashCollected.</summary>
    Matched = 1,
    AmountMismatch = 2,
    UnknownTracking = 3,
    /// <summary>Paid in an earlier statement: not counted twice.</summary>
    AlreadySettled = 4,
    /// <summary>The parcel was refused/returned or never left: no cash expected.</summary>
    NotPayable = 5,
    DuplicateInFile = 6,
    /// <summary>Matched, but Orders refused the update (see note).</summary>
    OrderUpdateFailed = 7,
}

/// <summary>One line of a courier remittance statement.</summary>
internal sealed record RemittanceLine(int LineNumber, string TrackingNumber, decimal CollectedAmount, decimal Fee);

/// <summary>What the matcher needs to know about a shipment.</summary>
internal sealed record ShipmentForMatching(Guid ShipmentId, Guid OrderId, string TrackingNumber, string OrderNumber, Guid CourierId, ShipmentStatus Status, bool IsSettled, decimal CodAmount);

internal sealed record ClassifiedLine(RemittanceLine Line, SettlementLineStatus Status, ShipmentForMatching? Shipment, decimal? ExpectedFee, string? Note);

/// <summary>Pure matching rules for a remittance statement (no database, no side effects).</summary>
internal static class SettlementMatcher
{
    public static IReadOnlyList<ClassifiedLine> Classify(Courier courier, IReadOnlyList<RemittanceLine> lines, IReadOnlyDictionary<string, ShipmentForMatching> shipmentsByTracking)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        List<ClassifiedLine> result = [];

        foreach (var line in lines)
        {
            if (!seen.Add(line.TrackingNumber))
            {
                result.Add(new(line, SettlementLineStatus.DuplicateInFile, null, null, "This tracking number appears earlier in the file."));
                continue;
            }

            if (!shipmentsByTracking.TryGetValue(line.TrackingNumber, out var shipment) || shipment.CourierId != courier.Id)
            {
                result.Add(new(line, SettlementLineStatus.UnknownTracking, null, null, "No shipment with this tracking number for this courier."));
                continue;
            }

            var expectedFee = courier.ExpectedCodFee(shipment.CodAmount);
            var feeNote = line.Fee != expectedFee ? Invariant($"Fee {line.Fee:0.00} differs from the expected {expectedFee:0.00}.") : null;

            if (shipment.IsSettled)
            {
                result.Add(new(line, SettlementLineStatus.AlreadySettled, shipment, expectedFee, "Already paid in an earlier statement."));
            }
            else if (shipment.Status is ShipmentStatus.Created or ShipmentStatus.Refused or ShipmentStatus.ReturnedToWarehouse)
            {
                result.Add(new(line, SettlementLineStatus.NotPayable, shipment, expectedFee, $"Shipment is {shipment.Status}: no cash expected."));
            }
            else if (line.CollectedAmount != shipment.CodAmount)
            {
                result.Add(new(line, SettlementLineStatus.AmountMismatch, shipment, expectedFee,
                    Invariant($"Collected {line.CollectedAmount:0.00}, expected {shipment.CodAmount:0.00}.")));
            }
            else
            {
                result.Add(new(line, SettlementLineStatus.Matched, shipment, expectedFee, feeNote));
            }
        }

        return result;
    }

    /// <summary>Notes are stored and shown everywhere: never format them with the server's culture.</summary>
    private static string Invariant(FormattableString text) => FormattableString.Invariant(text);
}

internal sealed class SettlementLine
{
    private SettlementLine() { }

    internal SettlementLine(Guid settlementId, ClassifiedLine classified)
    {
        SettlementId = settlementId;
        LineNumber = classified.Line.LineNumber;
        TrackingNumber = classified.Line.TrackingNumber;
        CollectedAmount = classified.Line.CollectedAmount;
        Fee = classified.Line.Fee;
        Status = classified.Status;
        ShipmentId = classified.Shipment?.ShipmentId;
        OrderId = classified.Shipment?.OrderId;
        OrderNumber = classified.Shipment?.OrderNumber;
        ExpectedAmount = classified.Shipment?.CodAmount;
        ExpectedFee = classified.ExpectedFee;
        Note = classified.Note;
    }

    public Guid Id { get; private init; } = Guid.CreateVersion7();

    public Guid SettlementId { get; private init; }

    public int LineNumber { get; private init; }

    public string TrackingNumber { get; private init; } = null!;

    public decimal CollectedAmount { get; private init; }

    public decimal Fee { get; private init; }

    public SettlementLineStatus Status { get; private set; }

    public Guid? ShipmentId { get; private init; }

    public Guid? OrderId { get; private init; }

    public string? OrderNumber { get; private init; }

    public decimal? ExpectedAmount { get; private init; }

    public decimal? ExpectedFee { get; private init; }

    public string? Note { get; private set; }

    internal void MarkOrderUpdateFailed(string reason)
    {
        Status = SettlementLineStatus.OrderUpdateFailed;
        Note = reason.Length > 500 ? reason[..500] : reason;
    }
}

internal sealed record CodSettledDomainEvent(Settlement Settlement) : IDomainEvent;

/// <summary>One imported courier remittance statement. The file hash makes a re-import a no-op.</summary>
internal sealed class Settlement : AggregateRoot<Guid>
{
    private readonly List<SettlementLine> _lines = [];

    private Settlement() { }

    private Settlement(Guid id) : base(id) { }

    public Guid CourierId { get; private set; }

    public string CourierName { get; private set; } = null!;

    public string Reference { get; private set; } = null!;

    public string FileHash { get; private set; } = null!;

    public string ImportedBy { get; private set; } = null!;

    public DateTime ImportedAtUtc { get; private set; }

    public IReadOnlyList<SettlementLine> Lines => _lines.AsReadOnly();

    /// <summary>Sum of all lines as the courier reported them.</summary>
    public decimal TotalCollected => _lines.Sum(l => l.CollectedAmount);

    public decimal TotalFees => _lines.Sum(l => l.Fee);

    public decimal NetPaid => TotalCollected - TotalFees;

    public decimal MatchedAmount => _lines.Where(l => l.Status == SettlementLineStatus.Matched).Sum(l => l.CollectedAmount);

    public int MatchedCount => _lines.Count(l => l.Status == SettlementLineStatus.Matched);

    public static Result<Settlement> Create(Courier courier, string? reference, string fileHash, string importedBy, IReadOnlyList<ClassifiedLine> lines, DateTime nowUtc)
    {
        reference = string.IsNullOrWhiteSpace(reference) ? $"{courier.Name} {nowUtc:yyyy-MM-dd HH:mm}" : reference.Trim();
        if (reference.Length > 100 || lines.Count == 0)
        {
            return Error.Validation("Shipping.Settlement.Invalid", "A statement needs at least one line and a reference of at most 100 characters.");
        }

        var settlement = new Settlement(Guid.CreateVersion7())
        {
            CourierId = courier.Id,
            CourierName = courier.Name,
            Reference = reference,
            FileHash = fileHash,
            ImportedBy = importedBy,
            ImportedAtUtc = nowUtc,
        };
        settlement._lines.AddRange(lines.Select(l => new SettlementLine(settlement.Id, l)));
        return settlement;
    }

    /// <summary>Raises <see cref="CodSettledDomainEvent"/> once the Orders updates are done.</summary>
    public void Complete() => Raise(new CodSettledDomainEvent(this));
}
