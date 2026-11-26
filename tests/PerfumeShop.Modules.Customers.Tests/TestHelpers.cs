using PerfumeShop.Modules.Customers.Domain.Common;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Customers.Tests;

internal static class TestHelpers
{
    private static int _next = 100_000;

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

    /// <summary>A Kosovo mobile number no other test uses, e.g. "+38349100001".</summary>
    public static string UniquePhone() => $"+38349{Interlocked.Increment(ref _next):D6}";

    public static PhoneNumber Phone() => PhoneNumber.Create(UniquePhone()).Ok();
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now = Now.Add(by);
}
