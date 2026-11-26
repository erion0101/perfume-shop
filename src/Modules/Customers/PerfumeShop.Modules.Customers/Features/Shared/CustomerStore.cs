using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Customers.Domain.Common;
using PerfumeShop.Modules.Customers.Domain.Customers;
using PerfumeShop.Modules.Customers.Infrastructure;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Customers.Features.Shared;

internal static class CustomerFeatureErrors
{
    public static readonly Error CustomerNotFound = Error.NotFound("Customers.Customer.NotFound", "Customer not found.");
    public static readonly Error InvalidCredentials = Error.Unauthorized("Customers.Auth.InvalidCredentials", "Email or password is wrong.");
    public static readonly Error LockedOut = Error.LimitExceeded("Customers.Auth.LockedOut", "Too many failed sign-ins. Try again in 15 minutes.");
    public static readonly Error Deactivated = Error.Forbidden("Customers.Auth.Deactivated", "This staff account is deactivated.");
    public static readonly Error PasswordChangeRequired = Error.Forbidden("Customers.Auth.PasswordChangeRequired", "Choose a new password before signing in (temporary password).");
    public static readonly Error TwoFactorRequired = Error.Unauthorized("Customers.Auth.TwoFactorRequired", "Enter the 6-digit code from your authenticator app.");
    public static readonly Error InvalidTwoFactorCode = Error.Unauthorized("Customers.Auth.InvalidTwoFactorCode", "The authenticator or recovery code is wrong.");
}

internal static class CustomerStore
{
    public static async Task<Result<Customer>> LoadAsync(this CustomersDbContext db, Guid customerId, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.Include(c => c.Addresses).FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);
        return customer is null ? CustomerFeatureErrors.CustomerNotFound : customer;
    }

    /// <summary>For the customer's own endpoints: a blocked customer gets 403 even with a still-valid access token.</summary>
    public static async Task<Result<Customer>> LoadActiveAsync(this CustomersDbContext db, Guid customerId, CancellationToken cancellationToken)
    {
        var customer = await db.LoadAsync(customerId, cancellationToken);
        return customer.IsSuccess && customer.Value.IsBlocked ? CustomerErrors.Blocked : customer;
    }

    /// <summary>Serializes OTP work per phone number for the current transaction (PostgreSQL advisory lock).</summary>
    public static Task LockPhoneAsync(this CustomersDbContext db, PhoneNumber phone, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtext({phone.Value}))", cancellationToken);

    public static string? ClientIp(this HttpContext http) => http.Connection.RemoteIpAddress?.ToString();
}
