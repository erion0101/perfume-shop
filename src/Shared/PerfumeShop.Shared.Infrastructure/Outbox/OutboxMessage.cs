namespace PerfumeShop.Shared.Infrastructure.Outbox;

/// <summary>
/// Row in a module's outbox table (&lt;schema&gt;.outbox_messages), saved in the same transaction as the
/// business change and dispatched later by <see cref="OutboxProcessor{TContext}"/>.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; init; }

    /// <summary>"Namespace.TypeName, AssemblyName" of the integration event.</summary>
    public required string Type { get; init; }

    /// <summary>The event as JSON.</summary>
    public required string Content { get; init; }

    public DateTime OccurredOnUtc { get; init; }

    public DateTime? ProcessedOnUtc { get; set; }

    public int Attempts { get; set; }

    public DateTime? LastAttemptAtUtc { get; set; }

    public string? Error { get; set; }
}
