using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Orders.Domain;

/// <summary>Delivery fee for one country, with an optional free-delivery threshold.</summary>
internal sealed class DeliveryRate
{
    public const decimal MaxFee = 100m;

    private DeliveryRate() { }

    internal DeliveryRate(string countryCode, string countryName, bool isEnabled, decimal fee, decimal? freeDeliveryFrom)
    {
        CountryCode = countryCode;
        CountryName = countryName;
        IsEnabled = isEnabled;
        Fee = fee;
        FreeDeliveryFrom = freeDeliveryFrom;
    }

    /// <summary>XK (Kosovo), AL (Albania), MK (North Macedonia).</summary>
    public string CountryCode { get; private init; } = null!;

    public string CountryName { get; private init; } = null!;

    public bool IsEnabled { get; private set; }

    /// <summary>EUR, VAT included.</summary>
    public decimal Fee { get; private set; }

    /// <summary>Subtotal (EUR) from which delivery is free; null = never free.</summary>
    public decimal? FreeDeliveryFrom { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public decimal FeeFor(decimal subtotal) => FreeDeliveryFrom is { } from && subtotal >= from ? 0m : Fee;

    public Result Update(bool isEnabled, decimal fee, decimal? freeDeliveryFrom, DateTime nowUtc)
    {
        if (fee < 0 || fee > MaxFee || decimal.Round(fee, 2) != fee || freeDeliveryFrom is <= 0 || (freeDeliveryFrom is { } f && decimal.Round(f, 2) != f))
        {
            return Error.Validation("Orders.Delivery.Invalid", $"Fee must be 0-{MaxFee} EUR and the free-delivery threshold above 0 (2 decimals).");
        }

        IsEnabled = isEnabled;
        Fee = fee;
        FreeDeliveryFrom = freeDeliveryFrom;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }
}

/// <summary>Anti-fake-order rules and timing (single row, editable by Admin).</summary>
internal sealed class OrderSettings
{
    public const int SingletonId = 1;

    private OrderSettings() { }

    internal OrderSettings(int maxOpenOrdersPerCustomer, int maxRefusedOrders, int pendingExpiryHours)
    {
        MaxOpenOrdersPerCustomer = maxOpenOrdersPerCustomer;
        MaxRefusedOrders = maxRefusedOrders;
        PendingExpiryHours = pendingExpiryHours;
    }

    public int Id { get; private init; } = SingletonId;

    /// <summary>Pending + Confirmed + Shipped orders one customer may have at once.</summary>
    public int MaxOpenOrdersPerCustomer { get; private set; }

    /// <summary>Customers with MORE refused parcels than this cannot order anymore.</summary>
    public int MaxRefusedOrders { get; private set; }

    /// <summary>Pending orders not confirmed within this many hours expire.</summary>
    public int PendingExpiryHours { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static OrderSettings Defaults => new(maxOpenOrdersPerCustomer: 2, maxRefusedOrders: 2, pendingExpiryHours: 48);

    public Result Update(int maxOpenOrdersPerCustomer, int maxRefusedOrders, int pendingExpiryHours, DateTime nowUtc)
    {
        if (maxOpenOrdersPerCustomer is < 1 or > 20 || maxRefusedOrders is < 0 or > 20 || pendingExpiryHours is < 1 or > 720)
        {
            return Error.Validation("Orders.Settings.Invalid", "Open orders 1-20, refused limit 0-20, expiry 1-720 hours.");
        }

        MaxOpenOrdersPerCustomer = maxOpenOrdersPerCustomer;
        MaxRefusedOrders = maxRefusedOrders;
        PendingExpiryHours = pendingExpiryHours;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }
}
