namespace PerfumeShop.Shared.Kernel;

/// <summary>Kind of failure; the HTTP layer maps it to a status code.</summary>
public enum ErrorType
{
    /// <summary>Input or business rule violation (400).</summary>
    Validation = 0,

    /// <summary>The thing does not exist (404).</summary>
    NotFound = 1,

    /// <summary>Clashes with current state, e.g. duplicates or a forbidden transition (409).</summary>
    Conflict = 2,

    /// <summary>A quota or budget is exhausted (429).</summary>
    LimitExceeded = 3,

    /// <summary>An external dependency failed (503).</summary>
    Unavailable = 4,

    /// <summary>Missing or invalid credentials (401).</summary>
    Unauthorized = 5,

    /// <summary>Authenticated but not allowed, e.g. a blocked customer (403).</summary>
    Forbidden = 6,
}

/// <summary>A failure description returned inside a <see cref="Result"/>.</summary>
public sealed record Error(string Code, string Message, ErrorType Type = ErrorType.Validation)
{
    public static readonly Error None = new(string.Empty, string.Empty);

    /// <summary>Structured data for clients (e.g. which parcels lack a receipt); becomes ProblemDetails extensions.</summary>
    public IReadOnlyDictionary<string, object?>? Details { get; init; }

    public Error WithDetails(string key, object? value) =>
        this with { Details = new Dictionary<string, object?>(Details ?? new Dictionary<string, object?>()) { [key] = value } };

    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error LimitExceeded(string code, string message) => new(code, message, ErrorType.LimitExceeded);

    public static Error Unavailable(string code, string message) => new(code, message, ErrorType.Unavailable);

    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);
}
