using System.Net.Mail;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Administration.Domain;

/// <summary>The shop's legal/contact details (one row). Receipts, packing slips and SMS read them.</summary>
internal sealed class BusinessProfile : IAuditable
{
    public const int SingletonId = 1;

    /// <summary>Values shipped in appsettings.json until the real ones are entered.</summary>
    public const string PlaceholderPhone = "+383 44 000 000";

    public int Id { get; private set; } = SingletonId;

    public string ShopName { get; private set; } = "";

    public string Address { get; private set; } = "";

    public string Phone { get; private set; } = "";

    public string? Email { get; private set; }

    /// <summary>NUI (Numri Unik Identifikues), 9 digits in Kosovo.</summary>
    public string TaxNumber { get; private set; } = "";

    public string? UpdatedBy { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static BusinessProfile Seed(string? shopName, string? address, string? phone, string? taxNumber) => new()
    {
        ShopName = shopName?.Trim() ?? "",
        Address = address?.Trim() ?? "",
        Phone = phone?.Trim() ?? "",
        TaxNumber = taxNumber?.Trim() ?? "",
    };

    public IReadOnlyList<string> PlaceholderFields()
    {
        List<string> fields = [];
        if (string.IsNullOrWhiteSpace(ShopName))
        {
            fields.Add("shopName");
        }

        if (string.IsNullOrWhiteSpace(Address))
        {
            fields.Add("address");
        }

        if (string.IsNullOrWhiteSpace(Phone) || Phone == PlaceholderPhone)
        {
            fields.Add("phone");
        }

        if (string.IsNullOrWhiteSpace(TaxNumber) || TaxNumber.All(c => c == '0'))
        {
            fields.Add("taxNumber");
        }

        return fields;
    }

    public Result Update(string shopName, string address, string phone, string? email, string taxNumber, string actorId)
    {
        shopName = shopName.Trim();
        address = address.Trim();
        phone = phone.Trim();
        taxNumber = taxNumber.Trim();
        email = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

        if (shopName.Length is < 1 or > 100 || address.Length is < 3 or > 200 || phone.Length is < 6 or > 30)
        {
            return Error.Validation("Administration.Business.Invalid", "Shop name (1-100), address (3-200) and phone (6-30) are required.");
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(taxNumber, "^[0-9]{8,10}$"))
        {
            return Error.Validation("Administration.Business.TaxNumberInvalid", "The fiscal number (NUI) has 8-10 digits.");
        }

        if (email is not null && (!MailAddress.TryCreate(email, out var parsed) || parsed.Address != email || email.Length > 200))
        {
            return Error.Validation("Administration.Business.EmailInvalid", "The email address is not valid.");
        }

        ShopName = shopName;
        Address = address;
        Phone = phone;
        Email = email;
        TaxNumber = taxNumber;
        UpdatedBy = actorId;
        return Result.Success();
    }
}

/// <summary>One audit log row (append-only).</summary>
internal sealed class AuditRecord
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public DateTime OccurredAtUtc { get; init; }

    public required string ActorId { get; init; }

    public string? ActorEmail { get; init; }

    public required string Action { get; init; }

    public required string EntityType { get; init; }

    public string? EntityId { get; init; }

    public required string Summary { get; init; }

    /// <summary>JSON (jsonb): before/after values or inputs.</summary>
    public string? Changes { get; init; }

    public string? IpAddress { get; init; }
}
