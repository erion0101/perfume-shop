using System.Net;
using PerfumeShop.Modules.Customers.Features.ListCustomers;
using PerfumeShop.Modules.Customers.Features.Shared;
using PerfumeShop.Shared.Infrastructure.Auth;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Testing;

namespace PerfumeShop.Modules.Customers.Tests.Integration;

public sealed class AdminCustomersTests(CustomersApiFactory factory)
{
    private const string Admin = "/api/admin/customers";
    private readonly HttpClient _admin = factory.CreateAdminClient();

    [Fact]
    public async Task List_finds_a_customer_by_phone_in_any_format_and_get_returns_details()
    {
        var phone = TestHelpers.UniquePhone();
        var login = await factory.LoginCustomerAsync(phone);
        await factory.CreateClientWithToken(login.AccessToken).PostJson("/api/customers/me/addresses", new { city = "Gjilan", street = "Rr. 1" });

        var list = await (await _admin.Get($"{Admin}?search={Uri.EscapeDataString("0" + phone[4..])}")).Read<PagedResponse<CustomerListItemDto>>();
        var details = await (await _admin.Get($"{Admin}/{login.CustomerId}")).Read<CustomerAdminDto>();

        var item = Assert.Single(list.Items);
        Assert.Equal(phone, item.Phone);
        Assert.Equal(1, item.AddressCount);
        Assert.Equal("Active", details.Status);
        Assert.Equal("Gjilan", details.Addresses.Single().City);
        Assert.NotNull(details.LastLoginAtUtc);
    }

    [Fact]
    public async Task Blocking_signs_the_customer_out_and_stops_new_logins_until_unblocked()
    {
        var phone = TestHelpers.UniquePhone();
        var login = await factory.LoginCustomerAsync(phone);
        var customer = factory.CreateClientWithToken(login.AccessToken);

        var block = await _admin.PostJson($"{Admin}/{login.CustomerId}/block", new { reason = "Refuzoi 3 porosi COD" });
        Assert.Equal(HttpStatusCode.OK, block.StatusCode);
        Assert.Equal("Blocked", (await block.Read<CustomerAdminDto>()).Status);

        var refresh = await factory.CreateClient().PostJson("/api/auth/refresh", new { refreshToken = login.RefreshToken });
        var otp = await factory.CreateClient().PostJson("/api/auth/otp/request", new { phone });
        var me = await customer.Get("/api/customers/me");
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, otp.StatusCode);
        Assert.Equal("Customers.Customer.Blocked", await otp.ErrorCode());
        Assert.Equal(HttpStatusCode.Forbidden, me.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await _admin.PostJson($"{Admin}/{login.CustomerId}/unblock")).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await factory.CreateClient().PostJson("/api/auth/otp/request", new { phone })).StatusCode);
    }

    [Fact]
    public async Task Block_needs_a_reason_and_cannot_be_repeated()
    {
        var login = await factory.LoginCustomerAsync(TestHelpers.UniquePhone());

        var noReason = await _admin.PostJson($"{Admin}/{login.CustomerId}/block", new { reason = "" });
        await _admin.PostJson($"{Admin}/{login.CustomerId}/block", new { reason = "fake orders" });
        var twice = await _admin.PostJson($"{Admin}/{login.CustomerId}/block", new { reason = "again" });

        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
    }

    [Fact]
    public async Task Staff_may_manage_customers_but_customers_may_not()
    {
        var staff = factory.CreateClientWithToken(ApiFactory.CreateTestToken([Roles.Staff]));
        var customer = await factory.CustomerClientAsync();

        Assert.Equal(HttpStatusCode.OK, (await staff.Get(Admin)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.Get(Admin)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.Get($"{Admin}/{Guid.NewGuid()}")).StatusCode);
    }
}
