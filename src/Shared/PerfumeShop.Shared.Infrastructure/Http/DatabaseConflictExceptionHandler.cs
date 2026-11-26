using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace PerfumeShop.Shared.Infrastructure.Http;

/// <summary>
/// Turns database races into 409 ProblemDetails instead of 500:
/// unique-index violations (e.g. two admins creating the same slug) and optimistic concurrency (xmin).
/// </summary>
public sealed class DatabaseConflictExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (code, detail) = exception switch
        {
            DbUpdateConcurrencyException => ("Database.ConcurrencyConflict", "The record was changed by someone else. Reload and try again."),
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg } =>
                ("Database.UniqueViolation", $"A record with the same value already exists ({pg.ConstraintName})."),
            _ => (null, null),
        };

        if (code is null)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = code,
                Detail = detail,
                Extensions = { ["code"] = code },
            },
        });
    }
}
