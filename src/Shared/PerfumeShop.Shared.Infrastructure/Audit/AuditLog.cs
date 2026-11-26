using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PerfumeShop.Shared.Infrastructure.Audit;

/// <summary>
/// One back-office action: who (taken from the signed-in user), what, on which entity, and what changed.
/// Never put passwords, codes or other secrets into <see cref="Changes"/>.
/// </summary>
/// <param name="Action">Stable name, e.g. "Catalog.Variant.PriceChanged" (filterable).</param>
/// <param name="Summary">Short human text shown in the admin ("Çmimi 39.90 → 42.90 EUR").</param>
/// <param name="Changes">Before/after values or the relevant inputs.</param>
public sealed record AuditEntry(string Action, string EntityType, string? EntityId, string Summary, IReadOnlyDictionary<string, object?>? Changes = null);

/// <summary>
/// Records audit entries (implemented by the Administration module). Call it after the change was saved;
/// it never throws (a failed audit write is logged as an error and does not undo the business change).
/// </summary>
public interface IAuditLog
{
    Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}

/// <summary>Used when no module provides an audit store (e.g. narrow unit tests).</summary>
internal sealed class NullAuditLog : IAuditLog
{
    public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public static class AuditRegistration
{
    public static IServiceCollection AddNullAuditLogFallback(this IServiceCollection services)
    {
        services.TryAddScoped<IAuditLog, NullAuditLog>();
        return services;
    }

    /// <summary>"39.90 → 42.90" style before/after pair for <see cref="AuditEntry.Changes"/>.</summary>
    public static IReadOnlyDictionary<string, object?> Change(string field, object? before, object? after) =>
        new Dictionary<string, object?> { [field] = new { before, after } };
}
