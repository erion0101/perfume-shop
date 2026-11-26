using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Customers.Contracts;
using PerfumeShop.Modules.Customers.Domain.Common;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Customers.Infrastructure;

internal sealed class CustomersApi(CustomersDbContext db) : ICustomersApi
{
    public async Task<CustomerSummaryDto?> GetCustomerSummaryAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);
        return customer is null
            ? null
            : new CustomerSummaryDto(customer.Id, customer.FullName, customer.Phone.Value, customer.Email, customer.Status.ToString(), customer.IsBlocked);
    }

    public async Task<CustomerAddressDto?> GetAddressAsync(Guid customerId, Guid addressId, CancellationToken cancellationToken = default)
    {
        var address = await db.Addresses.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == addressId && a.CustomerId == customerId, cancellationToken);
        return address is null ? null : ToDto(address);
    }

    public async Task<CustomerAddressDto?> GetDefaultAddressAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        var address = await db.Addresses.AsNoTracking()
            .FirstOrDefaultAsync(a => a.CustomerId == customerId && a.IsDefault, cancellationToken);
        return address is null ? null : ToDto(address);
    }

    public async Task<Result> SetFullNameAsync(Guid customerId, string fullName, CancellationToken cancellationToken = default)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);
        if (customer is null)
        {
            return Error.NotFound("Customers.Customer.NotFound", "Customer not found.");
        }

        if (customer.IsBlocked)
        {
            return CustomerErrors.Blocked;
        }

        var updated = customer.UpdateProfile(fullName, customer.Email);
        if (updated.IsFailure)
        {
            return updated;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static CustomerAddressDto ToDto(Domain.Customers.Address address) =>
        new(address.Id, address.CustomerId, address.CountryCode, address.City, address.Street, address.Notes, address.IsDefault);
}
