using PerfumeShop.Modules.Orders.Contracts;
using PerfumeShop.Modules.Payments.Domain;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Payments.Tests;

internal static class TestHelpers
{
    public static readonly DateTime Now = new(2026, 11, 26, 10, 0, 0, DateTimeKind.Utc);

    public static T Ok<T>(this Result<T> result)
    {
        Assert.True(result.IsSuccess, $"Expected success but got {result.Error.Code}: {result.Error.Message}");
        return result.Value;
    }

    public static void Fails(this Result result, string expectedCode)
    {
        Assert.True(result.IsFailure, $"Expected {expectedCode} but the operation succeeded.");
        Assert.Equal(expectedCode, result.Error.Code);
    }

    public static Payment Payment(decimal expected = 41.90m) =>
        PerfumeShop.Modules.Payments.Domain.Payment.Expect(Guid.NewGuid(), "PS-2026-000001", Guid.NewGuid(), expected, "CashOnDelivery", Now);

    /// <summary>Two bottles of 39.90 € + one 12.50 € attar + 2.00 € delivery.</summary>
    public static OrderSummaryDto Order(decimal deliveryFee = 2.00m) => new(
        Guid.NewGuid(), "PS-2026-000042", "Shipped", Guid.NewGuid(), "Arbër Krasniqi", "+38344123456",
        new OrderAddressDto("XK", "Lipjan", "Rr. Adem Jashari 12", null),
        92.30m, deliveryFee, 92.30m + deliveryFee, 14.08m + (deliveryFee == 0 ? 0 : 0.31m), "EUR", "CashOnDelivery", "Posta", "PK1", Now,
        [
            new OrderLineSummaryDto(Guid.NewGuid(), Guid.NewGuid(), "LAT-KHA-100", "Lattafa", "Khamrah", "FullBottle", 100, "Ml", 39.90m, 2, 79.80m, 12.17m, ["B2391×2"]),
            new OrderLineSummaryDto(Guid.NewGuid(), Guid.NewGuid(), "AJM-DAHN-3", "Ajmal", "Dahn Al Oudh", "Mini", 3, "Ml", 12.50m, 1, 12.50m, 1.91m, []),
        ]);
}
