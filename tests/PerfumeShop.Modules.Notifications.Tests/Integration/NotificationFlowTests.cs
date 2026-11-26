using System.Net;
using Microsoft.Extensions.DependencyInjection;
using PerfumeShop.Modules.Notifications.Contracts;
using PerfumeShop.Modules.Notifications.Infrastructure;
using PerfumeShop.Modules.Orders.Contracts;
using PerfumeShop.Modules.Payments.Contracts;
using PerfumeShop.Modules.Shipping.Contracts;
using PerfumeShop.Shared.Infrastructure.Jobs;

namespace PerfumeShop.Modules.Notifications.Tests.Integration;

/// <summary>Events → queued messages → dispatcher, on a real database. Tests in this assembly run one after another.</summary>
public sealed class NotificationFlowTests(NotificationsApiFactory factory)
{
    private static DateTime Now => DateTime.UtcNow;

    [Fact]
    public async Task Order_placed_queues_a_customer_sms_and_staff_emails_then_sends_them()
    {
        var order = await factory.OrderAsync();
        await factory.DispatchOutboxAsync();

        var messages = await factory.MessagesAsync(order.Number);
        var sms = Assert.Single(messages, m => m.TemplateKey == "order-placed");
        Assert.Equal(("Sms", "Customer", order.Phone, "Queued", "GSM-7", 1), (sms.Channel, sms.Audience, sms.Recipient, sms.Status, sms.Encoding, sms.Segments));
        Assert.Equal($"PerfumeShop: Porosia {order.Number} u pranua. Totali 41.90 EUR, paguani ne dorezim. Do t'ju telefonojme per konfirmim.", sms.Body);
        var emails = messages.Where(m => m.TemplateKey == "staff-new-order").ToList();
        Assert.Equal(NotificationsApiFactory.StaffRecipients.Order(), emails.Select(e => e.Recipient).Order());
        Assert.Equal($"Porosi e re {order.Number} – 41.90 EUR", emails[0].Subject);
        Assert.Contains("1 × Lattafa Khamrah 100 ml – 39.90 EUR", emails[0].Body, StringComparison.Ordinal);
        Assert.Contains("Arbër Krasniqi", emails[0].Body, StringComparison.Ordinal);

        await factory.SendAsync();

        var sent = await factory.MessagesAsync(order.Number);
        Assert.All(sent, m => Assert.Equal("Sent", m.Status));
        var attempt = Assert.Single(sent.Single(m => m.TemplateKey == "order-placed").AttemptLog);
        Assert.Equal(("fake-sms", true, "{\"status\":\"accepted\"}"), (attempt.Provider, attempt.Succeeded, attempt.Response));
        Assert.Contains(factory.Sms.Sent, s => s.Phone == order.Phone && s.Message == sms.Body);
        Assert.Equal(2, factory.Email.Sent.Count(e => e.Subject == emails[0].Subject));
    }

