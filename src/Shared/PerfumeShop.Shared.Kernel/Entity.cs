namespace PerfumeShop.Shared.Kernel;

/// <summary>Base class for entities identified by an id.</summary>
public abstract class Entity<TId>
    where TId : notnull
{
    protected Entity(TId id) => Id = id;

    /// <summary>Required by EF Core.</summary>
    protected Entity() => Id = default!;

    public TId Id { get; protected init; }
}
