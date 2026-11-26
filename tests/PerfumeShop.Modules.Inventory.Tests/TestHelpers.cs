using PerfumeShop.Modules.Inventory.Domain;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Inventory.Tests;

internal static class TestHelpers
{
    public static readonly DateTime Now = new(2026, 11, 25, 10, 0, 0, DateTimeKind.Utc);

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

    public static Money Eur(decimal amount) => Money.Euro(amount).Ok();

    public static GoodsReceipt Receipt(DateOnly? receivedOn = null, string invoice = "INV-2026-001") =>
        new(Guid.NewGuid(), "Lattafa Distribution FZE", invoice, new DateOnly(2026, 11, 1), receivedOn ?? new DateOnly(2026, 11, 3));

    public static StockItem Item(int threshold = 3) => StockItem.Create(Guid.NewGuid(), threshold).Ok();

    /// <summary>Receives a batch and drops the ledger entries produced so far.</summary>
    public static StockBatch ReceiveBatch(this StockItem item, string code, int quantity, DateOnly? receivedOn = null) =>
        item.Receive(Receipt(receivedOn), code, quantity, Eur(18.50m), "staff", Now).Ok();
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
