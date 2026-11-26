using Microsoft.Extensions.DependencyInjection;
using PerfumeShop.Modules.Customers.Features.RequestOtp;
using PerfumeShop.Modules.Customers.Features.VerifyOtp;

namespace PerfumeShop.Modules.Customers.Tests.Integration;

/// <summary>Real handlers and database, with a controllable clock and client IP.</summary>
public sealed class OtpHandlerTests(CustomersApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FixedTimeProvider Clock() => new(DateTimeOffset.UtcNow);

    private static T Create<T>(AsyncServiceScope scope, TimeProvider clock) =>
        ActivatorUtilities.CreateInstance<T>(scope.ServiceProvider, clock);

    [Fact]
    public async Task Code_expires_after_five_minutes()
    {
        await using var scope = factory.CreateScope();
        var clock = Clock();
        var phone = TestHelpers.UniquePhone();
        (await Create<RequestOtpHandler>(scope, clock).HandleAsync(new(phone), "198.51.100.1", Ct)).Ok();
        var code = factory.Sms.LastCodeFor(phone);

        clock.Advance(TimeSpan.FromMinutes(5));
        var result = await Create<VerifyOtpHandler>(scope, clock).HandleAsync(new(phone, code), null, Ct);

        result.Fails("Customers.Otp.Expired");
    }

    [Fact]
    public async Task Ten_codes_per_ip_per_window_across_different_phones()
    {
        await using var scope = factory.CreateScope();
        var handler = Create<RequestOtpHandler>(scope, Clock());
        var ip = $"203.0.113.{Random.Shared.Next(1, 250)}-{Guid.NewGuid():N}"[..40];

        for (var i = 0; i < 10; i++)
        {
            (await handler.HandleAsync(new(TestHelpers.UniquePhone()), ip, Ct)).Ok();
        }

        var eleventh = await handler.HandleAsync(new(TestHelpers.UniquePhone()), ip, Ct);
        var otherIp = await handler.HandleAsync(new(TestHelpers.UniquePhone()), "192.0.2.99", Ct);

        eleventh.Fails("Customers.Otp.RateLimited");
        otherIp.Ok();
    }

    [Fact]
    public async Task Phone_limit_resets_after_the_window()
    {
        await using var scope = factory.CreateScope();
        var clock = Clock();
        var handler = Create<RequestOtpHandler>(scope, clock);
        var phone = TestHelpers.UniquePhone();
        for (var i = 0; i < 3; i++)
        {
            (await handler.HandleAsync(new(phone), null, Ct)).Ok();
        }

        (await handler.HandleAsync(new(phone), null, Ct)).Fails("Customers.Otp.RateLimited");
        clock.Advance(TimeSpan.FromMinutes(15).Add(TimeSpan.FromSeconds(1)));

        (await handler.HandleAsync(new(phone), null, Ct)).Ok();
    }

    [Fact]
    public async Task Codes_are_stored_hashed()
    {
        await using var scope = factory.CreateScope();
        var phone = TestHelpers.UniquePhone();
        (await Create<RequestOtpHandler>(scope, Clock()).HandleAsync(new(phone), null, Ct)).Ok();
        var code = factory.Sms.LastCodeFor(phone);

        var db = scope.ServiceProvider.GetRequiredService<Infrastructure.CustomersDbContext>();
        var stored = db.PhoneVerifications.AsEnumerable().Last(v => v.Phone.Value == phone);

        Assert.Equal(64, stored.CodeHash.Length);
        Assert.DoesNotContain(code, stored.CodeHash, StringComparison.Ordinal);
    }
}
