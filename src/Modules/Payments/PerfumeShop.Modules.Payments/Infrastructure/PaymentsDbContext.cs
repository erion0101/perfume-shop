using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Payments.Domain;
using PerfumeShop.Shared.Infrastructure.Outbox;
using PerfumeShop.Shared.Infrastructure.Persistence;

namespace PerfumeShop.Modules.Payments.Infrastructure;

internal sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : ModuleDbContext(options)
{
    internal const string SchemaName = "payments";

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<FiscalReceipt> Receipts => Set<FiscalReceipt>();

    public DbSet<ReceiptNumberCounter> ReceiptNumberCounters => Set<ReceiptNumberCounter>();

    public DbSet<InboxMessage> Inbox => Set<InboxMessage>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyInbox();
        modelBuilder.ApplyOutbox();
    }
}

internal sealed class ReceiptNumberCounter
{
    public int Year { get; set; }

    public int LastNumber { get; set; }
}
