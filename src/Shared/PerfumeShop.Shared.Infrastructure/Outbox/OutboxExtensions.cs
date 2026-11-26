using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Shared.Infrastructure.Outbox;

public static class OutboxExtensions
{
    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Maps &lt;schema&gt;.outbox_messages in a module DbContext.</summary>
    public static ModelBuilder ApplyOutbox(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(outbox =>
        {
            outbox.ToTable("outbox_messages");
            outbox.HasKey(m => m.Id);
            outbox.Property(m => m.Id).ValueGeneratedNever();
            outbox.Property(m => m.Type).HasMaxLength(300).IsRequired();
            outbox.Property(m => m.Content).HasColumnType("jsonb").IsRequired();
            outbox.Property(m => m.Error).HasMaxLength(2000);
            outbox.HasIndex(m => m.OccurredOnUtc)
                .HasFilter("processed_on_utc IS NULL")
                .HasDatabaseName("ix_outbox_messages_unprocessed");
        });

        return modelBuilder;
    }

    /// <summary>Queues an integration event; it is written by the next SaveChanges (same transaction).</summary>
    public static void AddIntegrationEvent(this DbContext db, IIntegrationEvent integrationEvent)
    {
        var type = integrationEvent.GetType();
        db.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Id = integrationEvent.Id,
            Type = $"{type.FullName}, {type.Assembly.GetName().Name}",
            Content = JsonSerializer.Serialize(integrationEvent, type, SerializerOptions),
            OccurredOnUtc = integrationEvent.OccurredOnUtc,
        });
    }
}
