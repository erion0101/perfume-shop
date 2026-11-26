using PerfumeShop.Modules.Shipping.Domain;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Shipping.Infrastructure.Couriers;

/// <summary>A courier status update read from the courier's file.</summary>
internal sealed record CourierStatusUpdate(int LineNumber, string TrackingNumber, string RawStatus, ShipmentStatus? Status, string? Note);

internal sealed record ManifestFile(string FileName, byte[] Csv, string Html);

/// <summary>
/// How we exchange data with a courier. Local couriers have no APIs, so the only implementation is
/// <see cref="ManualCourierAdapter"/> (CSV files + printable pages). An API-based courier gets its own adapter.
/// </summary>
internal interface ICourierAdapter
{
    string Code { get; }

    /// <summary>Daily handover list: parcels the courier picks up, with the cash to collect.</summary>
    /// <param name="receiptStates">Sale receipt state per order id ("Issued", "Failed", "Missing", ...); every other state is flagged.</param>
    ManifestFile BuildManifest(Courier courier, DateOnly date, IReadOnlyList<Shipment> shipments, IReadOnlyDictionary<Guid, string> receiptStates, ShopInfo shop);

    Result<IReadOnlyList<CourierStatusUpdate>> ParseStatusUpdates(string csv);

    Result<IReadOnlyList<RemittanceLine>> ParseRemittance(string csv);
}

/// <summary>Section "Shipping:Shop": printed on manifests and packing slips.</summary>
internal sealed class ShopInfo
{
    public const string SectionName = "Shipping:Shop";

    public string Name { get; set; } = "PerfumeShop";

    public string Phone { get; set; } = "+383 44 000 000";

    public string Address { get; set; } = "Lipjan, Kosovë";

    /// <summary>"Today" for manifests is a Kosovo calendar day.</summary>
    public string TimeZone { get; set; } = "Europe/Belgrade";
}
