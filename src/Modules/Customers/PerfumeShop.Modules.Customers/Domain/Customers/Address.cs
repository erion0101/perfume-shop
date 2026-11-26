using PerfumeShop.Modules.Customers.Domain.Common;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Customers.Domain.Customers;

/// <summary>Delivery address. Part of the <see cref="Customer"/> aggregate.</summary>
internal sealed class Address : Entity<Guid>, IAuditable
{
    public const int CityMaxLength = 100;
    public const int StreetMaxLength = 200;
    public const int NotesMaxLength = 500;

    /// <summary>Kosovo (XK, the code used by couriers and the EU), Albania, North Macedonia.</summary>
    public static readonly IReadOnlyList<string> SupportedCountries = ["XK", "AL", "MK"];
    public const string DefaultCountry = "XK";

    private Address() { }

    private Address(Guid id, Guid customerId) : base(id) => CustomerId = customerId;

    public Guid CustomerId { get; private set; }

    public string CountryCode { get; private set; } = DefaultCountry;

    public string City { get; private set; } = null!;

    public string Street { get; private set; } = null!;

    /// <summary>Directions for the courier, e.g. "kati 2, afër shkollës".</summary>
    public string? Notes { get; private set; }

    public bool IsDefault { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    internal static Result<Address> Create(Guid customerId, string? countryCode, string? city, string? street, string? notes)
    {
        var address = new Address(Guid.CreateVersion7(), customerId);
        var changed = address.Change(countryCode, city, street, notes);
        return changed.IsFailure ? changed.Error : address;
    }

    internal Result Change(string? countryCode, string? city, string? street, string? notes)
    {
        countryCode = string.IsNullOrWhiteSpace(countryCode) ? DefaultCountry : countryCode.Trim().ToUpperInvariant();
        if (!SupportedCountries.Contains(countryCode))
        {
            return CustomerErrors.CountryNotSupported;
        }

        city = city?.Trim();
        street = street?.Trim();
        notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

        if (string.IsNullOrEmpty(city) || city.Length > CityMaxLength)
        {
            return CustomerErrors.CityInvalid;
        }

        if (string.IsNullOrEmpty(street) || street.Length > StreetMaxLength)
        {
            return CustomerErrors.StreetInvalid;
        }

        if (notes?.Length > NotesMaxLength)
        {
            return CustomerErrors.NotesTooLong;
        }

        CountryCode = countryCode;
        City = city;
        Street = street;
        Notes = notes;
        return Result.Success();
    }

    internal void MarkDefault(bool isDefault) => IsDefault = isDefault;
}
