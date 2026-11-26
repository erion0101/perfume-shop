using System.Net;
using Microsoft.IdentityModel.JsonWebTokens;
using PerfumeShop.Modules.Customers.Features.RequestOtp;
using PerfumeShop.Modules.Customers.Features.Shared;

namespace PerfumeShop.Modules.Customers.Tests.Integration;

public sealed class OtpLoginTests(CustomersApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task First_login_creates_the_customer_and_returns_tokens()
    {
        var phone = TestHelpers.UniquePhone();

        var requested = await _client.PostJson("/api/auth/otp/request", new { phone });
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        var body = await requested.Read<RequestOtpResponse>();
        Assert.Equal(6, body.CodeLength);
        Assert.Contains("***", body.Phone, StringComparison.Ordinal);
        Assert.InRange(body.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(4), DateTime.UtcNow.AddMinutes(6));

        var verified = await _client.PostJson("/api/auth/otp/verify", new { phone, code = factory.Sms.LastCodeFor(phone) });

        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        var login = await verified.Read<LoginResponse>();
        Assert.True(login.IsNewCustomer);
        Assert.NotNull(login.CustomerId);
        Assert.Equal("Bearer", login.TokenType);
        Assert.InRange(login.AccessTokenExpiresAtUtc, DateTime.UtcNow.AddMinutes(14), DateTime.UtcNow.AddMinutes(16));
        Assert.InRange(login.RefreshTokenExpiresAtUtc, DateTime.UtcNow.AddDays(29), DateTime.UtcNow.AddDays(31));

        var jwt = new JsonWebToken(login.AccessToken);
        Assert.Equal("Customer", jwt.GetClaim("role").Value);
        Assert.Equal(login.CustomerId.ToString(), jwt.GetClaim("customer_id").Value);
    }

    [Fact]
    public async Task Next_login_finds_the_same_customer_even_in_local_format()
    {
        var phone = TestHelpers.UniquePhone();            // +38349xxxxxx
        var first = await factory.LoginCustomerAsync(phone);

        var second = await factory.LoginCustomerAsync("0" + phone[4..]); // 049xxxxxx

        Assert.False(second.IsNewCustomer);
        Assert.Equal(first.CustomerId, second.CustomerId);
    }

    [Fact]
    public async Task Wrong_codes_are_counted_and_the_fifth_locks_the_code()
    {
        var phone = TestHelpers.UniquePhone();
        await _client.PostJson("/api/auth/otp/request", new { phone });
        var code = factory.Sms.LastCodeFor(phone);
        var wrong = code == "000000" ? "111111" : "000000";

        var firstWrong = await _client.PostJson("/api/auth/otp/verify", new { phone, code = wrong });
        Assert.Equal(HttpStatusCode.BadRequest, firstWrong.StatusCode);
        Assert.Equal("Customers.Otp.InvalidCode", await firstWrong.ErrorCode());

        for (var i = 0; i < 3; i++)
        {
            await _client.PostJson("/api/auth/otp/verify", new { phone, code = wrong });
        }

        var fifth = await _client.PostJson("/api/auth/otp/verify", new { phone, code = wrong });
        var right = await _client.PostJson("/api/auth/otp/verify", new { phone, code });

        Assert.Equal(HttpStatusCode.TooManyRequests, fifth.StatusCode);
        Assert.Equal("Customers.Otp.TooManyAttempts", await fifth.ErrorCode());
        Assert.Equal(HttpStatusCode.TooManyRequests, right.StatusCode);
    }

    [Fact]
    public async Task A_new_code_invalidates_the_previous_one()
    {
        var phone = TestHelpers.UniquePhone();
        await _client.PostJson("/api/auth/otp/request", new { phone });
        var oldCode = factory.Sms.LastCodeFor(phone);
        await _client.PostJson("/api/auth/otp/request", new { phone });
        var newCode = factory.Sms.LastCodeFor(phone);

        if (oldCode != newCode)
        {
            var old = await _client.PostJson("/api/auth/otp/verify", new { phone, code = oldCode });
            Assert.Equal(HttpStatusCode.BadRequest, old.StatusCode);
        }

        var fresh = await _client.PostJson("/api/auth/otp/verify", new { phone, code = newCode });
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
    }

    [Fact]
    public async Task Three_codes_per_phone_per_window()
    {
        var phone = TestHelpers.UniquePhone();
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.Accepted, (await _client.PostJson("/api/auth/otp/request", new { phone })).StatusCode);
        }

        var fourth = await _client.PostJson("/api/auth/otp/request", new { phone });

        Assert.Equal(HttpStatusCode.TooManyRequests, fourth.StatusCode);
        Assert.Equal("Customers.Otp.RateLimited", await fourth.ErrorCode());
    }

    [Fact]
    public async Task Verifying_without_a_request_fails()
    {
        var response = await _client.PostJson("/api/auth/otp/verify", new { phone = TestHelpers.UniquePhone(), code = "123456" });

        Assert.Equal("Customers.Otp.InvalidCode", await response.ErrorCode());
    }

    [Theory]
    [InlineData("+383 38 123 456", "Customers.Phone.Invalid")]
    [InlineData("+49 151 12345678", "Customers.Phone.CountryNotSupported")]
    public async Task Only_supported_mobile_numbers_get_a_code(string phone, string code)
    {
        var response = await _client.PostJson("/api/auth/otp/request", new { phone });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, await response.ErrorCode());
    }

    [Fact]
    public async Task Code_must_have_six_digits()
    {
        var response = await _client.PostJson("/api/auth/otp/verify", new { phone = TestHelpers.UniquePhone(), code = "12ab" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Sms_failure_returns_503_and_the_code_cannot_be_used()
    {
        var phone = TestHelpers.UniquePhone();
        factory.Sms.FailSends = true;
        HttpResponseMessage response;
        try
        {
            response = await _client.PostJson("/api/auth/otp/request", new { phone });
        }
        finally
        {
            factory.Sms.FailSends = false;
        }

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Customers.Otp.SmsFailed", await response.ErrorCode());
    }
}
