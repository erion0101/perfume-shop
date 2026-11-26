using Microsoft.EntityFrameworkCore;

namespace PerfumeShop.Shared.Infrastructure.Persistence;

/// <summary>Base DbContext for a module: every module owns exactly one PostgreSQL schema.</summary>
public abstract class ModuleDbContext(DbContextOptions options) : DbContext(options)
{
    protected abstract string Schema { get; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
