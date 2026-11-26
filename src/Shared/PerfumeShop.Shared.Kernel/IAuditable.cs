namespace PerfumeShop.Shared.Kernel;

/// <summary>Entity whose timestamps are set automatically when it is saved.</summary>
public interface IAuditable
{
    DateTime CreatedAtUtc { get; }

    DateTime UpdatedAtUtc { get; }
}
