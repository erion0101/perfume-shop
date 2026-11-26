using PerfumeShop.Modules.Customers.Domain.Customers;
using PerfumeShop.Modules.Customers.Infrastructure.Auth;

namespace PerfumeShop.Modules.Customers.Features.Shared;

internal sealed record AddressDto(Guid Id, string CountryCode, string City, string Street, string? Notes, bool IsDefault);

/// <param name="IsProfileComplete">False until the customer has a name (asked at first checkout).</param>
internal sealed record CustomerProfileDto(
    Guid Id,
    string Phone,
    string? FullName,
    string? Email,
    bool IsProfileComplete,
    IReadOnlyList<AddressDto> Addresses);

internal sealed record CustomerAdminDto(
    Guid Id,
    string Phone,
    string? FullName,
    string? Email,
    string Status,
    string? BlockReason,
    DateTime? BlockedAtUtc,
    DateTime PhoneVerifiedAtUtc,
    DateTime? LastLoginAtUtc,
    DateTime CreatedAtUtc,
    IReadOnlyList<AddressDto> Addresses);

/// <summary>Returned by every successful sign-in and refresh.</summary>
internal sealed record LoginResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    string TokenType,
    Guid? CustomerId = null,
    bool? IsNewCustomer = null)
{
    public static LoginResponse From(AuthTokens tokens, Guid? customerId = null, bool? isNewCustomer = null) =>
        new(tokens.AccessToken, tokens.AccessTokenExpiresAtUtc, tokens.RefreshToken, tokens.RefreshTokenExpiresAtUtc, tokens.TokenType, customerId, isNewCustomer);
}

internal static class CustomerDtoMappings
{
    public static AddressDto ToDto(this Address a) => new(a.Id, a.CountryCode, a.City, a.Street, a.Notes, a.IsDefault);

    public static IReadOnlyList<AddressDto> AddressDtos(this Customer c) =>
        [.. c.Addresses.OrderByDescending(a => a.IsDefault).ThenBy(a => a.Id).Select(ToDto)];

    public static CustomerProfileDto ToProfileDto(this Customer c) =>
        new(c.Id, c.Phone.Value, c.FullName, c.Email, c.FullName is not null, c.AddressDtos());

    public static CustomerAdminDto ToAdminDto(this Customer c) => new(
        c.Id, c.Phone.Value, c.FullName, c.Email, c.Status.ToString(), c.BlockReason, c.BlockedAtUtc,
        c.PhoneVerifiedAtUtc, c.LastLoginAtUtc, c.CreatedAtUtc, c.AddressDtos());
}
