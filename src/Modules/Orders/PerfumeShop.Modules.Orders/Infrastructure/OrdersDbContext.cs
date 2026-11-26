using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Orders.Domain;
using PerfumeShop.Shared.Infrastructure.Outbox;
using PerfumeShop.Shared.Infrastructure.Persistence;

namespace PerfumeShop.Modules.Orders.Infrastructure;

internal sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : ModuleDbContext(options)
{
    internal const string SchemaName = "orders";

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<DeliveryRate> DeliveryRates => Set<DeliveryRate>();

    public DbSet<OrderSettings> Settings => Set<OrderSettings>();

    public DbSet<OrderNumberCounter> OrderNumberCounters => Set<OrderNumberCounter>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyOutbox();
    }

    /// <summary>Every order status change becomes an integration event in the outbox, in the same transaction.</summary>
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        foreach (var order in ChangeTracker.Entries<Order>().Select(e => e.Entity).ToList())
        {
            foreach (var change in order.DomainEvents.OfType<OrderStatusChangedDomainEvent>())
            {
                this.AddIntegrationEvent(OrderEventMapper.ToIntegrationEvent(change));
            }

            order.ClearDomainEvents();
        }

        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        throw new NotSupportedException("Use SaveChangesAsync so integration events reach the outbox.");
}

/// <summary>Last order number used per year: PS-{year}-{number:000000}.</summary>
internal sealed class OrderNumberCounter
{
    public int Year { get; set; }

    public int LastNumber { get; set; }
}
