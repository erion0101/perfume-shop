using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Shipping.Domain;

internal enum ShipmentStatus
{
    Created = 1,
    HandedOver = 2,
    InTransit = 3,
    Delivered = 4,
    Refused = 5,
    ReturnedToWarehouse = 6,
}

/// <summary>Where a status change came from.</summary>
internal enum UpdateSource
{
    Manual = 1,
    CsvImport = 2,
    Settlement = 3,
}

/// <summary>Who receives the parcel, copied from the order (the manifest and courier need it).</summary>
internal sealed record Recipient(string FullName, string Phone, string CountryCode, string City, string Street, string? Notes);

internal sealed class ShipmentStatusChange
{
    private ShipmentStatusChange() { }

    internal ShipmentStatusChange(Guid shipmentId, ShipmentStatus? from, ShipmentStatus to, string actorId, UpdateSource source, string? note, DateTime atUtc)
    {
        ShipmentId = shipmentId;
        FromStatus = from;
        ToStatus = to;
        ActorId = actorId;
        Source = source;
        Note = note;
        ChangedAtUtc = atUtc;
    }

    public Guid Id { get; private init; } = Guid.CreateVersion7();

    public Guid ShipmentId { get; private init; }

    public ShipmentStatus? FromStatus { get; private init; }

    public ShipmentStatus ToStatus { get; private init; }

    public string ActorId { get; private init; } = null!;

    public UpdateSource Source { get; private init; }

    public string? Note { get; private init; }

    public DateTime ChangedAtUtc { get; private init; }
}

internal sealed record ShipmentStatusChangedDomainEvent(Shipment Shipment, ShipmentStatus To, string? Note, DateTime AtUtc) : IDomainEvent;

/// <summary>Result of applying a courier status update.</summary>
internal enum TransitionOutcome
{
    Applied = 1,

    /// <summary>The shipment is already there (or further): repeated or late update, nothing to do.</summary>
    AlreadyApplied = 2,
}

/// <summary>
/// The parcel for one order. Created → HandedOver → InTransit → Delivered / Refused → ReturnedToWarehouse.
/// The order's stock goes back only when a refused parcel is physically back (ReturnedToWarehouse).
/// </summary>
internal sealed class Shipment : AggregateRoot<Guid>, IAuditable
{
    public const int TextMaxLength = 100;

    private readonly List<ShipmentStatusChange> _history = [];

    private Shipment() { }

    private Shipment(Guid id) : base(id) { }

    public Guid OrderId { get; private set; }

    public string OrderNumber { get; private set; } = null!;

    public Guid CustomerId { get; private set; }

    public Guid CourierId { get; private set; }

    public string CourierName { get; private set; } = null!;

    public string TrackingNumber { get; private set; } = null!;

    /// <summary>Cash the courier must collect: the order total (EUR).</summary>
    public decimal CodAmount { get; private set; }

    public int ParcelCount { get; private set; }

    public int? WeightGrams { get; private set; }

    public Recipient Recipient { get; private set; } = null!;

    public string? Note { get; private set; }

    public ShipmentStatus Status { get; private set; }

    public DateTime? HandedOverAtUtc { get; private set; }

    public DateTime? InTransitAtUtc { get; private set; }

    public DateTime? DeliveredAtUtc { get; private set; }

    public DateTime? RefusedAtUtc { get; private set; }

    public DateTime? ReturnedAtUtc { get; private set; }

    public Guid? SettlementId { get; private set; }

    public DateTime? SettledAtUtc { get; private set; }

    public decimal? CollectedAmount { get; private set; }

    public decimal? CodFee { get; private set; }

    /// <summary>Set when staff handed the parcel to the courier although its sale receipt was not issued.</summary>
    public string? ReceiptOverrideReason { get; private set; }

    public string? ReceiptOverrideBy { get; private set; }

    public DateTime? ReceiptOverrideAtUtc { get; private set; }

    public IReadOnlyList<ShipmentStatusChange> History => _history.AsReadOnly();

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Paid: through a courier statement, or recorded manually by staff.</summary>
    public bool IsSettled => SettledAtUtc is not null;

    public static Result<Shipment> Create(
        Guid orderId,
        string orderNumber,
        Guid customerId,
        Courier courier,
        string? trackingNumber,
        decimal codAmount,
        int parcelCount,
        int? weightGrams,
        Recipient recipient,
        string? note,
        string actorId,
        DateTime nowUtc)
    {
        trackingNumber = trackingNumber?.Trim().ToUpperInvariant();
        if (trackingNumber is not { Length: > 0 and <= TextMaxLength })
        {
            return ShippingErrors.TrackingRequired;
        }

        if (parcelCount is < 1 or > 10 || weightGrams is < 1 or > 30_000)
        {
            return ShippingErrors.ParcelsInvalid;
        }

        var selectable = courier.EnsureSelectableFor(recipient.CountryCode);
        if (selectable.IsFailure)
        {
            return selectable.Error;
        }

        var shipment = new Shipment(Guid.CreateVersion7())
        {
            OrderId = orderId,
            OrderNumber = orderNumber,
            CustomerId = customerId,
            CourierId = courier.Id,
            CourierName = courier.Name,
            TrackingNumber = trackingNumber,
            CodAmount = codAmount,
            ParcelCount = parcelCount,
            WeightGrams = weightGrams,
            Recipient = recipient,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            Status = ShipmentStatus.Created,
        };
        shipment.Record(null, ShipmentStatus.Created, actorId, UpdateSource.Manual, null, nowUtc);
        return shipment;
    }

