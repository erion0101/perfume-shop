using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PerfumeShop.Shared.Infrastructure.Jobs;

/// <param name="JobId">Hangfire background job id (one alert per failed job).</param>
/// <param name="RecurringJobId">e.g. "orders-outbox", when the job came from a schedule.</param>
public sealed record JobFailure(string JobId, string JobName, string? RecurringJobId, string Reason, DateTime FailedAtUtc);

/// <summary>Told when a background job ends up Failed (after Hangfire's automatic retries). Notifications alerts staff.</summary>
public interface IJobFailureListener
{
    Task OnJobFailedAsync(JobFailure failure, CancellationToken cancellationToken);
}

/// <summary>
/// Runs after Hangfire's AutomaticRetry filter (order 20), so it only sees jobs that really failed.
/// A listener error is logged and never changes the job's state.
/// </summary>
internal sealed class JobFailureFilter(IServiceScopeFactory scopes, TimeProvider timeProvider, ILogger<JobFailureFilter> logger) : JobFilterAttribute, IElectStateFilter
{
    public const int FilterOrder = 100;

    public void OnStateElection(ElectStateContext context)
    {
        if (context.CandidateState is not FailedState failed)
        {
            return;
        }

        var job = context.BackgroundJob.Job;
        var failure = new JobFailure(
            context.BackgroundJob.Id,
            job is null ? "unknown" : $"{job.Type.Name}.{job.Method.Name}",
            context.GetJobParameter<string>("RecurringJobId"),
            $"{failed.Exception?.GetType().Name}: {failed.Exception?.Message}",
            timeProvider.GetUtcNow().UtcDateTime);
        logger.LogError(failed.Exception, "Background job {JobName} ({JobId}, {RecurringJobId}) failed", failure.JobName, failure.JobId, failure.RecurringJobId);

        try
        {
            using var scope = scopes.CreateScope();
            foreach (var listener in scope.ServiceProvider.GetServices<IJobFailureListener>())
            {
                // Hangfire state filters are synchronous.
                listener.OnJobFailedAsync(failure, CancellationToken.None).GetAwaiter().GetResult();
            }
        }
#pragma warning disable CA1031 // an alert problem must never break Hangfire's state machine
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogError(ex, "Could not report the failure of job {JobId}", failure.JobId);
        }
    }
}
