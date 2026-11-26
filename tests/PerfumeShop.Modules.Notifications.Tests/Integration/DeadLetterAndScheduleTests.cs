using Microsoft.Extensions.DependencyInjection;
using PerfumeShop.Shared.Infrastructure.Outbox;

namespace PerfumeShop.Modules.Notifications.Tests.Integration;

public sealed class DeadLetterAndScheduleTests(NotificationsApiFactory factory)
{
    [Fact]
    public async Task An_event_that_fails_permanently_alerts_staff_once()
    {
        factory.Throwing.Throw = true;
        try
        {
            await factory.OrderAsync(confirm: true);
            for (var run = 0; run < OutboxProcessor<Microsoft.EntityFrameworkCore.DbContext>.MaxAttempts + 2; run++)
            {
                await using var scope = factory.CreateScope();
                foreach (var processor in scope.ServiceProvider.GetServices<IOutboxProcessor>())
                {
                    await processor.ProcessAsync(TestContext.Current.CancellationToken);
                }
            }
        }
        finally
        {
            factory.Throwing.Throw = false;
        }

        var alerts = (await factory.MessagesAsync(templateKey: "staff-dead-letter"))
            .Where(m => m.Subject == "Ngjarja nuk u dorëzua: OrderConfirmedIntegrationEvent (orders)")
            .ToList();
        Assert.Equal(NotificationsApiFactory.StaffRecipients.Order(), alerts.Select(a => a.Recipient).Order()); // once per recipient, not per run
        Assert.Contains("dështoi 10 herë", alerts[0].Body, StringComparison.Ordinal);
        Assert.Contains("Test handler is down", alerts[0].Body, StringComparison.Ordinal);
        Assert.Single(alerts.Select(a => a.SourceId).Distinct());
    }

    [Fact]
    public async Task Repeated_dead_letter_reports_queue_one_alert()
    {
        var deadLetter = new DeadLetter("payments", Guid.NewGuid(), "FiscalReceiptFailedIntegrationEvent", 10, "boom", DateTime.UtcNow, DateTime.UtcNow);
        await using (var scope = factory.CreateScope())
        {
            var listener = scope.ServiceProvider.GetRequiredService<IDeadLetterListener>();
            await listener.OnDeadLetterAsync(deadLetter, TestContext.Current.CancellationToken);
            await listener.OnDeadLetterAsync(deadLetter, TestContext.Current.CancellationToken);
        }

        Assert.Equal(2, (await factory.MessagesAsync(templateKey: "staff-dead-letter")).Count(m => m.SourceId == $"dead-letter-{deadLetter.MessageId}"));
    }

    [Fact]
    public void Daily_alerts_run_at_08_00_kosovo_time_all_year()
    {
        var lowStock = factory.Jobs.Registered[NotificationsModule.LowStockJobId];
        var unpaid = factory.Jobs.Registered[NotificationsModule.UnpaidCodJobId];

        Assert.Equal(("0 0 8 * * *", "Europe/Belgrade"), (lowStock.Cron, lowStock.TimeZone?.Id));
        Assert.Equal(("0 5 8 * * *", "Europe/Belgrade"), (unpaid.Cron, unpaid.TimeZone?.Id));
        Assert.Null(factory.Jobs.Registered[NotificationsModule.DispatchJobId].TimeZone);
    }
}
