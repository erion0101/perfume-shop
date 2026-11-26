using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Inventory.Domain;
using PerfumeShop.Shared.Infrastructure.Persistence;

namespace PerfumeShop.Modules.Inventory.Infrastructure;

internal sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : ModuleDbContext(options)
{
    internal const string SchemaName = "inventory";

    public DbSet<StockItem> StockItems => Set<StockItem>();

    public IQueryable<StockBatch> Batches => Set<StockBatch>();

    public DbSet<StockReservation> Reservations => Set<StockReservation>();

    public IQueryable<StockAllocation> Allocations => Set<StockAllocation>();

    public DbSet<StockMovement> Movements => Set<StockMovement>();

    protected override string Schema => SchemaName;

    /// <summary>Writes the ledger: every movement a <see cref="StockItem"/> recorded is inserted in the same save.</summary>
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        foreach (var item in ChangeTracker.Entries<StockItem>().Select(e => e.Entity).ToList())
        {
            Movements.AddRange(item.PendingMovements);
            item.ClearPendingMovements();
        }

        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        throw new NotSupportedException("Use SaveChangesAsync so the stock ledger is written.");
}
