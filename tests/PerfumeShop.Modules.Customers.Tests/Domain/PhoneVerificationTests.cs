using PerfumeShop.Modules.Customers.Domain.Verification;

namespace PerfumeShop.Modules.Customers.Tests.Domain;

public sealed class PhoneVerificationTests
{
    private static readonly DateTime Now = new(2026, 11, 25, 10, 0, 0, DateTimeKind.Utc);

    private static PhoneVerification Issue() =>
        PhoneVerification.Issue(Guid.NewGuid(), TestHelpers.Phone(), new string('a', 64), Now, TimeSpan.FromMinutes(5), maxAttempts: 5, "203.0.113.7");

    [Fact]
    public void Correct_code_consumes_the_verification()
    {
        var verification = Issue();

        verification.Verify(codeMatches: true, Now.AddMinutes(1)).Ok();

        Assert.False(verification.IsPending);
        Assert.Equal(1, verification.Attempts);
    }

    [Fact]
    public void A_code_works_only_once()
    {
        var verification = Issue();
        verification.Verify(true, Now).Ok();

        verification.Verify(true, Now).Fails("Customers.Otp.InvalidCode");
    }

    [Fact]
    public void Wrong_code_counts_an_attempt_and_says_how_many_are_left()
    {
        var verification = Issue();

        var result = verification.Verify(codeMatches: false, Now);

        result.Fails("Customers.Otp.InvalidCode");
        Assert.Contains("4 attempt(s) left", result.Error.Message, StringComparison.Ordinal);
        Assert.True(verification.IsPending);
    }

    [Fact]
    public void After_five_wrong_codes_even_the_right_code_is_refused()
    {
        var verification = Issue();
        for (var i = 0; i < 4; i++)
        {
            verification.Verify(false, Now).Fails("Customers.Otp.InvalidCode");
        }

        verification.Verify(false, Now).Fails("Customers.Otp.TooManyAttempts");
        verification.Verify(true, Now).Fails("Customers.Otp.TooManyAttempts");
        Assert.Equal(5, verification.Attempts);
    }

    [Fact]
    public void Code_expires_after_five_minutes()
    {
        var verification = Issue();

        verification.Verify(true, Now.AddMinutes(4).AddSeconds(59)).Ok();
        var later = Issue();
        later.Verify(true, Now.AddMinutes(5)).Fails("Customers.Otp.Expired");
    }

    [Fact]
    public void Invalidated_code_cannot_be_used()
    {
        var verification = Issue();
        verification.Invalidate(Now);

        verification.Verify(true, Now).Fails("Customers.Otp.InvalidCode");
    }
}
