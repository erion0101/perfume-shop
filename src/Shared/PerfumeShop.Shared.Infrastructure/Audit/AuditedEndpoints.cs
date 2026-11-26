using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace PerfumeShop.Shared.Infrastructure.Audit;

/// <summary>
/// <c>.Audited(...)</c> on a back-office endpoint: after a successful (2xx) response it records the action with the
/// route id and the request body (password, code and token fields redacted). Failed requests are not audited.
/// </summary>
public static class AuditedEndpoints
{
    private static readonly string[] IdRouteKeys = ["variantId", "id", "orderId", "countryCode", "key"];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static RouteHandlerBuilder Audited(this RouteHandlerBuilder builder, string action, string entityType, string summary) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var result = await next(context);
            if (!IsSuccess(result))
            {
                return result;
            }

            var http = context.HttpContext;
            var entityId = IdRouteKeys.Select(k => http.Request.RouteValues.TryGetValue(k, out var v) ? v?.ToString() : null).FirstOrDefault(v => v is not null);
            var changes = new Dictionary<string, object?>();
            foreach (var argument in context.Arguments)
            {
                // Our own request records only (never HttpRequest, HttpContext, ClaimsPrincipal, ...).
                if (argument is not null
                    && argument.GetType().Namespace?.StartsWith("PerfumeShop.", StringComparison.Ordinal) == true
                    && argument.GetType().Name.EndsWith("Request", StringComparison.Ordinal))
                {
                    changes["request"] = Redact(JsonSerializer.SerializeToNode(argument, argument.GetType(), Json));
                }
            }

            foreach (var (key, value) in http.Request.Query)
            {
                changes[$"query.{key}"] = value.ToString();
            }

            await http.RequestServices.GetRequiredService<IAuditLog>()
                .RecordAsync(new AuditEntry(action, entityType, entityId, summary, changes.Count == 0 ? null : changes), http.RequestAborted);
            return result;
        });

    private static bool IsSuccess(object? result)
    {
        while (result is INestedHttpResult nested)
        {
            result = nested.Result;
        }

        return result is IStatusCodeHttpResult { StatusCode: >= 200 and < 300 } || result is IStatusCodeHttpResult { StatusCode: null };
    }

    /// <summary>Secrets never reach the audit log.</summary>
    public static JsonNode? Redact(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    var lower = key.ToLowerInvariant();
                    if (lower.EndsWith("password", StringComparison.Ordinal) || lower is "code" or "twofactorcode" or "recoverycode" or "token" or "refreshtoken" or "secret")
                    {
                        obj[key] = "***";
                    }
                    else
                    {
                        Redact(obj[key]);
                    }
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    Redact(item);
                }

                break;
        }

        return node;
    }
}
