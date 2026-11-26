using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Orders.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Orders.Features.OrderSettings;

internal sealed record OrderSettingsDto(int MaxOpenOrdersPerCustomer, int MaxRefusedOrders, int PendingExpiryHours, DateTime UpdatedAtUtc);

internal sealed record UpdateOrderSettingsRequest(int MaxOpenOrdersPerCustomer, int MaxRefusedOrders, int PendingExpiryHours);

internal sealed record DeliveryRateDto(string CountryCode, string CountryName, bool IsEnabled, decimal Fee, decimal? FreeDeliveryFrom, string Currency, DateTime UpdatedAtUtc);

internal sealed record UpdateDeliveryRateRequest(bool IsEnabled, decimal Fee, decimal? FreeDeliveryFrom);

internal sealed class UpdateOrderSettingsValidator : AbstractValidator<UpdateOrderSettingsRequest>
{
    public UpdateOrderSettingsValidator()
    {
        RuleFor(x => x.MaxOpenOrdersPerCustomer).InclusiveBetween(1, 20);
        RuleFor(x => x.MaxRefusedOrders).InclusiveBetween(0, 20);
        RuleFor(x => x.PendingExpiryHours).InclusiveBetween(1, 720);
    }
}

internal sealed class UpdateDeliveryRateValidator : AbstractValidator<UpdateDeliveryRateRequest>
{
    public UpdateDeliveryRateValidator()
    {
        RuleFor(x => x.Fee).InclusiveBetween(0, Domain.DeliveryRate.MaxFee).PrecisionScale(10, 2, ignoreTrailingZeros: true);
        RuleFor(x => x.FreeDeliveryFrom).GreaterThan(0).PrecisionScale(10, 2, ignoreTrailingZeros: true).When(x => x.FreeDeliveryFrom is not null);
    }
}

/// <summary>Admin-only: anti-fake-order rules, confirmation window, delivery fees per country.</summary>
internal sealed class OrderSettingsHandler(OrdersDbContext db, TimeProvider timeProvider) : IHandler
{
    public async Task<OrderSettingsDto> GetSettingsAsync(CancellationToken cancellationToken)
    {
        var s = await db.Settings.AsNoTracking().SingleAsync(cancellationToken);
        return new OrderSettingsDto(s.MaxOpenOrdersPerCustomer, s.MaxRefusedOrders, s.PendingExpiryHours, s.UpdatedAtUtc);
    }

    /// <summary>Applies to new orders; the confirmation window of existing orders does not change.</summary>
    public async Task<Result<OrderSettingsDto>> UpdateSettingsAsync(UpdateOrderSettingsRequest request, CancellationToken cancellationToken)
    {
        var s = await db.Settings.SingleAsync(cancellationToken);
        var updated = s.Update(request.MaxOpenOrdersPerCustomer, request.MaxRefusedOrders, request.PendingExpiryHours, timeProvider.GetUtcNow().UtcDateTime);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new OrderSettingsDto(s.MaxOpenOrdersPerCustomer, s.MaxRefusedOrders, s.PendingExpiryHours, s.UpdatedAtUtc);
    }

    public async Task<IReadOnlyList<DeliveryRateDto>> ListRatesAsync(CancellationToken cancellationToken) =>
        await db.DeliveryRates.AsNoTracking()
            .OrderBy(r => r.CountryCode)
            .Select(r => new DeliveryRateDto(r.CountryCode, r.CountryName, r.IsEnabled, r.Fee, r.FreeDeliveryFrom, Domain.Order.Currency, r.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

    public async Task<Result<DeliveryRateDto>> UpdateRateAsync(string countryCode, UpdateDeliveryRateRequest request, CancellationToken cancellationToken)
    {
        var code = countryCode.Trim().ToUpperInvariant();
        var rate = await db.DeliveryRates.FirstOrDefaultAsync(r => r.CountryCode == code, cancellationToken);
        if (rate is null)
        {
            return Error.NotFound("Orders.Delivery.CountryNotFound", "Only XK, AL and MK are configured.");
        }

        var updated = rate.Update(request.IsEnabled, request.Fee, request.FreeDeliveryFrom, timeProvider.GetUtcNow().UtcDateTime);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new DeliveryRateDto(rate.CountryCode, rate.CountryName, rate.IsEnabled, rate.Fee, rate.FreeDeliveryFrom, Domain.Order.Currency, rate.UpdatedAtUtc);
    }
}

internal static class OrderSettingsEndpoint
{
    public static void Map(RouteGroupBuilder admin)
    {
        admin.MapGet("/settings", async (OrderSettingsHandler h, CancellationToken ct) => TypedResults.Ok(await h.GetSettingsAsync(ct)))
            .WithName("GetOrderSettings");

        admin.MapPut("/settings", async (UpdateOrderSettingsRequest request, OrderSettingsHandler h, CancellationToken ct) =>
                (await h.UpdateSettingsAsync(request, ct)).ToOk())
            .WithValidation<UpdateOrderSettingsRequest>()
            .WithName("UpdateOrderSettings");

        admin.MapGet("/delivery-rates", async (OrderSettingsHandler h, CancellationToken ct) => TypedResults.Ok(await h.ListRatesAsync(ct)))
            .WithName("ListDeliveryRates");

        admin.MapPut("/delivery-rates/{countryCode}", async (string countryCode, UpdateDeliveryRateRequest request, OrderSettingsHandler h, CancellationToken ct) =>
                (await h.UpdateRateAsync(countryCode, request, ct)).ToOk())
            .WithValidation<UpdateDeliveryRateRequest>()
            .WithName("UpdateDeliveryRate");
    }
}
