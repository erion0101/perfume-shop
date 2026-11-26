namespace PerfumeShop.Shared.Infrastructure.Outbox;

/// <summary>
/// An integration event that could not be delivered after <see cref="OutboxProcessor{TContext}.MaxAttempts"/> tries
/// (an event handler — the inbox side — kept throwing). It stays in the outbox table with its error.
/// </summary>
/// <param name="Schema">Module schema of the outbox, e.g. "orders".</param>
public sealed record DeadLetter(string Schema, Guid MessageId, string EventType, int Attempts, string Error, DateTime OccurredOnUtc, DateTime FailedAtUtc);

/// <summary>Told once per message when it fails for the last time. Notifications alerts staff.</summary>
public interface IDeadLetterListener
{
    Task OnDeadLetterAsync(DeadLetter deadLetter, CancellationToken cancellationToken);
}
