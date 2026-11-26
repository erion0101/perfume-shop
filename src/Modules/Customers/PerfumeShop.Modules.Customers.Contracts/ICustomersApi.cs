using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Customers.Contracts;

/// <summary>Synchronous API other modules use (e.g. Orders at checkout).</summary>
public interface ICustomersApi
{
    /// <summary>Null when the customer does not exist.</summary>
    Task<CustomerSummaryDto?> GetCustomerSummaryAsync(Guid customerId, CancellationToken cancellationToken = default);

    /// <summary>Null when the address does not exist or belongs to another customer.</summary>
    Task<CustomerAddressDto?> GetAddressAsync(Guid customerId, Guid addressId, CancellationToken cancellationToken = default);

    /// <summary>The customer's default address, or null when they have none.</summary>
    Task<CustomerAddressDto?> GetDefaultAddressAsync(Guid customerId, CancellationToken cancellationToken = default);

    /// <summary>Sets the customer's full name (checkout asks for it when the profile has none).</summary>
    Task<Result> SetFullNameAsync(Guid customerId, string fullName, CancellationToken cancellationToken = default);
}

/// <param name="PhoneE164">Verified mobile number, e.g. "+38344123456".</param>
/// <param name="Status">Active or Blocked.</param>
public sealed record CustomerSummaryDto(Guid CustomerId, string? FullName, string PhoneE164, string? Email, string Status, bool IsBlocked);

/// <param name="CountryCode">XK, AL or MK.</param>
public sealed record CustomerAddressDto(Guid AddressId, Guid CustomerId, string CountryCode, string City, string Street, string? Notes, bool IsDefault);
