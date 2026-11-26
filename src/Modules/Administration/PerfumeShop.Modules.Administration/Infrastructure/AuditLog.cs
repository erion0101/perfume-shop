using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PerfumeShop.Modules.Administration.Domain;
using PerfumeShop.Shared.Infrastructure.Audit;
using PerfumeShop.Shared.Infrastructure.Auth;

namespace PerfumeShop.Modules.Administration.Infrastructure;

/// <summary>Stores audit entries with the signed-in user (token "sub" and "email") and IP. Best effort: never throws.</summary>
internal sealed class AuditLog(AdministrationDbContext db, IHttpContextAccessor http, TimeProvider timeProvider, ILogger<AuditLog> logger) : IAuditLog
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        var user = http.HttpContext?.User;
        try
        {
            db.AuditRecords.Add(new AuditRecord
            {
                OccurredAtUtc = timeProvider.GetUtcNow().UtcDateTime,
                ActorId = user?.FindFirst(PlatformClaims.Subject)?.Value ?? "system",
                ActorEmail = user?.FindFirst(PlatformClaims.Email)?.Value,
                Action = entry.Action,
                EntityType = entry.EntityType,
                EntityId = entry.EntityId,
                Summary = entry.Summary.Length > 500 ? entry.Summary[..500] : entry.Summary,
                Changes = entry.Changes is null ? null : JsonSerializer.Serialize(entry.Changes, Json),
                IpAddress = http.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            logger.LogError(ex, "Audit entry {Action} for {EntityType} {EntityId} could not be stored", entry.Action, entry.EntityType, entry.EntityId);
        }
    }
}
