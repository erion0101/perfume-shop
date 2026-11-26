using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Customers.Domain.Common;

internal static class CustomerErrors
{
    public static readonly Error PhoneInvalid = Error.Validation(
        "Customers.Phone.Invalid", "Enter a valid mobile number, e.g. +383 44 123 456.");
    public static readonly Error PhoneCountryNotSupported = Error.Validation(
        "Customers.Phone.CountryNotSupported", "Only Kosovo (+383), Albania (+355) and North Macedonia (+389) numbers are supported.");
    public static readonly Error FullNameInvalid = Error.Validation(
        "Customers.Customer.FullNameInvalid", "Full name must be 2-100 characters.");
    public static readonly Error EmailInvalid = Error.Validation(
        "Customers.Customer.EmailInvalid", "Email address is not valid.");
    public static readonly Error AlreadyBlocked = Error.Conflict(
        "Customers.Customer.AlreadyBlocked", "The customer is already blocked.");
    public static readonly Error NotBlocked = Error.Conflict(
        "Customers.Customer.NotBlocked", "The customer is not blocked.");
    public static readonly Error BlockReasonInvalid = Error.Validation(
        "Customers.Customer.BlockReasonInvalid", "A block reason of at most 500 characters is required.");
    public static readonly Error Blocked = Error.Forbidden(
        "Customers.Customer.Blocked", "This account is blocked. Please contact us.");

    public static readonly Error TooManyAddresses = Error.Conflict(
        "Customers.Address.TooMany", $"A customer can have at most {Customers.Customer.MaxAddresses} addresses.");
    public static readonly Error AddressNotFound = Error.NotFound(
        "Customers.Address.NotFound", "Address not found.");
    public static readonly Error CountryNotSupported = Error.Validation(
        "Customers.Address.CountryNotSupported", "We deliver to Kosovo (XK), Albania (AL) and North Macedonia (MK).");
    public static readonly Error CityInvalid = Error.Validation(
        "Customers.Address.CityInvalid", "City is required (max 100 characters).");
    public static readonly Error StreetInvalid = Error.Validation(
        "Customers.Address.StreetInvalid", "Street is required (max 200 characters).");
    public static readonly Error NotesTooLong = Error.Validation(
        "Customers.Address.NotesTooLong", "Notes can be at most 500 characters.");
}

internal static class OtpErrors
{
    public static readonly Error InvalidCode = Error.Validation(
        "Customers.Otp.InvalidCode", "The code is wrong or no code was requested for this number.");
    public static readonly Error Expired = Error.Validation(
        "Customers.Otp.Expired", "The code has expired. Request a new one.");
    public static readonly Error TooManyAttempts = Error.LimitExceeded(
        "Customers.Otp.TooManyAttempts", "Too many wrong codes. Request a new one.");
    public static readonly Error RateLimited = Error.LimitExceeded(
        "Customers.Otp.RateLimited", "Too many codes requested. Try again in a few minutes.");
    public static readonly Error SmsFailed = Error.Unavailable(
        "Customers.Otp.SmsFailed", "We could not send the SMS. Try again later.");

    public static Error WrongCode(int attemptsLeft) => Error.Validation(
        "Customers.Otp.InvalidCode", $"Wrong code. {attemptsLeft} attempt(s) left.");
}
