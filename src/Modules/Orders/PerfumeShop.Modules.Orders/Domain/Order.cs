using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Orders.Domain;

/// <summary>
/// A Cash on Delivery order. Status machine:
/// Pending → Confirmed → Shipped → Delivered → CashCollected;
/// Pending (customer or staff) / Confirmed (staff) → Cancelled; Shipped → Refused; Pending → Expired.
/// Every change is appended to <see cref="History"/> and raises <see cref="OrderStatusChangedDomainEvent"/>.
/// </summary>
internal sealed class Order : AggregateRoot<Guid>, IAuditable
{
    public const string Currency = Money.Eur;
    public const string PaymentMethod = "CashOnDelivery";
    public const int MaxLines = 20;
    public const int MaxQuantityPerLine = 10;
    public const int NoteMaxLength = 500;
    public const int TextMaxLength = 100;

    private readonly List<OrderLine> _lines = [];
    private readonly List<OrderStatusChange> _history = [];
    private readonly List<OrderNote> _notes = [];

    private Order() { }

    private Order(Guid id) : base(id) { }

    public string Number { get; private set; } = null!;

    public Guid CustomerId { get; private set; }

    public OrderStatus Status { get; private set; }

    public CustomerSnapshot Customer { get; private set; } = null!;

    public DeliveryAddress Address { get; private set; } = null!;

    public string? CustomerNote { get; private set; }

    public decimal Subtotal { get; private set; }

    public decimal DeliveryFee { get; private set; }

    public decimal Total { get; private set; }

    /// <summary>VAT contained in <see cref="Total"/> (lines + delivery), for the fiscal receipt.</summary>
    public decimal VatAmount { get; private set; }

    public string IdempotencyKey { get; private set; } = null!;

    /// <summary>Fingerprint of the request, so a reused key with different content is detected.</summary>
    public string RequestHash { get; private set; } = null!;

    public DateTime PlacedAtUtc { get; private set; }

    /// <summary>Pending orders not confirmed by then expire.</summary>
    public DateTime ExpiresAtUtc { get; private set; }

    public DateTime? ConfirmedAtUtc { get; private set; }

    public DateTime? ShippedAtUtc { get; private set; }

    public DateTime? DeliveredAtUtc { get; private set; }

    public DateTime? CashCollectedAtUtc { get; private set; }

    public DateTime? ClosedAtUtc { get; private set; }

    public string? CancellationReason { get; private set; }

    public string? CourierName { get; private set; }

    public string? TrackingNumber { get; private set; }

    public decimal? CashCollectedAmount { get; private set; }

    public IReadOnlyList<OrderLine> Lines => _lines.AsReadOnly();

    public IReadOnlyList<OrderStatusChange> History => _history.AsReadOnly();

    public IReadOnlyList<OrderNote> Notes => _notes.AsReadOnly();

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Open = Cash on Delivery money still on its way: counts towards the per-customer limit.</summary>
    public static readonly IReadOnlyList<OrderStatus> OpenStatuses = [OrderStatus.Pending, OrderStatus.Confirmed, OrderStatus.Shipped];

