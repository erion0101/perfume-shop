using System.Net;
using PerfumeShop.Modules.Orders.Features.OrderSettings;
using PerfumeShop.Shared.Infrastructure.Auth;
using PerfumeShop.Testing;

namespace PerfumeShop.Modules.Orders.Tests.Integration;

public sealed class SettingsTests(OrdersApiFactory factory)
{
    private const string Admin = "/api/admin/orders";

    [Fact]
    public async Task Admin_reads_and_changes_delivery_rates()
    {
        var admin = factory.CreateAdminClient();
        var rates = await (await admin.Get($"{Admin}/delivery-rates")).Read<List<DeliveryRateDto>>();
        Assert.Equal(["AL", "MK", "XK"], rates.Select(r => r.CountryCode));
        Assert.True(rates.Single(r => r.CountryCode == "XK").IsEnabled);

        var updated = await admin.PutJson($"{Admin}/delivery-rates/MK", new { isEnabled = true, fee = 4.50m, freeDeliveryFrom = 70m });
        var invalid = await admin.PutJson($"{Admin}/delivery-rates/MK", new { isEnabled = true, fee = -1m });
        await admin.PutJson($"{Admin}/delivery-rates/MK", new { isEnabled = false, fee = 5.00m, freeDeliveryFrom = 80m });

        var rate = await updated.Read<DeliveryRateDto>();
        Assert.Equal((true, 4.50m, 70m), (rate.IsEnabled, rate.Fee, rate.FreeDeliveryFrom));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Settings_are_admin_only()
    {
        var staff = factory.CreateClientWithToken(ApiFactory.CreateTestToken([Roles.Staff]));

        Assert.Equal(HttpStatusCode.Forbidden, (await staff.Get($"{Admin}/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.Get(Admin)).StatusCode); // but staff can work with orders
        var settings = await (await factory.CreateAdminClient().Get($"{Admin}/settings")).Read<OrderSettingsDto>();
        Assert.Equal((2, 2, 48), (settings.MaxOpenOrdersPerCustomer, settings.MaxRefusedOrders, settings.PendingExpiryHours));
    }
}
