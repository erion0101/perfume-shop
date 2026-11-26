using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Shared.Infrastructure.Http;

/// <summary>
/// Maps domain results to HTTP responses; failures become RFC 9457 ProblemDetails. The typed return values
/// (<see cref="Results{TResult1, TResult2}"/>) put the success body type into the OpenAPI document, which the
/// TypeScript client (web/packages/api-client) is generated from.
/// </summary>
public static class ResultHttpExtensions
{
    public static ProblemHttpResult ToProblem(this Error error)
    {
        var status = error.Type switch
        {
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.LimitExceeded => StatusCodes.Status429TooManyRequests,
            ErrorType.Unavailable => StatusCodes.Status503ServiceUnavailable,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest,
        };

        var extensions = new Dictionary<string, object?>(error.Details ?? new Dictionary<string, object?>()) { ["code"] = error.Code };
        return TypedResults.Problem(statusCode: status, title: error.Code, detail: error.Message, extensions: extensions);
    }

    public static Results<Ok<T>, ProblemHttpResult> ToOk<T>(this Result<T> result) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();

    public static Results<NoContent, ProblemHttpResult> ToNoContent(this Result result) =>
        result.IsSuccess ? TypedResults.NoContent() : result.Error.ToProblem();

    public static Results<Created<T>, ProblemHttpResult> ToCreated<T>(this Result<T> result, Func<T, string> location) =>
        result.IsSuccess ? TypedResults.Created(location(result.Value), result.Value) : result.Error.ToProblem();
}