    public static Result<Order> Place(
        Guid id,
        string number,
        Guid customerId,
        CustomerSnapshot customer,
        DeliveryAddress address,
        IReadOnlyList<OrderLineDraft> lines,
        decimal deliveryFee,
        string? customerNote,
        string idempotencyKey,
        string requestHash,
        DateTime nowUtc,
        TimeSpan confirmWithin)
    {
        if (lines.Count is 0 or > MaxLines)
        {
            return OrderErrors.NoLines;
        }

        if (lines.Any(l => l.Quantity is < 1 or > MaxQuantityPerLine))
        {
            return OrderErrors.QuantityInvalid;
        }

        if (lines.Select(l => l.VariantId).Distinct().Count() != lines.Count)
        {
            return OrderErrors.DuplicateVariant;
        }

        if (lines.Any(l => l.UnitPrice <= 0))
        {
            return OrderErrors.PriceInvalid;
        }

        var fullName = customer.FullName?.Trim();
        if (fullName is not { Length: >= 2 and <= TextMaxLength })
        {
            return OrderErrors.FullNameRequired;
        }

        if (string.IsNullOrWhiteSpace(customer.Phone))
        {
            return OrderErrors.PhoneRequired;
        }

        if (string.IsNullOrWhiteSpace(address.CountryCode) || string.IsNullOrWhiteSpace(address.City) || string.IsNullOrWhiteSpace(address.Street))
        {
            return OrderErrors.AddressInvalid;
        }

        customerNote = string.IsNullOrWhiteSpace(customerNote) ? null : customerNote.Trim();
        if (customerNote?.Length > NoteMaxLength)
        {
            return OrderErrors.NoteTooLong;
        }

        if (deliveryFee < 0)
        {
            return OrderErrors.DeliveryFeeInvalid;
        }

        if (idempotencyKey is not { Length: >= 8 and <= 100 })
        {
            return OrderErrors.IdempotencyKeyInvalid;
        }

        var order = new Order(id)
        {
            Number = number,
            CustomerId = customerId,
            Status = OrderStatus.Pending,
            Customer = customer with { FullName = fullName },
            Address = address,
            CustomerNote = customerNote,
            DeliveryFee = deliveryFee,
            IdempotencyKey = idempotencyKey,
            RequestHash = requestHash,
            PlacedAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc.Add(confirmWithin),
        };
        order._lines.AddRange(lines.Select(l => new OrderLine(id, l)));
        order.Subtotal = order._lines.Sum(l => l.LineTotal);
        order.Total = order.Subtotal + deliveryFee;
        order.VatAmount = order._lines.Sum(l => l.VatAmount) + Vat.Of(deliveryFee);
        order.Record(null, OrderStatus.Pending, OrderActor.Customer(customerId), null, nowUtc);
        return order;
    }

    public Result Confirm(OrderActor actor, DateTime nowUtc)
    {
        if (!actor.IsStaff)
        {
            return OrderErrors.OnlyStaff;
        }

        if (Status != OrderStatus.Pending)
        {
            return OrderErrors.InvalidTransition(Status, "confirm");
        }

        if (nowUtc >= ExpiresAtUtc)
        {
            return OrderErrors.ConfirmationWindowPassed;
        }

        ConfirmedAtUtc = nowUtc;
        return Move(OrderStatus.Confirmed, actor, null, nowUtc);
    }

    /// <summary>Customers may cancel only while Pending; staff until the parcel ships (with a reason).</summary>
    public Result Cancel(OrderActor actor, string? reason, DateTime nowUtc)
    {
        reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (reason?.Length > NoteMaxLength || (actor.IsStaff && reason is null))
        {
            return OrderErrors.ReasonRequired;
        }

        var allowed = actor.Kind == ActorKind.Customer
            ? Status == OrderStatus.Pending
            : Status is OrderStatus.Pending or OrderStatus.Confirmed;
        if (!allowed)
        {
            return OrderErrors.InvalidTransition(Status, "cancel");
        }

        CancellationReason = reason ?? "Cancelled by the customer";
        ClosedAtUtc = nowUtc;
        return Move(OrderStatus.Cancelled, actor, CancellationReason, nowUtc);
    }

    /// <summary>Checked before stock is committed in Inventory, so nothing leaves the warehouse for an order that cannot ship.</summary>
    public Result CanShip(OrderActor actor, string? courierName, string? trackingNumber)
    {
        if (!actor.IsStaff)
        {
            return OrderErrors.OnlyStaff;
        }

        if (courierName?.Trim() is not { Length: > 0 and <= TextMaxLength } || trackingNumber?.Trim() is not { Length: > 0 and <= TextMaxLength })
        {
            return OrderErrors.CourierRequired;
        }

        return Status == OrderStatus.Confirmed ? Result.Success() : OrderErrors.InvalidTransition(Status, "ship");
    }

