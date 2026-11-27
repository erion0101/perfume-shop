using System.Collections.Concurrent;
using System.Linq.Expressions;
using Hangfire;

namespace PerfumeShop.Shared.Infrastructure.Jobs;

/// <summary>
/// One-off background work with retries (Hangfire: 10 attempts with growing delays). Use it for calls to other
/// systems that must not slow down or fail the request, e.g. telling the storefront to revalidate its pages.
/// </summary>
public interface IBackgroundJobQueue
{
    void Enqueue<TJob>(Expression<Func<TJob, Task>> call)
        where TJob : class;
}

internal sealed class HangfireBackgroundJobQueue(IBackgroundJobClient client) : IBackgroundJobQueue
{
    public void Enqueue<TJob>(Expression<Func<TJob, Task>> call)
        where TJob : class => client.Enqueue(call);
}

/// <summary>
/// Used when background jobs are disabled (integration tests): records what would have been queued so tests can
/// assert on it and run the job themselves.
/// </summary>
public sealed class RecordingBackgroundJobQueue : IBackgroundJobQueue
{
    private readonly ConcurrentQueue<LambdaExpression> _queued = new();

    public IReadOnlyList<LambdaExpression> Queued => [.. _queued];

    public void Enqueue<TJob>(Expression<Func<TJob, Task>> call)
        where TJob : class => _queued.Enqueue(call);

    /// <summary>Queued calls for <typeparamref name="TJob"/>, oldest first.</summary>
    public IReadOnlyList<Expression<Func<TJob, Task>>> For<TJob>()
        where TJob : class => [.. _queued.OfType<Expression<Func<TJob, Task>>>()];

    public void Clear() => _queued.Clear();
}
