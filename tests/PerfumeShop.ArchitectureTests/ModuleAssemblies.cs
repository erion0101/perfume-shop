using System.Reflection;
using PerfumeShop.Modules.Administration;
using PerfumeShop.Modules.Administration.Contracts;
using PerfumeShop.Modules.Catalog;
using PerfumeShop.Modules.Catalog.Contracts;
using PerfumeShop.Modules.Customers;
using PerfumeShop.Modules.Customers.Contracts;
using PerfumeShop.Modules.Inventory;
using PerfumeShop.Modules.Inventory.Contracts;
using PerfumeShop.Modules.Notifications;
using PerfumeShop.Modules.Notifications.Contracts;
using PerfumeShop.Modules.Orders;
using PerfumeShop.Modules.Orders.Contracts;
using PerfumeShop.Modules.Payments;
using PerfumeShop.Modules.Payments.Contracts;
using PerfumeShop.Modules.Shipping;
using PerfumeShop.Modules.Shipping.Contracts;

namespace PerfumeShop.ArchitectureTests;

internal sealed record ModuleAssembly(string Name, Assembly Implementation, Assembly Contracts)
{
    /// <summary>
    /// Full names of the module's internal types. Matching on type names (not on the root
    /// namespace) keeps "PerfumeShop.Modules.Orders" from also matching ".Orders.Contracts".
    /// </summary>
    public string[] InternalTypeNames() =>
        Implementation.GetTypes()
            .Where(t => !t.IsNested && t.Namespace?.StartsWith("PerfumeShop.Modules.", StringComparison.Ordinal) == true)
            .Select(t => t.FullName!)
            .ToArray();
}

internal static class ModuleAssemblies
{
    public static readonly IReadOnlyList<ModuleAssembly> All =
    [
        new("Catalog", typeof(CatalogModule).Assembly, typeof(ICatalogApi).Assembly),
        new("Inventory", typeof(InventoryModule).Assembly, typeof(IInventoryApi).Assembly),
        new("Orders", typeof(OrdersModule).Assembly, typeof(IOrdersApi).Assembly),
        new("Payments", typeof(PaymentsModule).Assembly, typeof(IPaymentsApi).Assembly),
        new("Shipping", typeof(ShippingModule).Assembly, typeof(IShippingApi).Assembly),
        new("Customers", typeof(CustomersModule).Assembly, typeof(ICustomersApi).Assembly),
        new("Notifications", typeof(NotificationsModule).Assembly, typeof(INotificationsApi).Assembly),
        new("Administration", typeof(AdministrationModule).Assembly, typeof(IBusinessProfileApi).Assembly),
    ];

    public static ModuleAssembly Get(string name) => All.Single(m => m.Name == name);

    public static TheoryData<string> Names() => new(All.Select(m => m.Name));
}
