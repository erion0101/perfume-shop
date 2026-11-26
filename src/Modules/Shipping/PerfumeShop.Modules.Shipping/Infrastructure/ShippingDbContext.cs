using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Shipping.Contracts;
using PerfumeShop.Modules.Shipping.Domain;
using PerfumeShop.Shared.Infrastructure.Outbox;
using PerfumeShop.Shared.Infrastructure.Persistence;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Shipping.Infrastructure;

internal sealed class ShippingDbContext(DbContextOptions<ShippingDbContext> options) : ModuleDbContext(options)
{
    internal const string SchemaName = "shipping";

    public DbSet<Courier> Couriers => Set<Courier>();

    public DbSet<Shipment> Shipments => Set<Shipment>();

    public DbSet<Settlement> Settlements => Set<Settlement>();

    public DbSet<StatusImport> StatusImports => Set<StatusImport>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyOutbox();
    }

    /// <summary>Shipment and settlement domain events become integration events in the outbox (same transaction).</summary>
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var roots = ChangeTracker.Entries<AggregateRoot<Guid>>().Select(e => e.Entity).ToList();
        foreach (var root in roots)
        {
            foreach (var domainEvent in root.DomainEvents)
            {
                if (ToIntegrationEvent(domainEvent) is { } integrationEvent)
                {
                    this.AddIntegrationEvent(integrationEvent);
                }
            }

            root.ClearDomainEvents();
        }

        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        throw new NotSupportedException("Use SaveChangesAsync so integration events reach the outbox.");

    private static IIntegrationEvent? ToIntegrationEvent(IDomainEvent domainEvent)
    {
        var id = Guid.CreateVersion7();
        return domainEvent switch
        {
            ShipmentStatusChangedDomainEvent { To: ShipmentStatus.Created } e =>
                new ShipmentCreatedIntegrationEvent(id, e.AtUtc, e.Shipment.Id, e.Shipment.OrderId, e.Shipment.OrderNumber, e.Shipment.CourierName, e.Shipment.TrackingNumber, e.Shipment.CodAmount),
            ShipmentStatusChangedDomainEvent { To: ShipmentStatus.HandedOver } e =>
                new ShipmentHandedOverIntegrationEvent(id, e.AtUtc, e.Shipment.Id, e.Shipment.OrderId, e.Shipment.OrderNumber, e.Shipment.CourierName, e.Shipment.TrackingNumber, e.Shipment.CodAmount),
            ShipmentStatusChangedDomainEvent { To: ShipmentStatus.Delivered } e =>
                new ShipmentDeliveredIntegrationEvent(id, e.AtUtc, e.Shipment.Id, e.Shipment.OrderId, e.Shipment.OrderNumber, e.Shipment.TrackingNumber),
            ShipmentStatusChangedDomainEvent { To: ShipmentStatus.Refused } e =>
                new ShipmentRefusedIntegrationEvent(id, e.AtUtc, e.Shipment.Id, e.Shipment.OrderId, e.Shipment.OrderNumber, e.Shipment.TrackingNumber, e.Note),
            CodSettledDomainEvent e =>
                new CodSettledIntegrationEvent(id, e.Settlement.ImportedAtUtc, e.Settlement.Id, e.Settlement.CourierId, e.Settlement.CourierName, e.Settlement.Reference,
                    e.Settlement.MatchedCount, e.Settlement.MatchedAmount, e.Settlement.TotalFees, e.Settlement.NetPaid,
                    e.Settlement.Lines.Count(l => l.Status != SettlementLineStatus.Matched),
                    [.. e.Settlement.Lines.Where(l => l.Status == SettlementLineStatus.Matched)
                        .Select(l => new CodSettledLine(l.ShipmentId!.Value, l.OrderId!.Value, l.OrderNumber!, l.TrackingNumber, l.CollectedAmount, l.Fee))]),
            _ => null,
        };
    }
}

/// <summary>A courier status file already processed (by content hash), with the per-line results.</summary>
internal sealed class StatusImport
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid CourierId { get; init; }

    public required string FileHash { get; init; }

    public required string ImportedBy { get; init; }

    public DateTime ImportedAtUtc { get; init; }

    public List<StatusImportLineResult> Results { get; init; } = [];
}

/// <param name="Outcome">Applied, AlreadyApplied, UnknownTracking, InvalidStatus, InvalidTransition, OrderUpdateFailed.</param>
internal sealed record StatusImportLineResult(int LineNumber, string TrackingNumber, string Status, string Outcome, string? Note);
