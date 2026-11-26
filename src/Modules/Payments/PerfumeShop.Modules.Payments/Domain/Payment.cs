using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Payments.Domain;

internal enum PaymentStatus
{
    /// <summary>Order placed; the courier will collect the cash on delivery.</summary>
    Expected = 1,
    Collected = 2,
    /// <summary>The parcel was refused: no money will come.</summary>
    Failed = 3,
    Cancelled = 4,
}

/// <summary>What changed when an event was applied (repeats are not errors: events are delivered at least once).</summary>
internal enum PaymentChange
{
    Applied = 1,
    Unchanged = 2,
}

/// <summary>
/// The Cash on Delivery payment of one order: expected amount, what the courier collected, the COD fee the courier
/// kept and what we actually received. Driven only by integration events (Orders, Shipping).
/// </summary>
internal sealed class Payment : AggregateRoot<Guid>, IAuditable
{
    private Payment() { }

    private Payment(Guid id) : base(id) { }

    public Guid OrderId { get; private set; }

    public string OrderNumber { get; private set; } = null!;

    public Guid CustomerId { get; private set; }

    /// <summary>CashOnDelivery (the only method in v1).</summary>
    public string Method { get; private set; } = null!;

    public PaymentStatus Status { get; private set; }

    public decimal ExpectedAmount { get; private set; }

    public decimal? CollectedAmount { get; private set; }

    /// <summary>COD commission the courier deducted.</summary>
    public decimal? CourierFee { get; private set; }

    /// <summary>Collected − courier fee: what reached our account.</summary>
    public decimal? NetReceived { get; private set; }

    public string? CourierName { get; private set; }

    public string? TrackingNumber { get; private set; }

    public DateTime PlacedAtUtc { get; private set; }

    public DateTime? ShippedAtUtc { get; private set; }

    public DateTime? CollectedAtUtc { get; private set; }

    public Guid? SettlementId { get; private set; }

    public string? SettlementReference { get; private set; }

    public DateTime? SettledAtUtc { get; private set; }

    public DateTime? ClosedAtUtc { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static Payment Expect(Guid orderId, string orderNumber, Guid customerId, decimal expectedAmount, string method, DateTime placedAtUtc) =>
        new(Guid.CreateVersion7())
        {
            OrderId = orderId,
            OrderNumber = orderNumber,
            CustomerId = customerId,
            ExpectedAmount = expectedAmount,
            Method = method,
            Status = PaymentStatus.Expected,
            PlacedAtUtc = placedAtUtc,
        };

    public PaymentChange RecordShipment(string courierName, string trackingNumber, DateTime atUtc)
    {
        if (CourierName == courierName && TrackingNumber == trackingNumber)
        {
            return PaymentChange.Unchanged;
        }

        CourierName = courierName;
        TrackingNumber = trackingNumber;
        ShippedAtUtc ??= atUtc;
        return PaymentChange.Applied;
    }

    /// <summary>The order was booked as CashCollected (statement or manual).</summary>
    public Result<PaymentChange> MarkCollected(decimal amount, DateTime atUtc)
    {
        if (Status == PaymentStatus.Collected)
        {
            return PaymentChange.Unchanged;
        }

        if (Status != PaymentStatus.Expected)
        {
            return Conflict("collected");
        }

        Status = PaymentStatus.Collected;
        CollectedAmount = amount;
        CollectedAtUtc = atUtc;
        ClosedAtUtc = atUtc;
        NetReceived = CourierFee is { } fee ? amount - fee : NetReceived;
        return PaymentChange.Applied;
    }

    /// <summary>The courier's statement paid this order: fee and net received are known now.</summary>
    public Result<PaymentChange> RecordSettlement(Guid settlementId, string reference, decimal collected, decimal fee, DateTime atUtc)
    {
        if (SettlementId == settlementId)
        {
            return PaymentChange.Unchanged;
        }

        if (SettlementId is not null || Status is PaymentStatus.Failed or PaymentStatus.Cancelled)
        {
            return Conflict("settled");
        }

        if (Status == PaymentStatus.Expected)
        {
            Status = PaymentStatus.Collected;
            CollectedAtUtc = atUtc;
            ClosedAtUtc = atUtc;
        }

        CollectedAmount = collected;
        SettlementId = settlementId;
        SettlementReference = reference;
        SettledAtUtc = atUtc;
        CourierFee = fee;
        NetReceived = collected - fee;
        return PaymentChange.Applied;
    }

    /// <summary>Parcel refused and back in the warehouse.</summary>
    public Result<PaymentChange> MarkFailed(DateTime atUtc) => Close(PaymentStatus.Failed, atUtc);

    /// <summary>Order cancelled or expired before shipping.</summary>
    public Result<PaymentChange> Cancel(DateTime atUtc) => Close(PaymentStatus.Cancelled, atUtc);

    private Result<PaymentChange> Close(PaymentStatus to, DateTime atUtc)
    {
        if (Status == to)
        {
            return PaymentChange.Unchanged;
        }

        if (Status != PaymentStatus.Expected)
        {
            return Conflict(to.ToString().ToLowerInvariant());
        }

        Status = to;
        ClosedAtUtc = atUtc;
        return PaymentChange.Applied;
    }

    private Error Conflict(string action) =>
        Error.Conflict("Payments.Payment.InvalidTransition", $"Payment of {OrderNumber} is {Status} and cannot be {action}.");
}