    /// <summary>Moves to <paramref name="to"/>; a repeated or late (older) update is reported as already applied.</summary>
    public Result<TransitionOutcome> MoveTo(ShipmentStatus to, string actorId, UpdateSource source, string? note, DateTime nowUtc)
    {
        if (Status == to || IsLateUpdate(to))
        {
            return TransitionOutcome.AlreadyApplied;
        }

        var allowed = to switch
        {
            ShipmentStatus.HandedOver => Status == ShipmentStatus.Created,
            ShipmentStatus.InTransit => Status == ShipmentStatus.HandedOver,
            ShipmentStatus.Delivered or ShipmentStatus.Refused => Status is ShipmentStatus.HandedOver or ShipmentStatus.InTransit,
            ShipmentStatus.ReturnedToWarehouse => Status == ShipmentStatus.Refused,
            _ => false,
        };
        if (!allowed)
        {
            return ShippingErrors.InvalidTransition(Status, to);
        }

        switch (to)
        {
            case ShipmentStatus.HandedOver: HandedOverAtUtc = nowUtc; break;
            case ShipmentStatus.InTransit: InTransitAtUtc = nowUtc; break;
            case ShipmentStatus.Delivered: DeliveredAtUtc = nowUtc; break;
            case ShipmentStatus.Refused: RefusedAtUtc = nowUtc; break;
            case ShipmentStatus.ReturnedToWarehouse: ReturnedAtUtc = nowUtc; break;
        }

        var from = Status;
        Status = to;
        Record(from, to, actorId, source, note?.Trim() is { Length: > 0 } n ? n[..Math.Min(n.Length, 500)] : null, nowUtc);
        return TransitionOutcome.Applied;
    }

    /// <summary>The courier paid us the collected cash (from a remittance statement).</summary>
    public Result Settle(Guid settlementId, decimal collectedAmount, decimal fee, DateTime nowUtc)
    {
        if (IsSettled)
        {
            return ShippingErrors.AlreadySettled;
        }

        if (Status != ShipmentStatus.Delivered)
        {
            return ShippingErrors.InvalidTransition(Status, ShipmentStatus.Delivered);
        }

        SettlementId = settlementId;
        SettledAtUtc = nowUtc;
        CollectedAmount = collectedAmount;
        CodFee = fee;
        return Result.Success();
    }

    /// <summary>
    /// Staff record the cash by hand (e.g. a statement line with a wrong amount, after checking with the courier).
    /// Counts as settled, so a later statement line for this parcel is reported as already settled.
    /// </summary>
    public Result SettleManually(decimal collectedAmount, string? note, string actorId, DateTime nowUtc)
    {
        if (IsSettled)
        {
            return ShippingErrors.AlreadySettled;
        }

        if (Status != ShipmentStatus.Delivered)
        {
            return ShippingErrors.InvalidTransition(Status, ShipmentStatus.Delivered);
        }

        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (collectedAmount < 0 || decimal.Round(collectedAmount, 2) != collectedAmount || note?.Length > 500 || (collectedAmount != CodAmount && note is null))
        {
            return Error.Validation("Shipping.Shipment.ManualCollectionInvalid", "Amount ≥ 0 with 2 decimals; a note is required when it differs from the COD amount.");
        }

        SettledAtUtc = nowUtc;
        CollectedAmount = collectedAmount;
        _history.Add(new ShipmentStatusChange(Id, Status, Status, actorId, UpdateSource.Manual,
            FormattableString.Invariant($"Cash collected manually: {collectedAmount:0.00} EUR") + (note is null ? string.Empty : $" ({note})"), nowUtc));
        return Result.Success();
    }

    /// <summary>Records the staff override for a handover without an issued fiscal receipt (before <see cref="MoveTo"/>).</summary>
    public Result OverrideMissingReceipt(string? reason, string actorId, DateTime nowUtc)
    {
        reason = reason?.Trim();
        if (reason is not { Length: >= 5 and <= 500 })
        {
            return ShippingErrors.ReceiptOverrideReasonRequired;
        }

        if (Status != ShipmentStatus.Created)
        {
            return ShippingErrors.InvalidTransition(Status, ShipmentStatus.HandedOver);
        }

        ReceiptOverrideReason = reason;
        ReceiptOverrideBy = actorId;
        ReceiptOverrideAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>Courier files often repeat or arrive out of order: "InTransit" after "Delivered" is old news.</summary>
    private bool IsLateUpdate(ShipmentStatus to) => to switch
    {
        ShipmentStatus.HandedOver => Status is not ShipmentStatus.Created,
        ShipmentStatus.InTransit => Status is ShipmentStatus.Delivered or ShipmentStatus.Refused or ShipmentStatus.ReturnedToWarehouse,
        ShipmentStatus.Refused => Status == ShipmentStatus.ReturnedToWarehouse,
        _ => false,
    };

    private void Record(ShipmentStatus? from, ShipmentStatus to, string actorId, UpdateSource source, string? note, DateTime nowUtc)
    {
        _history.Add(new ShipmentStatusChange(Id, from, to, actorId, source, note, nowUtc));
        Raise(new ShipmentStatusChangedDomainEvent(this, to, note, nowUtc));
    }
}
