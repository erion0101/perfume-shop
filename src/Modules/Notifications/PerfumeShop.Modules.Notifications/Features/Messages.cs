using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PerfumeShop.Modules.Notifications.Domain;
using PerfumeShop.Modules.Notifications.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Audit;
using PerfumeShop.Shared.Infrastructure.Auth;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Notifications.Features;

internal sealed record AttemptDto(DateTime AttemptedAtUtc, string Provider, bool Succeeded, string? ProviderMessageId, string? Response, string? Error);

internal sealed record MessageDto(
    Guid Id,
    string Channel,
    string Audience,
    string Recipient,
    string TemplateKey,
    string? Subject,
    string Body,
    string Status,
    string? OrderNumber,
    string SourceId,
    string? Encoding,
    int? Segments,
    bool HeldForQuietHours,
    DateTime SendAfterUtc,
    DateTime NextAttemptAtUtc,
    int Attempts,
    string? Provider,
    string? ProviderMessageId,
    string? LastError,
    DateTime CreatedAtUtc,
    DateTime? SentAtUtc,
    string? ResentBy,
    IReadOnlyList<AttemptDto> AttemptLog);

/// <param name="From">Created on or after this UTC date.</param>
internal sealed record ListMessagesQuery(
    string? Status, string? Channel, string? TemplateKey, string? Recipient, string? OrderNumber, DateOnly? From, DateOnly? To, int Page = 1, int PageSize = 50);

internal sealed class ListMessagesValidator : AbstractValidator<ListMessagesQuery>
{
    public ListMessagesValidator()
    {
        RuleFor(x => x.Status).IsEnumName(typeof(NotificationStatus), caseSensitive: false).When(x => x.Status is not null);
        RuleFor(x => x.Channel).IsEnumName(typeof(NotificationChannel), caseSensitive: false).When(x => x.Channel is not null);
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).When(x => x.From is not null && x.To is not null);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}

internal sealed class MessagesHandler(NotificationsDbContext db, QuietHours quietHours, TimeProvider timeProvider) : IHandler
{
    private static readonly Error NotFound = Error.NotFound("Notifications.Message.NotFound", "Message not found.");

    public async Task<PagedResponse<MessageDto>> ListAsync(ListMessagesQuery query, CancellationToken ct)
    {
        var messages = db.Messages.AsNoTracking();
        if (Enum.TryParse<NotificationStatus>(query.Status, ignoreCase: true, out var status))
        {
            messages = messages.Where(m => m.Status == status);
        }

        if (Enum.TryParse<NotificationChannel>(query.Channel, ignoreCase: true, out var channel))
        {
            messages = messages.Where(m => m.Channel == channel);
        }

        if (!string.IsNullOrWhiteSpace(query.TemplateKey))
        {
            messages = messages.Where(m => m.TemplateKey == query.TemplateKey.Trim());
        }

        if (!string.IsNullOrWhiteSpace(query.Recipient))
        {
            var recipient = query.Recipient.Trim();
            messages = messages.Where(m => m.Recipient.Contains(recipient));
        }

        if (!string.IsNullOrWhiteSpace(query.OrderNumber))
        {
            var number = query.OrderNumber.Trim().ToUpperInvariant();
            messages = messages.Where(m => m.OrderNumber == number);
        }

        if (query.From is { } from)
        {
            var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            messages = messages.Where(m => m.CreatedAtUtc >= fromUtc);
        }

        if (query.To is { } to)
        {
            var toUtc = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            messages = messages.Where(m => m.CreatedAtUtc < toUtc);
        }

        var total = await messages.CountAsync(ct);
        var page = await messages.Include(m => m.AttemptLog).OrderByDescending(m => m.CreatedAtUtc).ThenByDescending(m => m.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new PagedResponse<MessageDto>([.. page.Select(ToDto)], query.Page, query.PageSize, total);
    }

    public async Task<Result<MessageDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var message = await db.Messages.AsNoTracking().Include(m => m.AttemptLog).FirstOrDefaultAsync(m => m.Id == id, ct);
        return message is null ? NotFound : ToDto(message);
    }

    /// <summary>Queues a failed message again (same text). Customer SMS still respect quiet hours.</summary>
    public async Task<Result<MessageDto>> ResendAsync(Guid id, string actorId, CancellationToken ct)
    {
        var message = await db.Messages.Include(m => m.AttemptLog).FirstOrDefaultAsync(m => m.Id == id, ct);
        if (message is null)
        {
            return NotFound;
        }

        var resent = message.Resend(actorId, timeProvider.GetUtcNow().UtcDateTime, quietHours);
        if (resent.IsFailure)
        {
            return resent.Error;
        }

        await db.SaveChangesAsync(ct);
        return ToDto(message);
    }

    private static MessageDto ToDto(NotificationMessage m) => new(
        m.Id, m.Channel.ToString(), m.Audience.ToString(), m.Recipient, m.TemplateKey, m.Subject, m.Body, m.Status.ToString(), m.OrderNumber, m.SourceId,
        m.Encoding, m.Segments, m.HeldForQuietHours, m.SendAfterUtc, m.NextAttemptAtUtc, m.Attempts, m.Provider, m.ProviderMessageId, m.LastError,
        m.CreatedAtUtc, m.SentAtUtc, m.ResentBy,
        [.. m.AttemptLog.OrderBy(a => a.AttemptedAtUtc).ThenBy(a => a.Id)
            .Select(a => new AttemptDto(a.AttemptedAtUtc, a.Provider, a.Succeeded, a.ProviderMessageId, a.Response, a.Error))]);
}

internal static class MessagesEndpoint
{
    public static void Map(RouteGroupBuilder staff)
    {
        staff.MapGet("/messages", async ([AsParameters] ListMessagesQuery query, MessagesHandler h, CancellationToken ct) => TypedResults.Ok(await h.ListAsync(query, ct)))
            .WithValidation<ListMessagesQuery>()
            .WithName("ListNotificationMessages")
            .WithDescription("Notification log. Filters: status, channel, templateKey, recipient, orderNumber, from, to.");

        staff.MapGet("/messages/{id:guid}", async (Guid id, MessagesHandler h, CancellationToken ct) => (await h.GetAsync(id, ct)).ToOk())
            .WithName("GetNotificationMessage");

        staff.MapPost("/messages/{id:guid}/resend", async (Guid id, ClaimsPrincipal user, MessagesHandler h, CancellationToken ct) =>
                (await h.ResendAsync(id, user.GetUserId()?.ToString() ?? "unknown", ct)).ToOk())
            .Audited("Notifications.Message.Resent", "NotificationMessage", "Njoftimi u ridërgua")
            .WithName("ResendNotificationMessage")
            .WithDescription("Failed messages only (409 otherwise).");
    }
}
