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

/// <param name="Warning">Set when the SMS needs more than one segment (each segment is billed).</param>
internal sealed record SmsInfoDto(string Encoding, int Length, int Segments, int PerSegment, string NonGsmCharacters, string? Warning)
{
    public static SmsInfoDto? From(SmsInfo? info) => info is null
        ? null
        : new SmsInfoDto(info.Encoding, info.Length, info.Segments, info.PerSegment, info.NonGsmCharacters, info.Warning);
}

internal sealed record PlaceholderDto(string Name, string Sample);

/// <param name="Sms">Encoding and segment count of the text rendered with the sample values (SMS templates).</param>
/// <param name="SmsWithoutTransliteration">The same without ë→e / ç→c, to show what transliteration saves.</param>
internal sealed record TemplateDto(
    string Key,
    string Channel,
    string Audience,
    string? Alert,
    string Description,
    string? Subject,
    string Body,
    bool Transliterate,
    bool IsEnabled,
    IReadOnlyList<PlaceholderDto> Placeholders,
    SmsInfoDto? Sms,
    SmsInfoDto? SmsWithoutTransliteration,
    string? UpdatedBy,
    DateTime UpdatedAtUtc);

internal sealed record UpdateTemplateRequest(string? Subject, string Body, bool Transliterate = true, bool IsEnabled = true);

/// <summary>Draft text to preview (nothing is saved). Missing fields use the saved template; missing values use the samples.</summary>
internal sealed record PreviewTemplateRequest(string? Subject, string? Body, bool? Transliterate, Dictionary<string, string>? Values);

internal sealed record PreviewDto(string Key, string Channel, string? Subject, string Text, IReadOnlyList<string> UsedPlaceholders, SmsInfoDto? Sms);

internal sealed class UpdateTemplateValidator : AbstractValidator<UpdateTemplateRequest>
{
    public UpdateTemplateValidator()
    {
        RuleFor(x => x.Body).NotEmpty().MaximumLength(NotificationTemplate.EmailMaxLength);
        RuleFor(x => x.Subject).MaximumLength(NotificationTemplate.SubjectMaxLength);
    }
}

internal sealed class PreviewTemplateValidator : AbstractValidator<PreviewTemplateRequest>
{
    public PreviewTemplateValidator()
    {
        RuleFor(x => x.Body).MaximumLength(NotificationTemplate.EmailMaxLength);
        RuleFor(x => x.Subject).MaximumLength(NotificationTemplate.SubjectMaxLength);
        RuleForEach(x => x.Values).Must(v => v.Key.Length <= 50 && v.Value.Length <= 2000).When(x => x.Values is not null);
    }
}

internal sealed class TemplatesHandler(NotificationsDbContext db, IOptions<NotificationOptions> options, PerfumeShop.Modules.Administration.Contracts.IBusinessProfileApi business) : IHandler
{
    private static readonly Error NotFound = Error.NotFound("Notifications.Template.NotFound", "Template not found.");

    public async Task<IReadOnlyList<TemplateDto>> ListAsync(CancellationToken ct)
    {
        var templates = await db.Templates.AsNoTracking().ToDictionaryAsync(t => t.Key, ct);
        var shop = await ShopAsync(ct);
        return [.. TemplateCatalog.All.Where(d => templates.ContainsKey(d.Key)).Select(d => ToDto(templates[d.Key], d, shop))];
    }

    public async Task<Result<TemplateDto>> GetAsync(string key, CancellationToken ct)
    {
        var (template, definition) = await FindAsync(key, tracking: false, ct);
        return template is null ? NotFound : ToDto(template, definition!, await ShopAsync(ct));
    }

    public async Task<Result<TemplateDto>> UpdateAsync(string key, UpdateTemplateRequest request, string actorId, CancellationToken ct)
    {
        var (template, definition) = await FindAsync(key, tracking: true, ct);
        if (template is null)
        {
            return NotFound;
        }

        var updated = template.Update(definition!, request.Subject, request.Body, request.Transliterate, request.IsEnabled, actorId);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        await db.SaveChangesAsync(ct);
        return ToDto(template, definition!, await ShopAsync(ct));
    }

