using System.Net;
using Microsoft.Extensions.DependencyInjection;
using PerfumeShop.Modules.Notifications.Contracts;
using PerfumeShop.Testing;

namespace PerfumeShop.Modules.Notifications.Tests.Integration;

public sealed class TemplateAndSettingsApiTests(NotificationsApiFactory factory)
{
    private const string Api = "/api/admin/notifications";

    [Fact]
    public async Task Templates_show_placeholders_and_sms_segments()
    {
        var templates = await (await factory.CreateAdminClient().Get($"{Api}/templates")).Read<IReadOnlyList<TemplateBody>>();

        Assert.Equal(12, templates.Count);
        Assert.Equal(6, templates.Count(t => t is { Channel: "Sms", Audience: "Customer" }));
        var placed = templates.Single(t => t.Key == "order-placed");
        Assert.Contains(placed.Placeholders, p => p is { Name: "orderNumber", Sample: "PS-2026-000042" });
        Assert.Equal(("GSM-7", 1, (string?)null), (placed.Sms!.Encoding, placed.Sms.Segments, placed.Sms.Warning));
        Assert.Equal(("UCS-2", 2), (placed.SmsWithoutTransliteration!.Encoding, placed.SmsWithoutTransliteration.Segments));
        Assert.NotNull(placed.SmsWithoutTransliteration.Warning);
        Assert.All(templates.Where(t => t.Channel == "Email"), t => Assert.Null(t.Sms));
    }

