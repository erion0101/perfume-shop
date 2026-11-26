using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Notifications.Domain;
using PerfumeShop.Modules.Notifications.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Auth;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Notifications.Features;

internal sealed record StaffAlertSettingsDto(
    IReadOnlyList<string> Recipients, bool NewOrder, bool FailedReceipt, bool LowStock, bool UnpaidDeliveries, bool JobFailures, string? UpdatedBy, DateTime UpdatedAtUtc);

internal sealed record UpdateStaffAlertSettingsRequest(
    IReadOnlyList<string> Recipients, bool NewOrder = true, bool FailedReceipt = true, bool LowStock = true, bool UnpaidDeliveries = true, bool JobFailures = true);

internal sealed class UpdateStaffAlertSettingsValidator : AbstractValidator<UpdateStaffAlertSettingsRequest>
{
    public UpdateStaffAlertSettingsValidator()
    {
        RuleFor(x => x.Recipients).NotNull().Must(r => r.Count <= StaffAlertSettings.MaxRecipients);
        RuleForEach(x => x.Recipients).NotEmpty().EmailAddress().MaximumLength(200);
    }
}

internal sealed class StaffAlertsHandler(NotificationsDbContext db) : IHandler
{
    public async Task<StaffAlertSettingsDto> GetAsync(CancellationToken ct) => ToDto(await LoadAsync(ct));

    public async Task<Result<StaffAlertSettingsDto>> UpdateAsync(UpdateStaffAlertSettingsRequest request, string actorId, CancellationToken ct)
    {
        var settings = await LoadAsync(ct);
        var updated = settings.Update(request.Recipients, request.NewOrder, request.FailedReceipt, request.LowStock, request.UnpaidDeliveries, request.JobFailures, actorId);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        await db.SaveChangesAsync(ct);
        return ToDto(settings);
    }

    private async Task<StaffAlertSettings> LoadAsync(CancellationToken ct) =>
        await db.StaffAlertSettings.FirstOrDefaultAsync(ct)
        ?? throw new InvalidOperationException("Staff alert settings are missing (seeded on startup).");

    private static StaffAlertSettingsDto ToDto(StaffAlertSettings s) =>
        new(s.Recipients, s.NewOrder, s.FailedReceipt, s.LowStock, s.UnpaidDeliveries, s.JobFailures, s.UpdatedBy, s.UpdatedAtUtc);
}

internal static class StaffAlertsEndpoint
{
    public static void Map(RouteGroupBuilder staff)
    {
        staff.MapGet("/settings/staff-alerts", async (StaffAlertsHandler h, CancellationToken ct) => TypedResults.Ok(await h.GetAsync(ct)))
            .WithName("GetStaffAlertSettings");

        staff.MapPut("/settings/staff-alerts", async (UpdateStaffAlertSettingsRequest request, ClaimsPrincipal user, StaffAlertsHandler h, CancellationToken ct) =>
                (await h.UpdateAsync(request, user.GetUserId()?.ToString() ?? "unknown", ct)).ToOk())
            .RequireAuthorization(Policies.Admin)
            .WithValidation<UpdateStaffAlertSettingsRequest>()
            .WithName("UpdateStaffAlertSettings")
            .WithDescription("Admin. Recipients (emails) and which alerts are on: new order, failed fiscal receipt, low stock, unpaid deliveries, job failures.");
    }
}
