using Microsoft.EntityFrameworkCore;

namespace PerfumeShop.Shared.Infrastructure.Outbox;

/// <summary>
/// "This consumer already handled this event" (&lt;schema&gt;.inbox_messages). Outbox delivery is at-least-once,
/// so a handler adds this row in the same SaveChanges as its own changes; a repeat is then skipped.
/// </summary>
public sealed class InboxMessage
{
    public Guid EventId { get; init; }

    public required string Consumer { get; init; }

    public DateTime ProcessedOnUtc { get; init; }
}

public static class InboxExtensions
{
    public static ModelBuilder ApplyInbox(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InboxMessage>(inbox =>
        {
            inbox.ToTable("inbox_messages");
            inbox.HasKey(m => new { m.EventId, m.Consumer });
            inbox.Property(m => m.Consumer).HasMaxLength(100);
        });

        return modelBuilder;
    }

    public static Task<bool> HasProcessedAsync(this DbContext db, Guid eventId, string consumer, CancellationToken cancellationToken) =>
        db.Set<InboxMessage>().AnyAsync(m => m.EventId == eventId && m.Consumer == consumer, cancellationToken);

    public static void MarkProcessed(this DbContext db, Guid eventId, string consumer, DateTime nowUtc) =>
        db.Set<InboxMessage>().Add(new InboxMessage { EventId = eventId, Consumer = consumer, ProcessedOnUtc = nowUtc });
}