    [Fact]
    public async Task Admin_edits_a_template_and_unknown_placeholders_are_rejected()
    {
        var admin = factory.CreateAdminClient();
        var original = await (await admin.Get($"{Api}/templates/order-confirmed")).Read<TemplateBody>();

        var bad = await admin.PutJson($"{Api}/templates/order-confirmed", new { body = "Porosia {{orderNumber}} – {{iban}}", transliterate = true, isEnabled = true });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal("Notifications.Template.UnknownPlaceholder", await bad.ErrorCode());
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutJson($"{Api}/templates/nope", new { body = "x" })).StatusCode);

        try
        {
            var ok = await admin.PutJson($"{Api}/templates/order-confirmed", new { body = "{{shopName}}: {{orderNumber}} është konfirmuar.", transliterate = false, isEnabled = true });
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            var updated = await ok.Read<TemplateBody>();
            Assert.Equal(("UCS-2", false), (updated.Sms!.Encoding, updated.Transliterate));

            var order = await factory.OrderAsync(confirm: true);
            await factory.DispatchOutboxAsync();
            Assert.Equal($"PerfumeShop: {order.Number} është konfirmuar.", (await factory.MessagesAsync(order.Number, "order-confirmed")).Single().Body);
        }
        finally
        {
            await admin.PutJson($"{Api}/templates/order-confirmed", new { body = original.Body, transliterate = true, isEnabled = true });
        }
    }

    [Fact]
    public async Task Preview_renders_a_draft_with_sample_or_given_values()
    {
        var admin = factory.CreateAdminClient();

        var saved = await (await admin.PostJson($"{Api}/templates/shipment-handed-over/preview")).Read<PreviewBody>();
        var draft = await (await admin.PostJson($"{Api}/templates/shipment-handed-over/preview", new
        {
            body = "Pako {{trackingNumber}} me {{courier}}: {{codAmount}} € në dorëzim. " + new string('x', 120),
            transliterate = false,
            values = new Dictionary<string, string> { ["courier"] = "Albanian Courier" },
        })).Read<PreviewBody>();
        var email = await (await admin.PostJson($"{Api}/templates/staff-new-order/preview")).Read<PreviewBody>();
        var unknown = await admin.PostJson($"{Api}/templates/shipment-handed-over/preview", new { body = "{{tracking}}" });
        var unknownValue = await admin.PostJson($"{Api}/templates/shipment-handed-over/preview", new { values = new Dictionary<string, string> { ["iban"] = "x" } });

        Assert.Equal("PerfumeShop: Porosia PS-2026-000042 u nis me Posta e Kosoves, nr. gjurmimit PK2026000001XK. Paguani 41.90 EUR ne dorezim.", saved.Text);
        Assert.Equal(1, saved.Sms!.Segments);
        Assert.StartsWith("Pako PK2026000001XK me Albanian Courier: 41.90 € në dorëzim.", draft.Text, StringComparison.Ordinal);
        Assert.Equal(("UCS-2", 3), (draft.Sms!.Encoding, draft.Sms.Segments));
        Assert.Contains("ë", draft.Sms.Warning, StringComparison.Ordinal);
        Assert.Equal(["trackingNumber", "courier", "codAmount"], draft.UsedPlaceholders);
        Assert.Equal("Porosi e re PS-2026-000042 – 41.90 EUR", email.Subject);
        Assert.Null(email.Sms);
        Assert.Equal("Notifications.Template.UnknownPlaceholder", await unknown.ErrorCode());
        Assert.Equal("Notifications.Template.UnknownPlaceholder", await unknownValue.ErrorCode());
    }

    [Fact]
    public async Task Staff_alert_settings_validate_recipients()
    {
        var admin = factory.CreateAdminClient();

        var bad = await admin.PutJson($"{Api}/settings/staff-alerts", new { recipients = new[] { "nope" } });
        var settings = await (await admin.Get($"{Api}/settings/staff-alerts")).Read<SettingsBody>();

        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(NotificationsApiFactory.StaffRecipients, settings.Recipients);
        Assert.True(settings is { NewOrder: true, FailedReceipt: true, LowStock: true, UnpaidDeliveries: true, JobFailures: true });
    }

    [Fact]
    public async Task Staff_can_read_only_admin_can_change_customers_cannot_see_anything()
    {
        var staff = factory.CreateClientWithToken(ApiFactory.CreateTestToken(["Staff"]));
        var customer = factory.CreateClientWithToken(ApiFactory.CreateTestToken(["Customer"], customerId: Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.OK, (await staff.Get($"{Api}/templates")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.Get($"{Api}/messages")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.PostJson($"{Api}/templates/order-placed/preview")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutJson($"{Api}/templates/order-placed", new { body = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutJson($"{Api}/settings/staff-alerts", new { recipients = Array.Empty<string>() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.Get($"{Api}/messages")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().Get($"{Api}/templates")).StatusCode);
    }

    [Fact]
    public async Task Message_log_filters_validate()
    {
        var admin = factory.CreateAdminClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Get($"{Api}/messages?status=Delivered")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Get($"{Api}/messages?channel=Fax")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.Get($"{Api}/messages?channel=Email&status=Sent&from=2026-01-01&to=2030-01-01")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Get($"{Api}/messages/{Guid.NewGuid()}")).StatusCode);
    }

    private sealed record SettingsBody(IReadOnlyList<string> Recipients, bool NewOrder, bool FailedReceipt, bool LowStock, bool UnpaidDeliveries, bool JobFailures);
}

/// <summary>Console senders print OTP codes and emails: they must never exist outside Development.</summary>
public sealed class SenderGuardTests(NotificationsApiFactory shared)
{
    private sealed class HostIn(string connectionString, string environment) : ApiFactory(connectionString)
    {
        protected override string EnvironmentName => environment;
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public async Task Console_senders_exist_only_in_development(string environment)
    {
        await using var host = new HostIn(shared.ConnectionString, environment);

        Assert.Equal("UnconfiguredEmailSender", host.Services.GetRequiredService<IEmailSender>().GetType().Name);
        Assert.Equal("UnconfiguredSmsSender", host.Services.GetRequiredService<ISmsSender>().GetType().Name);
        var result = await host.Services.GetRequiredService<IEmailSender>().SendAsync(new EmailMessage("a@example.com", "s", "b"), TestContext.Current.CancellationToken);
        Assert.Equal((false, "No email provider configured."), (result.Succeeded, result.Error));
    }

    [Fact]
    public async Task Development_uses_the_console_senders()
    {
        await using var host = new HostIn(shared.ConnectionString, "Development");

        Assert.Equal("ConsoleEmailSender", host.Services.GetRequiredService<IEmailSender>().GetType().Name);
        Assert.Equal("ConsoleSmsSender", host.Services.GetRequiredService<ISmsSender>().GetType().Name);
    }
}