    public async Task<Result<PreviewDto>> PreviewAsync(string key, PreviewTemplateRequest? request, CancellationToken ct)
    {
        var (template, definition) = await FindAsync(key, tracking: false, ct);
        if (template is null)
        {
            return NotFound;
        }

        var subject = request?.Subject ?? template.Subject;
        var body = request?.Body ?? template.Body;
        var valid = NotificationTemplate.Validate(definition!, definition!.Channel == NotificationChannel.Sms ? null : subject, body);
        if (valid.IsFailure)
        {
            return valid.Error;
        }

        var values = new Dictionary<string, string>(definition.Placeholders, StringComparer.Ordinal);
        foreach (var (name, value) in request?.Values ?? [])
        {
            if (!definition.Placeholders.ContainsKey(name))
            {
                return Error.Validation("Notifications.Template.UnknownPlaceholder", $"Unknown placeholder {{{{{name}}}}}.");
            }

            values[name] = value;
        }

        var rendered = NotificationQueue.Render(definition, subject, body, request?.Transliterate ?? template.Transliterate, values, options.Value.WithShop(await business.GetAsync(ct)));
        return new PreviewDto(key, definition.Channel.ToString(), rendered.Subject, rendered.Body,
            TemplateText.UsedPlaceholders(body + " " + subject), SmsInfoDto.From(rendered.Sms));
    }

    private async Task<NotificationOptions> ShopAsync(CancellationToken ct) => options.Value.WithShop(await business.GetAsync(ct));

    private static TemplateDto ToDto(NotificationTemplate t, TemplateDefinition d, NotificationOptions shop)
    {
        var samples = new Dictionary<string, string>(d.Placeholders, StringComparer.Ordinal);
        var rendered = NotificationQueue.Render(t, d, samples, shop);
        var raw = d.Channel == NotificationChannel.Sms ? NotificationQueue.Render(d, t.Subject, t.Body, false, samples, shop) : null;
        return new TemplateDto(t.Key, t.Channel.ToString(), t.Audience.ToString(), d.Alert?.ToString(), d.Description, t.Subject, t.Body, t.Transliterate, t.IsEnabled,
            [.. d.Placeholders.Select(p => new PlaceholderDto(p.Key, p.Value))], SmsInfoDto.From(rendered.Sms), SmsInfoDto.From(raw?.Sms), t.UpdatedBy, t.UpdatedAtUtc);
    }

    private async Task<(NotificationTemplate? Template, TemplateDefinition? Definition)> FindAsync(string key, bool tracking, CancellationToken ct)
    {
        var definition = TemplateCatalog.Find(key);
        if (definition is null)
        {
            return (null, null);
        }

        var query = tracking ? db.Templates : db.Templates.AsNoTracking();
        return (await query.FirstOrDefaultAsync(t => t.Key == key, ct), definition);
    }
}

internal static class TemplatesEndpoint
{
    public static void Map(RouteGroupBuilder staff)
    {
        staff.MapGet("/templates", async (TemplatesHandler h, CancellationToken ct) => TypedResults.Ok(await h.ListAsync(ct)))
            .WithName("ListNotificationTemplates")
            .WithDescription("Every template with its placeholders and, for SMS, the segment count of the sample text.");

        staff.MapGet("/templates/{key}", async (string key, TemplatesHandler h, CancellationToken ct) => (await h.GetAsync(key, ct)).ToOk())
            .WithName("GetNotificationTemplate");

        staff.MapPut("/templates/{key}", async (string key, UpdateTemplateRequest request, ClaimsPrincipal user, TemplatesHandler h, CancellationToken ct) =>
                (await h.UpdateAsync(key, request, user.GetUserId()?.ToString() ?? "unknown", ct)).ToOk())
            .RequireAuthorization(Policies.Admin)
            .WithValidation<UpdateTemplateRequest>()
            .Audited("Settings.NotificationTemplate.Updated", "NotificationTemplate", "Shablloni i njoftimit u ndryshua")
            .WithName("UpdateNotificationTemplate")
            .WithDescription("Admin. Unknown placeholders are rejected (400).");

        staff.MapPost("/templates/{key}/preview", async (string key, PreviewTemplateRequest? request, TemplatesHandler h, CancellationToken ct) =>
                (await h.PreviewAsync(key, request, ct)).ToOk())
            .WithValidation<PreviewTemplateRequest>()
            .WithName("PreviewNotificationTemplate")
            .WithDescription("Renders the saved template, or a draft, with sample data. Nothing is saved or sent.");
    }
}
