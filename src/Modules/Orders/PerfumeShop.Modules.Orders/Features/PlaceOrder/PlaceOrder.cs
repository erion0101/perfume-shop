using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PerfumeShop.Modules.Customers.Contracts;
using PerfumeShop.Modules.Inventory.Contracts;
using PerfumeShop.Modules.Orders.Domain;
using PerfumeShop.Modules.Orders.Features.QuoteCart;
using PerfumeShop.Modules.Orders.Features.Shared;
using PerfumeShop.Modules.Orders.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Auth;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Orders.Features.PlaceOrder;

/// <param name="AddressId">One of the customer's saved addresses; the default address when omitted.</param>
/// <param name="FullName">Recipient name; required when the customer profile has none (it is then saved).</param>
internal sealed record PlaceOrderRequest(List<CartItemRequest> Items, Guid? AddressId, string? FullName, string? CustomerNote);

internal sealed record PlaceOrderResult(OrderDto Order, bool Replayed);

internal sealed class PlaceOrderValidator : AbstractValidator<PlaceOrderRequest>
{
    public PlaceOrderValidator()
    {
        RuleFor(x => x.Items).NotEmpty().Must(i => i.Count <= Order.MaxLines);
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.VariantId).NotEmpty();
            item.RuleFor(i => i.Quantity).InclusiveBetween(1, Order.MaxQuantityPerLine);
        });
        RuleFor(x => x.FullName).MaximumLength(Order.TextMaxLength);
        RuleFor(x => x.CustomerNote).MaximumLength(Order.NoteMaxLength);
    }
}

