using PerfumeShop.Modules.Payments.Domain;
using static PerfumeShop.Modules.Payments.Tests.TestHelpers;

namespace PerfumeShop.Modules.Payments.Tests.Domain;

public sealed class PaymentTests
{
    [Fact]
    public void New_payment_expects_the_order_total_cash_on_delivery()
    {
        var payment = Payment();

        Assert.Equal((PaymentStatus.Expected, 41.90m, "CashOnDelivery"), (payment.Status, payment.ExpectedAmount, payment.Method));
    }

    [Fact]
    public void Collected_then_settled_gives_fee_and_net()
    {
        var payment = Payment();
        var settlementId = Guid.NewGuid();

        Assert.Equal(PaymentChange.Applied, payment.MarkCollected(41.90m, Now).Ok());
        Assert.Equal(PaymentChange.Applied, payment.RecordSettlement(settlementId, "POSTA-W48", 41.90m, 1.42m, Now).Ok());

        Assert.Equal((PaymentStatus.Collected, 41.90m, 1.42m, 40.48m, settlementId), (payment.Status, payment.CollectedAmount!.Value, payment.CourierFee!.Value, payment.NetReceived!.Value, payment.SettlementId!.Value));
    }

    [Fact]
    public void Settlement_before_the_collected_event_works_too()
    {
        var payment = Payment();

        payment.RecordSettlement(Guid.NewGuid(), "POSTA-W48", 41.90m, 1.42m, Now).Ok();
        var collected = payment.MarkCollected(41.90m, Now).Ok();

        Assert.Equal(PaymentChange.Unchanged, collected);
        Assert.Equal((PaymentStatus.Collected, 40.48m), (payment.Status, payment.NetReceived!.Value));
    }

    [Fact]
    public void Repeated_events_change_nothing()
    {
        var payment = Payment();
        var settlementId = Guid.NewGuid();
        payment.MarkCollected(41.90m, Now).Ok();
        payment.RecordSettlement(settlementId, "W48", 41.90m, 1.42m, Now).Ok();

        Assert.Equal(PaymentChange.Unchanged, payment.MarkCollected(41.90m, Now).Ok());
        Assert.Equal(PaymentChange.Unchanged, payment.RecordSettlement(settlementId, "W48", 41.90m, 1.42m, Now).Ok());
        payment.RecordSettlement(Guid.NewGuid(), "W49", 41.90m, 1.42m, Now).Fails("Payments.Payment.InvalidTransition");
    }

    [Fact]
    public void Refused_or_cancelled_payments_never_become_collected()
    {
        var refused = Payment();
        refused.MarkFailed(Now).Ok();
        Assert.Equal(PaymentChange.Unchanged, refused.MarkFailed(Now).Ok());
        refused.MarkCollected(41.90m, Now).Fails("Payments.Payment.InvalidTransition");

        var cancelled = Payment();
        cancelled.Cancel(Now).Ok();
        cancelled.RecordSettlement(Guid.NewGuid(), "W48", 41.90m, 1.42m, Now).Fails("Payments.Payment.InvalidTransition");
        Assert.Equal(PaymentStatus.Cancelled, cancelled.Status);
    }

    [Fact]
    public void Collected_cash_cannot_be_cancelled()
    {
        var payment = Payment();
        payment.MarkCollected(41.90m, Now).Ok();

        payment.Cancel(Now).Fails("Payments.Payment.InvalidTransition");
    }
}
