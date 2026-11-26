using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Shipping.Domain;
using PerfumeShop.Modules.Shipping.Features.Shared;
using PerfumeShop.Modules.Shipping.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Shipping.Features.Couriers;

/// <param name="AcceptsFlammableGoods">Must be true for the courier to be selectable (perfume is UN1266).</param>
/// <param name="RemittanceDays">Days after delivery by which the courier should pay the COD money.</param>
internal sealed record CourierRequest(
    string Name,
    string? ContactName,
    string? ContactPhone,
    string? ContactEmail,
    List<string> EnabledCountries,
    decimal DeliveryFee,
    decimal CodFeeFixed,
    decimal CodFeePercent,
    bool AcceptsFlammableGoods,
    RemittanceSchedule RemittanceSchedule,
    int RemittanceDays,
    string? Notes,
    bool IsEnabled)
{
    public CourierDetails ToDetails() => new(Name, ContactName, ContactPhone, ContactEmail, EnabledCountries, DeliveryFee, CodFeeFixed, CodFeePercent,
        AcceptsFlammableGoods, RemittanceSchedule, RemittanceDays, Notes, IsEnabled);
}

internal sealed class CourierRequestValidator : AbstractValidator<CourierRequest>
{
    public CourierRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnabledCountries).NotNull();
        RuleForEach(x => x.EnabledCountries).Must(c => Courier.SupportedCountries.Contains(c.ToUpperInvariant())).WithMessage("Countries: XK, AL, MK.");
        RuleFor(x => x.DeliveryFee).InclusiveBetween(0, 100);
        RuleFor(x => x.CodFeeFixed).InclusiveBetween(0, 100);
        RuleFor(x => x.CodFeePercent).InclusiveBetween(0, 10);
        RuleFor(x => x.RemittanceSchedule).IsInEnum();
        RuleFor(x => x.RemittanceDays).InclusiveBetween(1, 60);
        RuleFor(x => x.ContactEmail).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.ContactEmail));
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

internal sealed class CouriersHandler(ShippingDbContext db) : IHandler
{
    public async Task<IReadOnlyList<CourierDto>> ListAsync(CancellationToken ct) =>
        [.. (await db.Couriers.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct)).Select(c => c.ToDto())];

    public async Task<Result<CourierDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var courier = await db.Couriers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        return courier is null ? ShippingFeatureErrors.CourierNotFound : courier.ToDto();
    }

    public async Task<Result<CourierDto>> CreateAsync(CourierRequest request, CancellationToken ct)
    {
        if (await NameTakenAsync(request.Name, null, ct))
        {
            return ShippingFeatureErrors.CourierNameTaken;
        }

        var courier = Courier.Create(request.ToDetails());
        if (courier.IsFailure)
        {
            return courier.Error;
        }

        db.Couriers.Add(courier.Value);
        await db.SaveChangesAsync(ct);
        return courier.Value.ToDto();
    }

    public async Task<Result<CourierDto>> UpdateAsync(Guid id, CourierRequest request, CancellationToken ct)
    {
        var courier = await db.Couriers.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (courier is null)
        {
            return ShippingFeatureErrors.CourierNotFound;
        }

        if (await NameTakenAsync(request.Name, id, ct))
        {
            return ShippingFeatureErrors.CourierNameTaken;
        }

        var updated = courier.Update(request.ToDetails());
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        await db.SaveChangesAsync(ct);
        return courier.ToDto();
    }

    private Task<bool> NameTakenAsync(string name, Guid? except, CancellationToken ct)
    {
        var lowered = name.Trim().ToLowerInvariant();
        return db.Couriers.AnyAsync(c => c.Name.ToLower() == lowered && c.Id != except, ct);
    }
}

internal static class CouriersEndpoint
{
    public static void MapRead(RouteGroupBuilder staff)
    {
        staff.MapGet("/couriers", async (CouriersHandler h, CancellationToken ct) => TypedResults.Ok(await h.ListAsync(ct))).WithName("ListCouriers");
        staff.MapGet("/couriers/{id:guid}", async (Guid id, CouriersHandler h, CancellationToken ct) => (await h.GetAsync(id, ct)).ToOk()).WithName("GetCourier");
    }

    public static void MapWrite(RouteGroupBuilder admin)
    {
        admin.MapPost("/couriers", async (CourierRequest request, CouriersHandler h, CancellationToken ct) =>
                (await h.CreateAsync(request, ct)).ToCreated(c => $"/api/admin/shipping/couriers/{c.Id}"))
            .WithValidation<CourierRequest>()
            .WithName("CreateCourier");

        admin.MapPut("/couriers/{id:guid}", async (Guid id, CourierRequest request, CouriersHandler h, CancellationToken ct) =>
                (await h.UpdateAsync(id, request, ct)).ToOk())
            .WithValidation<CourierRequest>()
            .WithName("UpdateCourier");
    }
}