/// <summary>
/// Cash on Delivery checkout. Prices come from Catalog (never from the request), the address from
/// Customers, and all lines are reserved in Inventory before the order is saved. The Idempotency-Key
/// makes a double click (or a retried request) return the same order.
/// </summary>
internal sealed class PlaceOrderHandler(
    OrdersDbContext db,
    OrderPricing pricing,
    OrderNumbers numbers,
    ICustomersApi customers,
    IInventoryApi inventory,
    TimeProvider timeProvider) : IHandler
{
    public async Task<Result<PlaceOrderResult>> HandleAsync(Guid customerId, string? idempotencyKey, PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        idempotencyKey = idempotencyKey?.Trim();
        if (idempotencyKey is not { Length: >= 8 and <= 100 })
        {
            return OrderFeatureErrors.IdempotencyKeyMissing;
        }

        var requestHash = Fingerprint(request);
        var replay = await FindReplayAsync(customerId, idempotencyKey, requestHash, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        // ---- who is ordering, and may they ----
        var customer = await customers.GetCustomerSummaryAsync(customerId, cancellationToken);
        if (customer is null)
        {
            return OrderFeatureErrors.CustomerNotFound;
        }

        if (customer.IsBlocked)
        {
            return OrderFeatureErrors.CustomerBlocked;
        }

        var settings = await db.Settings.AsNoTracking().SingleAsync(cancellationToken);
        var refused = await db.Orders.CountAsync(o => o.CustomerId == customerId && o.Status == OrderStatus.Refused, cancellationToken);
        if (refused > settings.MaxRefusedOrders)
        {
            return OrderFeatureErrors.TooManyRefusals;
        }

        var open = await db.Orders.CountAsync(o => o.CustomerId == customerId && Order.OpenStatuses.Contains(o.Status), cancellationToken);
        if (open >= settings.MaxOpenOrdersPerCustomer)
        {
            return OrderFeatureErrors.TooManyOpenOrders(settings.MaxOpenOrdersPerCustomer);
        }

        var address = request.AddressId is { } addressId
            ? await customers.GetAddressAsync(customerId, addressId, cancellationToken)
            : await customers.GetDefaultAddressAsync(customerId, cancellationToken);
        if (address is null)
        {
            return OrderFeatureErrors.AddressRequired;
        }

        var fullName = string.IsNullOrWhiteSpace(request.FullName) ? customer.FullName : request.FullName.Trim();
        if (fullName is not { Length: >= 2 and <= Order.TextMaxLength })
        {
            return OrderErrors.FullNameRequired;
        }

        if (customer.FullName is null)
        {
            var saved = await customers.SetFullNameAsync(customerId, fullName, cancellationToken);
            if (saved.IsFailure)
            {
                return saved.Error;
            }
        }

        // ---- prices (from Catalog) and stock ----
        var cart = await pricing.PriceAsync([.. request.Items.Select(i => new CartItem(i.VariantId, i.Quantity))], address.CountryCode, cancellationToken);
        if (!cart.CanCheckout)
        {
            return OrderFeatureErrors.CartNotOrderable with { Message = $"{OrderFeatureErrors.CartNotOrderable.Message} ({string.Join("; ", cart.Problems)})" };
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var orderId = Guid.CreateVersion7();
        List<OrderLineDraft> drafts = [.. cart.Lines.Select(l => new OrderLineDraft(
            Guid.CreateVersion7(), l.VariantId, l.Variant!.FragranceId, l.Variant.Sku, l.Variant.BrandName, l.Variant.FragranceName,
            l.Variant.VariantType, l.Variant.SizeValue, l.Variant.SizeUnit, l.UnitPrice!.Value, l.Quantity))];
        var confirmWithin = TimeSpan.FromHours(settings.PendingExpiryHours);

        var reserved = await inventory.ReserveAsync(
            orderId, [.. drafts.Select(d => new StockReservationLine(d.LineId, d.VariantId, d.Quantity))], now.Add(confirmWithin), cancellationToken);
        if (reserved.IsFailure)
        {
            return reserved.Error;
        }

        // ---- save (number + order + outbox in one transaction) ----
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var number = await numbers.NextAsync(now.Year, cancellationToken);
            var order = Order.Place(
                orderId, number, customerId, new CustomerSnapshot(fullName, customer.PhoneE164),
                new DeliveryAddress(address.AddressId, address.CountryCode, address.City, address.Street, address.Notes),
                drafts, cart.DeliveryFee, request.CustomerNote, idempotencyKey, requestHash, now, confirmWithin);
            if (order.IsFailure)
            {
                await ReleaseAsync(drafts);
                return order.Error;
            }

            db.Orders.Add(order.Value);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new PlaceOrderResult(order.Value.ToDto(forCustomer: true), Replayed: false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent request with the same Idempotency-Key won: give back our reservation, return its order.
            await ReleaseAsync(drafts);
            db.ChangeTracker.Clear();
            return await FindReplayAsync(customerId, idempotencyKey, requestHash, cancellationToken) ?? OrderFeatureErrors.IdempotencyKeyReused;
        }
        catch
        {
            await ReleaseAsync(drafts);
            throw;
        }
    }

    private async Task<Result<PlaceOrderResult>?> FindReplayAsync(Guid customerId, string key, string hash, CancellationToken cancellationToken)
    {
        var existing = await db.Orders.AsNoTracking().Include(o => o.Lines).Include(o => o.History)
            .FirstOrDefaultAsync(o => o.CustomerId == customerId && o.IdempotencyKey == key, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        return existing.RequestHash == hash
            ? new PlaceOrderResult(existing.ToDto(forCustomer: true), Replayed: true)
            : OrderFeatureErrors.IdempotencyKeyReused;
    }

    private Task ReleaseAsync(IEnumerable<OrderLineDraft> drafts) =>
        inventory.ReleaseAsync([.. drafts.Select(d => d.LineId)], "Order was not saved", CancellationToken.None);

    private static string Fingerprint(PlaceOrderRequest request)
    {
        var items = string.Join(";", request.Items.GroupBy(i => i.VariantId).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Sum(i => i.Quantity)}"));
        var text = $"{items}|{request.AddressId}|{request.FullName?.Trim()}|{request.CustomerNote?.Trim()}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}

internal static class PlaceOrderEndpoint
{
    public const string IdempotencyHeader = "Idempotency-Key";

    public static void Map(RouteGroupBuilder customer) =>
        customer.MapPost("/", async (
                PlaceOrderRequest request,
                [FromHeader(Name = IdempotencyHeader)] string? idempotencyKey,
                ClaimsPrincipal user,
                HttpResponse response,
                PlaceOrderHandler handler,
                CancellationToken ct) =>
            {
                var result = await handler.HandleAsync(user.GetCustomerId()!.Value, idempotencyKey, request, ct);
                if (result.IsFailure)
                {
                    return result.Error.ToProblem();
                }

                if (result.Value.Replayed)
                {
                    response.Headers["Idempotent-Replayed"] = "true";
                    return TypedResults.Ok(result.Value.Order);
                }

                return (IResult)TypedResults.Created($"/api/orders/{result.Value.Order.Id}", result.Value.Order);
            })
            .WithValidation<PlaceOrderRequest>()
            .WithName("PlaceOrder")
            .Produces<OrderDto>(StatusCodes.Status201Created);
}
