namespace PerfumeShop.Modules.Inventory.Infrastructure;

/// <summary>Section "Inventory".</summary>
internal sealed class InventoryOptions
{
    public const string SectionName = "Inventory";

    /// <summary>Low-stock threshold for variants received for the first time (changeable per variant).</summary>
    public int DefaultLowStockThreshold { get; set; } = 3;

    /// <summary>How often a stock change is retried after losing a concurrency race.</summary>
    public int MaxConcurrencyRetries { get; set; } = 5;
}
