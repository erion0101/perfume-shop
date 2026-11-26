using System.Net.Mail;
using PerfumeShop.Modules.Customers.Domain.Common;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Customers.Domain.Customers;

internal enum CustomerStatus
{
    Active = 1,
    Blocked = 2,
}

/// <summary>
/// A shop customer, identified by a verified mobile number. Created on the first OTP login,
/// so the name is filled in later (profile or first checkout).
/// Invariant: when there are addresses, exactly one of them is the default.
/// </summary>
internal sealed class Customer : AggregateRoot<Guid>, IAuditable
{
    public const int FullNameMaxLength = 100;
    public const int EmailMaxLength = 254;
    public const int BlockReasonMaxLength = 500;
    public const int MaxAddresses = 10;

    private readonly List<Address> _addresses = [];

    private Customer() { }

    private Customer(Guid id, PhoneNumber phone, DateTime verifiedAtUtc) : base(id)
    {
        Phone = phone;
        PhoneVerifiedAtUtc = verifiedAtUtc;
        Status = CustomerStatus.Active;
    }

    public PhoneNumber Phone { get; private set; } = null!;

    public string? FullName { get; private set; }

    public string? Email { get; private set; }

    public CustomerStatus Status { get; private set; }

    public string? BlockReason { get; private set; }

    public DateTime? BlockedAtUtc { get; private set; }

    public DateTime PhoneVerifiedAtUtc { get; private set; }

    public DateTime? LastLoginAtUtc { get; private set; }

    public IReadOnlyList<Address> Addresses => _addresses.AsReadOnly();

    public Address? DefaultAddress => _addresses.Find(a => a.IsDefault);

    public bool IsBlocked => Status == CustomerStatus.Blocked;

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>The phone was just verified with an OTP code.</summary>
    public static Customer Register(PhoneNumber phone, DateTime verifiedAtUtc) =>
        new(Guid.CreateVersion7(), phone, verifiedAtUtc);

    public Result UpdateProfile(string? fullName, string? email)
    {
        fullName = string.IsNullOrWhiteSpace(fullName) ? null : string.Join(' ', fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (fullName is not null && fullName.Length is < 2 or > FullNameMaxLength)
        {
            return CustomerErrors.FullNameInvalid;
        }

        email = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
        if (email is not null && !IsValidEmail(email))
        {
            return CustomerErrors.EmailInvalid;
        }

        FullName = fullName;
        Email = email;
        return Result.Success();
    }

    public void RecordLogin(DateTime atUtc) => LastLoginAtUtc = atUtc;

    public Result Block(string? reason, DateTime atUtc)
    {
        reason = reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > BlockReasonMaxLength)
        {
            return CustomerErrors.BlockReasonInvalid;
        }

        if (IsBlocked)
        {
            return CustomerErrors.AlreadyBlocked;
        }

        Status = CustomerStatus.Blocked;
        BlockReason = reason;
        BlockedAtUtc = atUtc;
        return Result.Success();
    }

    public Result Unblock()
    {
        if (!IsBlocked)
        {
            return CustomerErrors.NotBlocked;
        }

        Status = CustomerStatus.Active;
        BlockReason = null;
        BlockedAtUtc = null;
        return Result.Success();
    }

    /// <summary>The first address always becomes the default.</summary>
    public Result<Address> AddAddress(string? city, string? street, string? notes, bool makeDefault, string? countryCode = null)
    {
        if (_addresses.Count >= MaxAddresses)
        {
            return CustomerErrors.TooManyAddresses;
        }

        var address = Address.Create(Id, countryCode, city, street, notes);
        if (address.IsFailure)
        {
            return address;
        }

        _addresses.Add(address.Value);
        if (makeDefault || _addresses.Count == 1)
        {
            MakeDefault(address.Value);
        }

        return address;
    }

    public Result UpdateAddress(Guid addressId, string? city, string? street, string? notes, string? countryCode = null)
    {
        var address = _addresses.Find(a => a.Id == addressId);
        return address is null ? CustomerErrors.AddressNotFound : address.Change(countryCode, city, street, notes);
    }

    public Result SetDefaultAddress(Guid addressId)
    {
        var address = _addresses.Find(a => a.Id == addressId);
        if (address is null)
        {
            return CustomerErrors.AddressNotFound;
        }

        MakeDefault(address);
        return Result.Success();
    }

    /// <summary>Removing the default promotes the oldest remaining address.</summary>
    public Result RemoveAddress(Guid addressId)
    {
        var address = _addresses.Find(a => a.Id == addressId);
        if (address is null)
        {
            return CustomerErrors.AddressNotFound;
        }

        _addresses.Remove(address);
        if (address.IsDefault && _addresses.Count > 0)
        {
            MakeDefault(_addresses.OrderBy(a => a.CreatedAtUtc).ThenBy(a => a.Id).First());
        }

        return Result.Success();
    }

    private void MakeDefault(Address chosen)
    {
        foreach (var address in _addresses)
        {
            address.MarkDefault(address == chosen);
        }
    }

    private static bool IsValidEmail(string email) =>
        email.Length <= EmailMaxLength
        && MailAddress.TryCreate(email, out var parsed)
        && parsed.Address == email
        && parsed.Host.Contains('.', StringComparison.Ordinal);
}
