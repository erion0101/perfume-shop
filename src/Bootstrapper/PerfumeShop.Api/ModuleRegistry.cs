using PerfumeShop.Modules.Catalog;
using PerfumeShop.Modules.Customers;
using PerfumeShop.Modules.Inventory;
using PerfumeShop.Modules.Notifications;
using PerfumeShop.Modules.Orders;
using PerfumeShop.Modules.Payments;
using PerfumeShop.Modules.Shipping;
using PerfumeShop.Shared.Infrastructure.Modules;

namespace PerfumeShop.Api;

/// <summary>Every module loaded by the host. Add new modules here.</summary>
internal static class ModuleRegistry
{
    public static IReadOnlyList<IModule> All { get; } =
    [
        new CatalogModule(),
        new InventoryModule(),
        new OrdersModule(),
        new PaymentsModule(),
        new ShippingModule(),
        new CustomersModule(),
        new NotificationsModule(),
    ];
}