    /// <param name="batches">Batch codes per line id, as committed by Inventory.</param>
    public Result Ship(OrderActor actor, string? courierName, string? trackingNumber, IReadOnlyDictionary<Guid, IReadOnlyList<LineBatch>> batches, DateTime nowUtc)
    {
        var allowed = CanShip(actor, courierName, trackingNumber);
        if (allowed.IsFailure)
        {
            return allowed;
        }

        courierName = courierName!.Trim();
        trackingNumber = trackingNumber!.Trim();

        foreach (var line in _lines)
        {
            line.RecordBatches(batches.TryGetValue(line.Id, out var lineBatches) ? lineBatches : []);
        }

        CourierName = courierName;
        TrackingNumber = trackingNumber;
        ShippedAtUtc = nowUtc;
        return Move(OrderStatus.Shipped, actor, $"{courierName} {trackingNumber}", nowUtc);
    }

    public Result MarkDelivered(OrderActor actor, DateTime nowUtc)
    {
        if (!actor.IsStaff)
        {
            return OrderErrors.OnlyStaff;
        }

        if (Status != OrderStatus.Shipped)
        {
            return OrderErrors.InvalidTransition(Status, "mark as delivered");
        }

        DeliveredAtUtc = nowUtc;
        return Move(OrderStatus.Delivered, actor, null, nowUtc);
    }

    /// <summary>The parcel was refused at the door and comes back.</summary>
    public Result MarkRefused(OrderActor actor, string? reason, DateTime nowUtc)
    {
        if (!actor.IsStaff)
        {
            return OrderErrors.OnlyStaff;
        }

        reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (reason?.Length > NoteMaxLength)
        {
            return OrderErrors.ReasonRequired;
        }

        if (Status != OrderStatus.Shipped)
        {
            return OrderErrors.InvalidTransition(Status, "mark as refused");
        }

        ClosedAtUtc = nowUtc;
        return Move(OrderStatus.Refused, actor, reason ?? "Refused at the door", nowUtc);
    }

    /// <summary>The courier handed over the cash. A different amount than the total needs a note.</summary>
    public Result MarkCashCollected(OrderActor actor, decimal amount, string? note, DateTime nowUtc)
    {
        if (!actor.IsStaff)
        {
            return OrderErrors.OnlyStaff;
        }

        if (amount < 0 || decimal.Round(amount, 2) != amount)
        {
            return OrderErrors.AmountInvalid;
        }

        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (note?.Length > NoteMaxLength)
        {
            return OrderErrors.NoteTooLong;
        }

        if (amount != Total && note is null)
        {
            return OrderErrors.CollectionNoteRequired;
        }

        if (Status != OrderStatus.Delivered)
        {
            return OrderErrors.InvalidTransition(Status, "mark cash collected for");
        }

        CashCollectedAmount = amount;
        CashCollectedAtUtc = nowUtc;
        ClosedAtUtc = nowUtc;
        return Move(OrderStatus.CashCollected, actor, note ?? FormattableString.Invariant($"Collected {amount:0.00} {Currency}"), nowUtc);
    }

    /// <summary>Background job: a Pending order past its confirmation window.</summary>
    public Result Expire(DateTime nowUtc)
    {
        if (Status != OrderStatus.Pending || nowUtc < ExpiresAtUtc)
        {
            return OrderErrors.InvalidTransition(Status, "expire");
        }

        ClosedAtUtc = nowUtc;
        return Move(OrderStatus.Expired, OrderActor.System, "Not confirmed in time", nowUtc);
    }

    public Result AddNote(OrderActor actor, string? text, DateTime nowUtc)
    {
        if (!actor.IsStaff)
        {
            return OrderErrors.OnlyStaff;
        }

        text = text?.Trim();
        if (text is not { Length: > 0 and <= 1000 })
        {
            return OrderErrors.NoteRequired;
        }

        _notes.Add(new OrderNote(Id, actor.Id, text, nowUtc));
        return Result.Success();
    }

    private Result Move(OrderStatus to, OrderActor actor, string? note, DateTime nowUtc)
    {
        var from = Status;
        Status = to;
        Record(from, to, actor, note, nowUtc);
        return Result.Success();
    }

    private void Record(OrderStatus? from, OrderStatus to, OrderActor actor, string? note, DateTime nowUtc)
    {
        _history.Add(new OrderStatusChange(Id, from, to, actor, note, nowUtc));
        Raise(new OrderStatusChangedDomainEvent(this, from, to, actor, note, nowUtc));
    }
}
