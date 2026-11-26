using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PerfumeShop.Shared.Infrastructure.Events;
using PerfumeShop.Shared.Infrastructure.Jobs;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Shared.Infrastructure.Outbox;

/// <summary>
/// Publishes unprocessed outbox messages of one module. Rows are locked with FOR UPDATE SKIP LOCKED, so
/// several workers never deliver the same message at once. A failing message is retried on the next run
/// and given up after <see cref="MaxAttempts"/> (it stays in the table with its error for inspection).
/// </summary>
public sealed class OutboxProcessor<TContext>(
    TContext db, IEventBus bus, TimeProvider timeProvider, IEnumerable<IDeadLetterListener> deadLetterListeners, ILogger<OutboxProcessor<TContext>> logger)
    : IOutboxProcessor
    where TContext : DbContext
{
    public const int MaxAttempts = 10;
    public const int BatchSize = 50;

    public async Task RunAsync(CancellationToken cancellationToken) => await ProcessAsync(cancellationToken);

    /// <summary>
    /// The schema is an identifier (cannot be a SQL parameter); it comes from the EF model and is
    /// checked to be a plain lowercase name. The numbers are constants.
    /// </summary>
    private static string LockBatchSql(string schema)
    {
        if (!schema.All(ch => char.IsAsciiLetterLower(ch) || char.IsAsciiDigit(ch) || ch == '_'))
        {
            throw new InvalidOperationException($"Unexpected schema name '{schema}'.");
        }

        return "SELECT * FROM \"" + schema + "\".outbox_messages "
            + "WHERE processed_on_utc IS NULL AND attempts < " + MaxAttempts + " "
            + "ORDER BY occurred_on_utc LIMIT " + BatchSize + " FOR UPDATE SKIP LOCKED";
    }

    /// <returns>Number of messages delivered.</returns>
    public async Task<int> ProcessAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var messages = await db.Set<OutboxMessage>()
            .FromSqlRaw(LockBatchSql(db.Model.GetDefaultSchema() ?? "public"))
            .ToListAsync(cancellationToken);

        var delivered = 0;
        var schema = db.Model.GetDefaultSchema() ?? "public";
        List<DeadLetter> deadLetters = [];
        foreach (var message in messages)
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            message.Attempts++;
            message.LastAttemptAtUtc = now;
            try
            {
                var type = Type.GetType(message.Type, throwOnError: true)!;
                var integrationEvent = (IIntegrationEvent)JsonSerializer.Deserialize(message.Content, type, OutboxExtensions.SerializerOptions)!;
                await bus.PublishAsync(integrationEvent, type, cancellationToken);
                message.ProcessedOnUtc = now;
                message.Error = null;
                delivered++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var error = (ex.InnerException ?? ex).ToString();
                message.Error = error.Length > 2000 ? error[..2000] : error;
                logger.LogWarning(ex, "Outbox message {Id} ({Type}) failed, attempt {Attempt}", message.Id, message.Type, message.Attempts);
                if (message.Attempts >= MaxAttempts)
                {
                    logger.LogError(ex, "Outbox message {Id} ({Type}) in {Schema} failed permanently after {Attempts} attempts", message.Id, message.Type, schema, message.Attempts);
                    deadLetters.Add(new DeadLetter(schema, message.Id, ShortTypeName(message.Type), message.Attempts, message.Error!, message.OccurredOnUtc, now));
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await ReportAsync(deadLetters, cancellationToken);
        return delivered;
    }

    /// <summary>After the commit, so a dead letter is reported only once it is really final.</summary>
    private async Task ReportAsync(List<DeadLetter> deadLetters, CancellationToken cancellationToken)
    {
        foreach (var deadLetter in deadLetters)
        {
            foreach (var listener in deadLetterListeners)
            {
                try
                {
                    await listener.OnDeadLetterAsync(deadLetter, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Could not report dead letter {Id}", deadLetter.MessageId);
                }
            }
        }
    }

    private static string ShortTypeName(string assemblyQualifiedName) =>
        assemblyQualifiedName.Split(',')[0].Split('.')[^1];
}
