namespace PerfumeShop.Modules.Catalog.Tests;

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    private static int _nextYear = 2100;

    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;

    /// <summary>A month nobody else uses, so budget tests sharing one database stay independent.</summary>
    public static FixedTimeProvider InUnusedMonth() =>
        new(new DateTimeOffset(Interlocked.Increment(ref _nextYear), 3, 15, 12, 0, 0, TimeSpan.Zero));
}
