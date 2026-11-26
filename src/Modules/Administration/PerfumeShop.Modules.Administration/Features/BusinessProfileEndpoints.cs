using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PerfumeShop.Modules.Administration.Contracts;
using PerfumeShop.Modules.Administration.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Audit;
using PerfumeShop.Shared.Infrastructure.Auth;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Administration.Features;

internal sealed record UpdateBusinessProfileRequest(string ShopName, string Address, string Phone, string? Email, string TaxNumber);

internal sealed class UpdateBusinessProfileValidator : AbstractValidator<UpdateBusinessProfileRequest>
{
    public UpdateBusinessProfileValidator()
    {
        RuleFor(x => x.ShopName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Address).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(30);
        RuleFor(x => x.TaxNumber).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Email).MaximumLength(200);
    }
}

internal sealed class BusinessProfileHandler(AdministrationDbContext db, IBusinessProfileApi api, IMemoryCache cache, IAuditLog audit) : IHandler
{
    public Task<BusinessProfileDto> GetAsync(CancellationToken ct) => api.GetAsync(ct);

    public async Task<Result<BusinessProfileDto>> UpdateAsync(UpdateBusinessProfileRequest request, string actorId, CancellationToken ct)
    {
        var profile = await db.BusinessProfiles.FirstAsync(ct);
        var before = BusinessProfileApi.ToDto(profile);
        var updated = profile.Update(request.ShopName, request.Address, request.Phone, request.Email, request.TaxNumber, actorId);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        await db.SaveChangesAsync(ct);
        cache.Remove(BusinessProfileApi.CacheKey);
        var after = BusinessProfileApi.ToDto(profile);
        await audit.RecordAsync(new AuditEntry("Settings.Business.Updated", "BusinessProfile", "1", "Të dhënat e biznesit u ndryshuan", new Dictionary<string, object?>
        {
            ["before"] = new { before.ShopName, before.Address, before.Phone, before.Email, before.TaxNumber },
            ["after"] = new { after.ShopName, after.Address, after.Phone, after.Email, after.TaxNumber },
        }), ct);
        return after;
    }
}

internal static class BusinessProfileEndpoint
{
    public static void Map(RouteGroupBuilder staff, RouteGroupBuilder admin)
    {
        staff.MapGet("/settings/business", async (BusinessProfileHandler h, CancellationToken ct) => TypedResults.Ok(await h.GetAsync(ct)))
            .WithName("GetBusinessProfile")
            .WithDescription("Shop name, address, phone, fiscal number used on receipts, packing slips and SMS; placeholderFields lists what is still a placeholder.");

        admin.MapPut("/settings/business", async (UpdateBusinessProfileRequest request, ClaimsPrincipal user, BusinessProfileHandler h, CancellationToken ct) =>
                (await h.UpdateAsync(request, user.GetUserId()?.ToString() ?? "unknown", ct)).ToOk())
            .WithValidation<UpdateBusinessProfileRequest>()
            .WithName("UpdateBusinessProfile");
    }
}
