using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Inventory.Infrastructure;

/// <summary>
/// Runs a stock change in a transaction and retries it from scratch when another request changed the
/// same stock row first (optimistic concurrency) or inserted the same order line / variant at the same time.
/// The work must load fresh data and call SaveChangesAsync itself.
/// </summary>
internal sealed class StockTransactions(InventoryDbContext db, IOptions<InventoryOptions> options)
{
    public async Task<Result<T>> RunAsync<T>(Func<CancellationToken, Task<Result<T>>> work, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var result = await work(cancellationToken);
                if (result.IsSuccess)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
                else
                {
                    await transaction.RollbackAsync(cancellationToken);
                    db.ChangeTracker.Clear();
                }

                return result;
            }
            catch (DbUpdateException ex) when (attempt < options.Value.MaxConcurrencyRetries && IsRace(ex))
            {
                await transaction.RollbackAsync(cancellationToken);
                db.ChangeTracker.Clear();
                await Task.Delay(Random.Shared.Next(5, 25 * attempt), cancellationToken);
            }
        }
    }

    public async Task<Result> RunAsync(Func<CancellationToken, Task<Result>> work, CancellationToken cancellationToken)
    {
        var result = await RunAsync<bool>(async ct =>
        {
            var inner = await work(ct);
            return inner.IsSuccess ? true : inner.Error;
        }, cancellationToken);

        return result.IsSuccess ? Result.Success() : result.Error;
    }

    private static bool IsRace(DbUpdateException exception) =>
        exception is DbUpdateConcurrencyException
        || exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
