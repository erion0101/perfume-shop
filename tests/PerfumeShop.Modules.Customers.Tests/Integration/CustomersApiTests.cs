using Microsoft.Extensions.DependencyInjection;
using PerfumeShop.Modules.Customers.Contracts;
using PerfumeShop.Modules.Customers.Features.Shared;

namespace PerfumeShop.Modules.Customers.Tests.Integration;

public sealed class CustomersApiTests(CustomersApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Returns_the_summary_and_only_the_customers_own_address()
    {
        var phone = TestHelpers.UniquePhone();
        var login = await factory.LoginCustomerAsync(phone);
        var client = factory.CreateClientWithToken(login.AccessToken);
        await client.PutJson("/api/customers/me", new { fullName = "Drita Berisha" });
        var address = await (await client.PostJson("/api/customers/me/addresses", new { city = "Pejë", street = "Rr. Mbretëresha Teutë 3" })).Read<AddressDto>();

        await using var scope = factory.CreateScope();
        var api = scope.ServiceProvider.GetRequiredService<ICustomersApi>();
        var customerId = login.CustomerId!.Value;

        var summary = await api.GetCustomerSummaryAsync(customerId, Ct);
        var own = await api.GetAddressAsync(customerId, address.Id, Ct);
        var foreign = await api.GetAddressAsync(Guid.NewGuid(), address.Id, Ct);

        Assert.Equal(new CustomerSummaryDto(customerId, "Drita Berisha", phone, null, "Active", false), summary);
        Assert.Equal(new CustomerAddressDto(address.Id, customerId, "XK", "Pejë", "Rr. Mbretëresha Teutë 3", null, true), own);
        Assert.Null(foreign);
        Assert.Null(await api.GetCustomerSummaryAsync(Guid.NewGuid(), Ct));
    }
}
