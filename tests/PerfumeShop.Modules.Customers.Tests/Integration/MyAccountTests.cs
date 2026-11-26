using System.Net;
using PerfumeShop.Modules.Customers.Features.Shared;

namespace PerfumeShop.Modules.Customers.Tests.Integration;

public sealed class MyAccountTests(CustomersApiFactory factory)
{
    private const string Me = "/api/customers/me";

    [Fact]
    public async Task New_customer_profile_is_incomplete_until_a_name_is_set()
    {
        var phone = TestHelpers.UniquePhone();
        var client = await factory.CustomerClientAsync(phone);

        var before = await (await client.Get(Me)).Read<CustomerProfileDto>();
        var updated = await client.PutJson(Me, new { fullName = "Arbër Krasniqi", email = "arber@example.com" });
        var after = await updated.Read<CustomerProfileDto>();

        Assert.Equal(phone, before.Phone);
        Assert.False(before.IsProfileComplete);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.True(after.IsProfileComplete);
        Assert.Equal("Arbër Krasniqi", after.FullName);
    }

    [Fact]
    public async Task Invalid_profile_values_are_rejected()
    {
        var client = await factory.CustomerClientAsync();

        var response = await client.PutJson(Me, new { fullName = "Arbër", email = "nope" });

        Assert.Equal("Customers.Customer.EmailInvalid", await response.ErrorCode());
    }

    [Fact]
    public async Task Address_lifecycle_keeps_exactly_one_default_in_the_database()
    {
        var client = await factory.CustomerClientAsync();

        var homeResponse = await client.PostJson($"{Me}/addresses", new { city = "Lipjan", street = "Rr. Adem Jashari 12", notes = "kati 2" });
        Assert.Equal(HttpStatusCode.Created, homeResponse.StatusCode);
        var home = await homeResponse.Read<AddressDto>();
        var work = await (await client.PostJson($"{Me}/addresses", new { city = "Prishtinë", street = "Rr. Nënë Tereza 5" })).Read<AddressDto>();
        Assert.True(home.IsDefault);
        Assert.False(work.IsDefault);

        // Switching the default touches two rows in one save (deferred exclusion constraint).
        var switched = await (await client.PostJson($"{Me}/addresses/{work.Id}/default")).Read<List<AddressDto>>();
        Assert.Equal(work.Id, switched.Single(a => a.IsDefault).Id);

        var edited = await client.PutJson($"{Me}/addresses/{home.Id}", new { city = "Lipjan", street = "Rr. Adem Jashari 14", notes = (string?)null });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);

        var afterDelete = await (await client.Delete($"{Me}/addresses/{work.Id}")).Read<List<AddressDto>>();
        var remaining = Assert.Single(afterDelete);
        Assert.Equal(home.Id, remaining.Id);
        Assert.True(remaining.IsDefault);
        Assert.Equal("Rr. Adem Jashari 14", remaining.Street);

        var stored = await (await client.Get($"{Me}/addresses")).Read<List<AddressDto>>();
        Assert.Single(stored, a => a.IsDefault);
    }

    [Fact]
    public async Task Customers_cannot_touch_each_others_addresses()
    {
        var owner = await factory.CustomerClientAsync();
        var other = await factory.CustomerClientAsync();
        var address = await (await owner.PostJson($"{Me}/addresses", new { city = "Ferizaj", street = "Rr. 1" })).Read<AddressDto>();

        var response = await other.Delete($"{Me}/addresses/{address.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Account_endpoints_need_a_customer_token()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().Get(Me)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateAdminClient().Get(Me)).StatusCode);
    }
}
