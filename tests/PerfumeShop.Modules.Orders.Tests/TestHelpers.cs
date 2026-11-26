using PerfumeShop.Modules.Orders.Domain;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Orders.Tests;

internal static class TestHelpers
{
    public static readonly DateTime Now = new(2026, 11, 26, 10, 0, 0, DateTimeKind.Utc);
    public static readonly OrderActor Staff = OrderActor.Staff("staff-user-1");

    public static T Ok<T>(this Result<T> result)
    {
        Assert.True(result.IsSuccess, $"Expected success but got {result.Error.Code}: {result.Error.Message}");
        return result.Value;
    }

    public static void Ok(this Result result) =>
        Assert.True(result.IsSuccess, $"Expected success but got {result.Error.Code}: {result.Error.Message}");

    public static void Fails(this Result result, string expectedCode)
    {
        Assert.True(result.IsFailure, $"Expected {expectedCode} but the operation succeeded.");
        Assert.Equal(expectedCode, result.Error.Code);
    }

    public static OrderLineDraft Line(decimal unitPrice = 39.90m, int quantity = 1, Guid? variantId = null) =>
        new(Guid.NewGuid(), variantId ?? Guid.NewGuid(), Guid.NewGuid(), "LAT-KHA-100", "Lattafa", "Khamrah", "FullBottle", 100, "Ml", unitPrice, quantity);

    public static Result<Order> Place(IReadOnlyList<OrderLineDraft>? lines = null, decimal deliveryFee = 2.00m, Guid? customerId = null, string fullName = "Arbër Krasniqi") =>
        Order.Place(
            Guid.NewGuid(), "PS-2026-000001", customerId ?? Guid.NewGuid(),
            new CustomerSnapshot(fullName, "+38344123456"),
            new DeliveryAddress(Guid.NewGuid(), "XK", "Lipjan", "Rr. Adem Jashari 12", "kati 2"),
            lines ?? [Line()], deliveryFee, "Thirrni para se të vini", "idem-key-0001", new string('a', 64), Now, TimeSpan.FromHours(48));

    public static Order Pending(Guid? customerId = null) => Place(customerId: customerId).Ok();

    public static Order Confirmed()
    {
        var order = Pending();
        order.Confirm(Staff, Now.AddHours(1)).Ok();
        return order;
    }

    public static Order Shipped()
    {
        var order = Confirmed();
        order.Ship(Staff, "Posta e Kosovës", "PK123456789", new Dictionary<Guid, IReadOnlyList<LineBatch>>(), Now.AddHours(2)).Ok();
        return order;
    }

    public static Order Delivered()
    {
        var order = Shipped();
        order.MarkDelivered(Staff, Now.AddDays(1)).Ok();
        return order;
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
