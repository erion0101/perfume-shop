using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Administration.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;

namespace PerfumeShop.Modules.Administration.Features;

internal sealed record AuditEntryDto(
    Guid Id, DateTime OccurredAtUtc, string ActorId, string? ActorEmail, string Action, string EntityType, string? EntityId, string Summary, string? Changes, string? IpAddress);

/// <param name="Action">Exact action or a prefix ending with "." (e.g. "Catalog.").</param>
/// <param name="Actor">Part of the actor's email, or the actor id.</param>
/// <param name="Search">Part of the summary or the entity id.</param>
internal sealed record ListAuditQuery(string? Action, string? EntityType, string? EntityId, string? Actor, string? Search, DateOnly? From, DateOnly? To, int Page = 1, int PageSize = 50);

internal sealed class ListAuditValidator : AbstractValidator<ListAuditQuery>
{
    public ListAuditValidator()
    {
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).When(x => x.From is not null && x.To is not null);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}

internal sealed class AuditHandler(AdministrationDbContext db) : IHandler
{
    public async Task<PagedResponse<AuditEntryDto>> ListAsync(ListAuditQuery q, CancellationToken ct)
    {
        var rows = db.AuditRecords.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Action))
        {
            var action = q.Action.Trim();
            rows = action.EndsWith('.') ? rows.Where(a => a.Action.StartsWith(action)) : rows.Where(a => a.Action == action);
        }

        if (!string.IsNullOrWhiteSpace(q.EntityType))
        {
            rows = rows.Where(a => a.EntityType == q.EntityType.Trim());
        }

        if (!string.IsNullOrWhiteSpace(q.EntityId))
        {
            rows = rows.Where(a => a.EntityId == q.EntityId.Trim());
        }

        if (!string.IsNullOrWhiteSpace(q.Actor))
        {
            var actor = q.Actor.Trim().ToLower();
            rows = rows.Where(a => a.ActorId == actor || (a.ActorEmail != null && a.ActorEmail.ToLower().Contains(actor)));
        }

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var term = q.Search.Trim().ToLower();
            rows = rows.Where(a => a.Summary.ToLower().Contains(term) || (a.EntityId != null && a.EntityId.ToLower().Contains(term)));
        }

        if (q.From is { } from)
        {
            var start = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            rows = rows.Where(a => a.OccurredAtUtc >= start);
        }

        if (q.To is { } to)
        {
            var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            rows = rows.Where(a => a.OccurredAtUtc < end);
        }

        var total = await rows.CountAsync(ct);
        var page = await rows.OrderByDescending(a => a.OccurredAtUtc).ThenByDescending(a => a.Id)
            .Skip((q.Page - 1) * q.PageSize).Take(q.PageSize)
            .Select(a => new AuditEntryDto(a.Id, a.OccurredAtUtc, a.ActorId, a.ActorEmail, a.Action, a.EntityType, a.EntityId, a.Summary, a.Changes, a.IpAddress))
            .ToListAsync(ct);
        return new PagedResponse<AuditEntryDto>(page, q.Page, q.PageSize, total);
    }
}

internal static class AuditEndpoint
{
    public static void Map(RouteGroupBuilder admin) =>
        admin.MapGet("/audit", async ([AsParameters] ListAuditQuery query, AuditHandler h, CancellationToken ct) => TypedResults.Ok(await h.ListAsync(query, ct)))
            .WithValidation<ListAuditQuery>()
            .WithName("ListAuditEntries")
            .WithDescription("Who did what in the back office (Admin only), newest first.");
}
