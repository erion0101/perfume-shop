using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Shipping.Domain;

internal enum RemittanceSchedule
{
    Daily = 1,
    Weekly = 2,
    BiWeekly = 3,
    Monthly = 4,
}

/// <param name="DeliveryFee">What WE pay the courier per parcel (EUR).</param>
/// <param name="CodFeeFixed">COD commission per parcel (EUR).</param>
/// <param name="CodFeePercent">COD commission as % of the collected amount.</param>
/// <param name="RemittanceDays">Days after delivery by which the courier should have paid us.</param>
internal sealed record CourierDetails(
    string Name,
    string? ContactName,
    string? ContactPhone,
    string? ContactEmail,
    IReadOnlyList<string> EnabledCountries,
    decimal DeliveryFee,
    decimal CodFeeFixed,
    decimal CodFeePercent,
    bool AcceptsFlammableGoods,
    RemittanceSchedule RemittanceSchedule,
    int RemittanceDays,
    string? Notes,
    bool IsEnabled);

/// <summary>A road courier (configured by Admin). No courier offers an API, so everything is manual or CSV.</summary>
internal sealed class Courier : AggregateRoot<Guid>, IAuditable
{
    public const string ManualAdapter = "Manual";
    public static readonly IReadOnlyList<string> SupportedCountries = ["XK", "AL", "MK"];

    private List<string> _countries = [];

    private Courier() { }

    private Courier(Guid id) : base(id) { }

    public string Name { get; private set; } = null!;

    public string? ContactName { get; private set; }

    public string? ContactPhone { get; private set; }

    public string? ContactEmail { get; private set; }

    public IReadOnlyList<string> EnabledCountries => _countries.AsReadOnly();

    public decimal DeliveryFee { get; private set; }

    public decimal CodFeeFixed { get; private set; }

    public decimal CodFeePercent { get; private set; }

    /// <summary>Perfume is a dangerous good (UN1266, Class 3): a courier must accept it to be selectable.</summary>
    public bool AcceptsFlammableGoods { get; private set; }

    public RemittanceSchedule RemittanceSchedule { get; private set; }

    public int RemittanceDays { get; private set; }

    public string? Notes { get; private set; }

    public bool IsEnabled { get; private set; }

    /// <summary>Which <c>ICourierAdapter</c> handles manifests and CSV files ("Manual" for now).</summary>
    public string AdapterCode { get; private set; } = ManualAdapter;

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static Result<Courier> Create(CourierDetails details)
    {
        var courier = new Courier(Guid.CreateVersion7());
        var applied = courier.Update(details);
        return applied.IsFailure ? applied.Error : courier;
    }

    public Result Update(CourierDetails d)
    {
        var name = d.Name?.Trim();
        var countries = d.EnabledCountries.Select(c => c.Trim().ToUpperInvariant()).Distinct().ToList();
        if (name is not { Length: > 0 and <= 100 }
            || countries.Any(c => !SupportedCountries.Contains(c))
            || !IsFee(d.DeliveryFee) || !IsFee(d.CodFeeFixed)
            || d.CodFeePercent is < 0 or > 10 || decimal.Round(d.CodFeePercent, 2) != d.CodFeePercent
            || !Enum.IsDefined(d.RemittanceSchedule) || d.RemittanceDays is < 1 or > 60
            || d.Notes?.Length > 1000 || d.ContactName?.Length > 100 || d.ContactPhone?.Length > 30 || d.ContactEmail?.Length > 254)
        {
            return ShippingErrors.CourierInvalid;
        }

        Name = name;
        ContactName = Clean(d.ContactName);
        ContactPhone = Clean(d.ContactPhone);
        ContactEmail = Clean(d.ContactEmail);
        _countries = countries;
        DeliveryFee = d.DeliveryFee;
        CodFeeFixed = d.CodFeeFixed;
        CodFeePercent = d.CodFeePercent;
        AcceptsFlammableGoods = d.AcceptsFlammableGoods;
        RemittanceSchedule = d.RemittanceSchedule;
        RemittanceDays = d.RemittanceDays;
        Notes = Clean(d.Notes);
        IsEnabled = d.IsEnabled;
        return Result.Success();
    }

    public Result EnsureSelectableFor(string countryCode)
    {
        if (!IsEnabled)
        {
            return ShippingErrors.CourierDisabled;
        }

        if (!AcceptsFlammableGoods)
        {
            return ShippingErrors.CourierNotForFlammables;
        }

        return _countries.Contains(countryCode) ? Result.Success() : ShippingErrors.CourierCountry;
    }

    public bool IsSelectableFor(string countryCode) => EnsureSelectableFor(countryCode).IsSuccess;

    /// <summary>Commission we expect the courier to keep for collecting <paramref name="codAmount"/>.</summary>
    public decimal ExpectedCodFee(decimal codAmount) =>
        decimal.Round(CodFeeFixed + (codAmount * CodFeePercent / 100m), 2, MidpointRounding.AwayFromZero);

    private static bool IsFee(decimal fee) => fee is >= 0 and <= 100 && decimal.Round(fee, 2) == fee;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