    [Fact]
    public async Task Confirmation_shipment_and_delivery_sms()
    {
        var order = await factory.OrderAsync(confirm: true);
        await factory.DispatchOutboxAsync();
        await factory.PublishAsync(new ShipmentCreatedIntegrationEvent(Guid.NewGuid(), Now, Guid.NewGuid(), order.Id, order.Number, "Posta e Kosovës", "PK2026000777XK", 41.90m));
        Assert.DoesNotContain(await factory.MessagesAsync(order.Number), m => m.TemplateKey == "shipment-handed-over"); // booked, not picked up yet
        await factory.PublishAsync(new ShipmentHandedOverIntegrationEvent(Guid.NewGuid(), Now, Guid.NewGuid(), order.Id, order.Number, "Posta e Kosovës", "PK2026000777XK", 41.90m));
        await factory.PublishAsync(new ShipmentDeliveredIntegrationEvent(Guid.NewGuid(), Now, Guid.NewGuid(), order.Id, order.Number, "PK2026000777XK"));

        var sms = (await factory.MessagesAsync(order.Number)).Where(m => m.Channel == "Sms").ToDictionary(m => m.TemplateKey, m => m.Body);

        Assert.Equal(["order-confirmed", "order-placed", "shipment-delivered", "shipment-handed-over"], sms.Keys.Order());
        Assert.Equal($"PerfumeShop: Porosia {order.Number} u konfirmua. Do t'ju njoftojme kur te niset. Pyetje: +383 44 000 000", sms["order-confirmed"]);
        Assert.Equal($"PerfumeShop: Porosia {order.Number} u nis me Posta e Kosoves, nr. gjurmimit PK2026000777XK. Paguani 41.90 EUR ne dorezim.", sms["shipment-handed-over"]);
        Assert.StartsWith("PerfumeShop: Faleminderit!", sms["shipment-delivered"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancelled_and_expired_orders_get_an_sms_refused_parcels_do_not()
    {
        var cancelled = await factory.OrderAsync();
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateAdminClient().PostJson($"/api/admin/orders/{cancelled.Id}/cancel", new { reason = "Nuk përgjigjet" })).StatusCode);
        var expired = await factory.OrderAsync();
        var refused = await factory.OrderAsync();
        await factory.DispatchOutboxAsync();

        await factory.PublishAsync(new OrderExpiredIntegrationEvent(Guid.NewGuid(), Now, expired.Id, expired.Number, Guid.NewGuid()));
        await factory.PublishAsync(new OrderRefusedIntegrationEvent(Guid.NewGuid(), Now, refused.Id, refused.Number, Guid.NewGuid(), "Refuzoi"));
        await factory.PublishAsync(new ShipmentRefusedIntegrationEvent(Guid.NewGuid(), Now, Guid.NewGuid(), refused.Id, refused.Number, "PK1", "Refuzoi"));

        Assert.Contains(await factory.MessagesAsync(cancelled.Number), m => m.TemplateKey == "order-cancelled" && m.Body.Contains("u anulua", StringComparison.Ordinal));
        Assert.Contains(await factory.MessagesAsync(expired.Number), m => m.TemplateKey == "order-expired" && m.Body.Contains("nuk arritem t'ju kontaktojme", StringComparison.Ordinal));
        Assert.Equal(["order-placed"], (await factory.MessagesAsync(refused.Number)).Where(m => m.Channel == "Sms").Select(m => m.TemplateKey));
    }

    [Fact]
    public async Task Each_event_produces_each_message_at_most_once()
    {
        var order = await factory.OrderAsync();
        await factory.DispatchOutboxAsync();
        var confirmed = new OrderConfirmedIntegrationEvent(Guid.NewGuid(), Now, order.Id, order.Number, Guid.NewGuid());

        await factory.PublishAsync(confirmed);
        await factory.PublishAsync(confirmed);
        await factory.DispatchOutboxAsync();

        var messages = await factory.MessagesAsync(order.Number);
        Assert.Single(messages, m => m.TemplateKey == "order-confirmed");
        Assert.Single(messages, m => m.TemplateKey == "order-placed");
        Assert.Equal(2, messages.Count(m => m.TemplateKey == "staff-new-order"));
    }

    [Fact]
    public async Task Customer_sms_in_quiet_hours_waits_until_they_end_but_otp_codes_do_not()
    {
        factory.Quiet = NotificationsApiFactory.QuietNow();
        try
        {
            var order = await factory.OrderAsync();
            var otp = Assert.Single(factory.Sms.Sent, s => s.Phone == order.Phone); // OTP codes are sent at once, quiet hours or not
            Assert.Matches(@"\b\d{6}\b", otp.Message);
            await factory.DispatchOutboxAsync();
            await factory.SendAsync();

            var messages = await factory.MessagesAsync(order.Number);
            var sms = Assert.Single(messages, m => m.TemplateKey == "order-placed");
            Assert.Equal(("Queued", true), (sms.Status, sms.HeldForQuietHours));
            Assert.Equal(factory.Quiet.NextAllowedUtc(sms.CreatedAtUtc), sms.SendAfterUtc, TimeSpan.FromSeconds(1));
            Assert.True(sms.SendAfterUtc > Now.AddMinutes(100));
            Assert.All(messages.Where(m => m.Channel == "Email"), m => Assert.Equal(("Sent", false), (m.Status, m.HeldForQuietHours))); // staff alerts are not held
            Assert.DoesNotContain(factory.Sms.Sent, s => s.Phone == order.Phone && s.Message == sms.Body);

            // 08:00: quiet hours are over and the message is due.
            factory.Quiet = NotificationsApiFactory.NeverQuiet;
            await factory.MakeDueAsync(sms.Id);
            await factory.SendAsync();

            Assert.Equal("Sent", (await factory.MessagesAsync(order.Number)).Single(m => m.Id == sms.Id).Status);
            Assert.Contains(factory.Sms.Sent, s => s.Phone == order.Phone && s.Message == sms.Body);
        }
        finally
        {
            factory.Quiet = NotificationsApiFactory.NeverQuiet;
        }
    }

    [Fact]
    public async Task A_due_sms_is_held_when_quiet_hours_start()
    {
        var order = await factory.OrderAsync();
        await factory.DispatchOutboxAsync(); // queued in daytime
        factory.Quiet = NotificationsApiFactory.QuietNow();
        try
        {
            await factory.SendAsync();

            var sms = (await factory.MessagesAsync(order.Number, "order-placed")).Single();
            Assert.Equal(("Queued", 0), (sms.Status, sms.Attempts));
            Assert.Equal(factory.Quiet.NextAllowedUtc(Now), sms.NextAttemptAtUtc, TimeSpan.FromMinutes(1));
        }
        finally
        {
            factory.Quiet = NotificationsApiFactory.NeverQuiet;
        }
    }

    [Fact]
    public async Task Failed_sends_are_retried_with_backoff_then_failed_and_resent_by_staff()
    {
        var order = await factory.OrderAsync();
        await factory.DispatchOutboxAsync();
        var id = (await factory.MessagesAsync(order.Number, "order-placed")).Single().Id;
        var admin = factory.CreateAdminClient();

        factory.Sms.Fail = true;
        try
        {
            await factory.SendAsync();
            var first = (await factory.MessagesAsync(order.Number, "order-placed")).Single();
            Assert.Equal(("Queued", 1, "Gateway timeout"), (first.Status, first.Attempts, first.LastError));
            Assert.Equal(first.AttemptLog[0].AttemptedAtUtc + TimeSpan.FromMinutes(1), first.NextAttemptAtUtc, TimeSpan.FromSeconds(1));
            Assert.Equal("{\"status\":504}", first.AttemptLog[0].Response);

            for (var i = 1; i < 5; i++)
            {
                await factory.MakeDueAsync(id);
                await factory.SendAsync();
            }

            var failed = (await factory.MessagesAsync(order.Number, "order-placed")).Single();
            Assert.Equal(("Failed", 5), (failed.Status, failed.AttemptLog.Count));
            var listed = await (await admin.Get($"/api/admin/notifications/messages?status=Failed&orderNumber={order.Number}")).Read<PageBody<MessageBody>>();
            Assert.Equal(id, Assert.Single(listed.Items).Id);
        }
        finally
        {
            factory.Sms.Fail = false;
        }

        var resend = await admin.PostJson($"/api/admin/notifications/messages/{id}/resend");
        Assert.Equal(HttpStatusCode.OK, resend.StatusCode);
        var queued = await resend.Read<MessageBody>();
        Assert.Equal(("Queued", 0), (queued.Status, queued.Attempts));
        await factory.SendAsync();

        var sent = (await factory.MessagesAsync(order.Number, "order-placed")).Single();
        Assert.Equal(("Sent", 6), (sent.Status, sent.AttemptLog.Count));
        Assert.NotNull(sent.ResentBy);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostJson($"/api/admin/notifications/messages/{id}/resend")).StatusCode);
    }

    [Fact]
    public async Task Staff_alerts_for_failed_receipts_job_failures_low_stock_and_unpaid_deliveries()
    {
        var receiptEvent = new FiscalReceiptFailedIntegrationEvent(Guid.NewGuid(), Now, Guid.NewGuid(), "PS-R-2026-000123", "Sale", Guid.NewGuid(), "PS-2026-123456", "Fiscal service down", 1);
        await factory.PublishAsync(receiptEvent);
        await factory.PublishAsync(receiptEvent);

        var receipt = await factory.MessagesAsync("PS-2026-123456", "staff-receipt-failed");
        Assert.Equal(2, receipt.Count); // once per recipient
        Assert.Equal("Kuponi fiskal dështoi: PS-2026-123456", receipt[0].Subject);
        Assert.Contains("PS-R-2026-000123 (Sale)", receipt[0].Body, StringComparison.Ordinal);
        Assert.Contains($"/api/admin/payments/receipts/{receiptEvent.ReceiptId}/retry", receipt[0].Body, StringComparison.Ordinal);

        var jobId = Guid.NewGuid().ToString("N")[..8];
        await using (var scope = factory.CreateScope())
        {
            var listener = scope.ServiceProvider.GetRequiredService<IJobFailureListener>();
            var failure = new JobFailure(jobId, "OutboxProcessor`1.RunAsync", "orders-outbox", "NpgsqlException: Connection refused", Now);
            await listener.OnJobFailedAsync(failure, TestContext.Current.CancellationToken);
            await listener.OnJobFailedAsync(failure, TestContext.Current.CancellationToken);
        }

        var job = (await factory.MessagesAsync(templateKey: "staff-job-failed")).Where(m => m.SourceId == $"job-{jobId}").ToList();
        Assert.Equal(2, job.Count);
        Assert.Contains("orders-outbox", job[0].Body, StringComparison.Ordinal);

        var low = await factory.OrderAsync(stock: 2); // 1 left ≤ threshold 3
        var sku = factory.Catalog.Get(low.VariantId).Sku;
        await using (var scope = factory.CreateScope())
        {
            var lowStock = scope.ServiceProvider.GetRequiredService<LowStockAlertJob>();
            Assert.Equal(2, await lowStock.QueueAsync(TestContext.Current.CancellationToken));
            Assert.Equal(0, await lowStock.QueueAsync(TestContext.Current.CancellationToken)); // once a day
        }

        var lowMail = (await factory.MessagesAsync(templateKey: "staff-low-stock")).Last();
        Assert.Contains($"{sku} Lattafa Khamrah 100 ml: 1 (kufiri 3)", lowMail.Body, StringComparison.Ordinal);

        factory.Shipping.Overdue = [new OverdueCodDto(Guid.NewGuid(), "PS-2026-000042", "Posta e Kosovës", "PK2026000001XK", 41.90m, Now.AddDays(-11), 11, 7)];
        try
        {
            await using var scope = factory.CreateScope();
            var unpaid = scope.ServiceProvider.GetRequiredService<UnpaidDeliveriesAlertJob>();
            Assert.Equal(2, await unpaid.QueueAsync(TestContext.Current.CancellationToken));
            Assert.Equal(0, await unpaid.QueueAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            factory.Shipping.Overdue = [];
        }

        var unpaidMail = (await factory.MessagesAsync(templateKey: "staff-unpaid-deliveries")).Last();
        Assert.Equal("Pako të dorëzuara pa pagesë: 1 (41.90 EUR)", unpaidMail.Subject);
        Assert.Contains("PS-2026-000042 PK2026000001XK 41.90 EUR", unpaidMail.Body, StringComparison.Ordinal);
        Assert.Contains("(11 ditë, afati 7)", unpaidMail.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Switched_off_alerts_and_disabled_templates_queue_nothing()
    {
        var admin = factory.CreateAdminClient();
        await admin.PutJson("/api/admin/notifications/settings/staff-alerts", new { recipients = NotificationsApiFactory.StaffRecipients, newOrder = false });
        var template = await (await admin.Get("/api/admin/notifications/templates/order-placed")).Read<TemplateBody>();
        await admin.PutJson("/api/admin/notifications/templates/order-placed", new { body = template.Body, transliterate = true, isEnabled = false });
        try
        {
            var order = await factory.OrderAsync();
            await factory.DispatchOutboxAsync();

            Assert.Empty(await factory.MessagesAsync(order.Number));
        }
        finally
        {
            await admin.PutJson("/api/admin/notifications/settings/staff-alerts", new { recipients = NotificationsApiFactory.StaffRecipients });
            await admin.PutJson("/api/admin/notifications/templates/order-placed", new { body = template.Body, transliterate = true, isEnabled = true });
        }
    }

    [Fact]
    public async Task Notifications_api_lists_the_messages_of_an_order()
    {
        var order = await factory.OrderAsync();
        await factory.DispatchOutboxAsync();
        await using var scope = factory.CreateScope();

        var messages = await scope.ServiceProvider.GetRequiredService<INotificationsApi>().GetForOrderAsync(order.Number.ToLowerInvariant(), TestContext.Current.CancellationToken);

        Assert.Equal(3, messages.Count);
        Assert.Contains(messages, m => m is { Channel: "Sms", TemplateKey: "order-placed" });
    }
}

internal sealed record PlaceholderBody(string Name, string Sample);

internal sealed record SmsInfoBody(string Encoding, int Length, int Segments, int PerSegment, string NonGsmCharacters, string? Warning);

internal sealed record TemplateBody(
    string Key, string Channel, string Audience, string? Alert, string Description, string? Subject, string Body, bool Transliterate, bool IsEnabled,
    IReadOnlyList<PlaceholderBody> Placeholders, SmsInfoBody? Sms, SmsInfoBody? SmsWithoutTransliteration);

internal sealed record PreviewBody(string Key, string Channel, string? Subject, string Text, IReadOnlyList<string> UsedPlaceholders, SmsInfoBody? Sms);
