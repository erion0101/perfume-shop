using PerfumeShop.Modules.Customers.Domain.Customers;

namespace PerfumeShop.Modules.Customers.Tests.Domain;

public sealed class CustomerTests
{
    private static readonly DateTime Now = new(2026, 11, 25, 10, 0, 0, DateTimeKind.Utc);

    private static Customer NewCustomer() => Customer.Register(TestHelpers.Phone(), Now);

    [Fact]
    public void Registered_customer_is_active_without_name_or_addresses()
    {
        var customer = NewCustomer();

        Assert.Equal(CustomerStatus.Active, customer.Status);
        Assert.Null(customer.FullName);
        Assert.Empty(customer.Addresses);
        Assert.Equal(Now, customer.PhoneVerifiedAtUtc);
    }

    [Fact]
    public void Profile_normalizes_name_and_email()
    {
        var customer = NewCustomer();

        customer.UpdateProfile("  Arbër   Krasniqi ", " Arber.K@Example.COM ").Ok();

        Assert.Equal("Arbër Krasniqi", customer.FullName);
        Assert.Equal("arber.k@example.com", customer.Email);
    }

    [Theory]
    [InlineData("A", null, "Customers.Customer.FullNameInvalid")]
    [InlineData("Arbër", "not-an-email", "Customers.Customer.EmailInvalid")]
    [InlineData("Arbër", "arber@localhost", "Customers.Customer.EmailInvalid")]
    public void Profile_rejects_invalid_values(string? name, string? email, string code) =>
        NewCustomer().UpdateProfile(name, email).Fails(code);

    [Fact]
    public void Block_needs_a_reason_and_cannot_repeat()
    {
        var customer = NewCustomer();

        customer.Block(" ", Now).Fails("Customers.Customer.BlockReasonInvalid");
        customer.Block("Refuzoi 3 porosi COD", Now).Ok();
        customer.Block("again", Now).Fails("Customers.Customer.AlreadyBlocked");

        Assert.True(customer.IsBlocked);
        Assert.Equal("Refuzoi 3 porosi COD", customer.BlockReason);
    }

    [Fact]
    public void Unblock_clears_the_block()
    {
        var customer = NewCustomer();
        customer.Unblock().Fails("Customers.Customer.NotBlocked");
        customer.Block("test", Now).Ok();

        customer.Unblock().Ok();

        Assert.Equal(CustomerStatus.Active, customer.Status);
        Assert.Null(customer.BlockReason);
    }

    // ---------- addresses: exactly one default ----------

    [Fact]
    public void First_address_becomes_the_default()
    {
        var customer = NewCustomer();

        var home = customer.AddAddress("Lipjan", "Rr. Adem Jashari 12", "kati 2", makeDefault: false).Ok();

        Assert.True(home.IsDefault);
    }

    [Fact]
    public void Making_another_address_default_moves_the_flag()
    {
        var customer = NewCustomer();
        var home = customer.AddAddress("Lipjan", "Rr. Adem Jashari 12", null, false).Ok();
        var work = customer.AddAddress("Prishtinë", "Rr. Nënë Tereza 5", null, false).Ok();

        Assert.False(work.IsDefault);
        customer.SetDefaultAddress(work.Id).Ok();

        Assert.True(work.IsDefault);
        Assert.False(home.IsDefault);
        Assert.Single(customer.Addresses, a => a.IsDefault);
    }

    [Fact]
    public void Adding_with_make_default_moves_the_flag()
    {
        var customer = NewCustomer();
        customer.AddAddress("Lipjan", "Rr. 1", null, false).Ok();

        var second = customer.AddAddress("Ferizaj", "Rr. 2", null, makeDefault: true).Ok();

        Assert.Same(second, customer.DefaultAddress);
        Assert.Single(customer.Addresses, a => a.IsDefault);
    }

    [Fact]
    public void Removing_the_default_promotes_another_address()
    {
        var customer = NewCustomer();
        var first = customer.AddAddress("Lipjan", "Rr. 1", null, false).Ok();
        var second = customer.AddAddress("Ferizaj", "Rr. 2", null, false).Ok();

        customer.RemoveAddress(first.Id).Ok();

        Assert.True(second.IsDefault);
    }

    [Fact]
    public void Removing_the_last_address_leaves_no_default()
    {
        var customer = NewCustomer();
        var only = customer.AddAddress("Lipjan", "Rr. 1", null, false).Ok();

        customer.RemoveAddress(only.Id).Ok();

        Assert.Null(customer.DefaultAddress);
    }

    [Fact]
    public void At_most_ten_addresses()
    {
        var customer = NewCustomer();
        for (var i = 0; i < Customer.MaxAddresses; i++)
        {
            customer.AddAddress("Lipjan", $"Rr. {i}", null, false).Ok();
        }

        customer.AddAddress("Lipjan", "Rr. 11", null, false).Fails("Customers.Address.TooMany");
    }

    [Theory]
    [InlineData("", "Rr. 1", "Customers.Address.CityInvalid")]
    [InlineData("Lipjan", " ", "Customers.Address.StreetInvalid")]
    public void Address_requires_city_and_street(string city, string street, string code) =>
        NewCustomer().AddAddress(city, street, null, false).Fails(code);

    [Fact]
    public void Unknown_address_is_not_found()
    {
        var customer = NewCustomer();

        customer.SetDefaultAddress(Guid.NewGuid()).Fails("Customers.Address.NotFound");
        customer.RemoveAddress(Guid.NewGuid()).Fails("Customers.Address.NotFound");
        customer.UpdateAddress(Guid.NewGuid(), "Lipjan", "Rr. 1", null).Fails("Customers.Address.NotFound");
    }
}
