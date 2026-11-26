using PerfumeShop.Modules.Payments.Contracts;

namespace PerfumeShop.Modules.Payments.Infrastructure.Providers;

/// <summary>
/// The customer pays the courier in cash when the order is delivered.
/// TODO: record expected amount on order placement, mark collected on courier reconciliation.
/// </summary>
internal sealed class CashOnDeliveryProvider : IPaymentProvider
{
    public PaymentMethod Method => PaymentMethod.CashOnDelivery;
}
