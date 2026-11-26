using PerfumeShop.Modules.Payments.Contracts;

namespace PerfumeShop.Modules.Payments.Infrastructure.Providers;

/// <summary>
/// One implementation per payment method. v1 has only <see cref="CashOnDeliveryProvider"/>;
/// a local card gateway can be added later as another implementation.
/// </summary>
internal interface IPaymentProvider
{
    PaymentMethod Method { get; }
}
