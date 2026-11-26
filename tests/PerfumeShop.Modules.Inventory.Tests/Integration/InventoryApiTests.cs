using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PerfumeShop.Modules.Inventory.Contracts;
using PerfumeShop.Modules.Inventory.Features.TraceBatch;
using PerfumeShop.Modules.Inventory.Infrastructure;

namespace PerfumeShop.Modules.Inventory.Tests.Integration;

/// <summary>The contract Orders will use, against real PostgreSQL.</summary>
public sealed class InventoryApiTests(InventoryApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<T> With<T>(Func<IInventoryApi, Task<T>> action)
    {
        await using var scope = factory.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IInventoryApi>());
    }

    private Task<StockAvailabilityDto> Availability(Guid variantId) => With(api => api.GetAvailabilityAsync(variantId, Ct));

    [Fact]
    public async Task Reserve_commit_and_release_are_idempotent_per_order_line()
    {
        var variantId = await factory.VariantWithStockAsync(5);
        var orderId = Guid.NewGuid();
        var line = new StockReservationLine(Guid.NewGuid(), variantId, 2);

        (await With(api => api.ReserveAsync(orderId, [line], null, Ct))).Ok();
        (await With(api => api.ReserveAsync(orderId, [line], null, Ct))).Ok();
        Assert.Equal(3, (await Availability(variantId)).Available);

        var first = await With(api => api.CommitAsync([line.OrderLineId], Ct));
        var again = await With(api => api.CommitAsync([line.OrderLineId], Ct));
        Assert.Equal(first.Ok().Single().Batches, again.Ok().Single().Batches);
        Assert.Equal(3, (await Availability(variantId)).Available);

        (await With(api => api.ReleaseAsync([line.OrderLineId], "Parcel refused", Ct))).Ok();
        (await With(api => api.ReleaseAsync([line.OrderLineId], "Parcel refused", Ct))).Ok();
        Assert.Equal(5, (await Availability(variantId)).Available);
    }

    [Fact]
    public async Task Same_order_line_with_different_quantity_is_a_conflict()
    {
        var variantId = await factory.VariantWithStockAsync(5);
        var lineId = Guid.NewGuid();
        (await With(api => api.ReserveAsync(Guid.NewGuid(), [new(lineId, variantId, 1)], null, Ct))).Ok();

        var result = await With(api => api.ReserveAsync(Guid.NewGuid(), [new(lineId, variantId, 2)], null, Ct));

        result.Fails(InventoryErrorCodes.ReservationMismatch);
    }

    [Fact]
    public async Task An_order_reserves_all_lines_or_none()
    {
        var plenty = await factory.VariantWithStockAsync(10);
        var scarce = await factory.VariantWithStockAsync(1);

        var result = await With(api => api.ReserveAsync(Guid.NewGuid(),
            [new(Guid.NewGuid(), plenty, 3), new(Guid.NewGuid(), scarce, 2)], null, Ct));

        result.Fails(InventoryErrorCodes.InsufficientStock);
        Assert.Equal(10, (await Availability(plenty)).Available);
        Assert.Equal(1, (await Availability(scarce)).Available);
    }

    [Fact]
    public async Task Two_orders_can_never_reserve_the_last_bottle()
    {
        var variantId = await factory.VariantWithStockAsync(1);

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            With(api => api.ReserveAsync(Guid.NewGuid(), [new(Guid.NewGuid(), variantId, 1)], null, Ct))));

        Assert.Single(results, r => r.IsSuccess);
        Assert.All(results.Where(r => r.IsFailure), r => Assert.Equal(InventoryErrorCodes.InsufficientStock, r.Error.Code));
        var availability = await Availability(variantId);
        Assert.Equal(0, availability.Available);

        await using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var item = await db.StockItems.AsNoTracking().SingleAsync(i => i.Id == variantId, Ct);
        Assert.Equal((1, 1), (item.OnHand, item.Reserved));
        Assert.Equal(1, await db.Reservations.CountAsync(r => r.VariantId == variantId, Ct));
    }

    [Fact]
    public async Task Concurrent_retries_of_the_same_order_line_reserve_once()
    {
        var variantId = await factory.VariantWithStockAsync(10);
        var line = new StockReservationLine(Guid.NewGuid(), variantId, 2);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            With(api => api.ReserveAsync(Guid.NewGuid(), [line], null, Ct))));

        Assert.All(results, r => r.Ok());
        Assert.Equal(8, (await Availability(variantId)).Available);
    }

    [Fact]
    public async Task Commit_consumes_batches_oldest_first_and_trace_shows_the_order()
    {
        var variantId = await factory.VariantWithStockAsync(2, 5); // two receipts on consecutive days
        var orderId = Guid.NewGuid();
        var line = new StockReservationLine(Guid.NewGuid(), variantId, 3);
        (await With(api => api.ReserveAsync(orderId, [line], null, Ct))).Ok();

        var committed = (await With(api => api.CommitAsync([line.OrderLineId], Ct))).Ok().Single();

        Assert.Equal([2, 1], committed.Batches.Select(b => b.Quantity));
        var oldestCode = committed.Batches[0].BatchCode;
        var trace = await (await factory.CreateAdminClient().Get($"/api/admin/inventory/batches?code={oldestCode}")).Read<List<BatchTraceDto>>();
        var shipment = Assert.Single(Assert.Single(trace).ShippedTo);
        Assert.Equal((orderId, 2, "Committed"), (shipment.OrderId, shipment.Quantity, shipment.Status));
        Assert.Null(shipment.OrderNumber); // not a real order in Orders: no number to show
    }

    [Fact]
    public async Task Commit_of_an_unknown_line_fails_and_release_of_an_unknown_line_is_ignored()
    {
        (await With(api => api.CommitAsync([Guid.NewGuid()], Ct))).Fails(InventoryErrorCodes.ReservationNotFound);
        (await With(api => api.ReleaseAsync([Guid.NewGuid()], "cancelled", Ct))).Ok();
    }

    [Fact]
    public async Task Expired_reservations_stop_blocking_stock()
    {
        var variantId = await factory.VariantWithStockAsync(1);
        var expired = new StockReservationLine(Guid.NewGuid(), variantId, 1);
        (await With(api => api.ReserveAsync(Guid.NewGuid(), [expired], DateTime.UtcNow.AddMinutes(-1), Ct))).Ok();
        Assert.Equal(0, (await Availability(variantId)).Available);

        // The next order for this variant releases the lapsed hold first.
        var next = await With(api => api.ReserveAsync(Guid.NewGuid(), [new(Guid.NewGuid(), variantId, 1)], null, Ct));

        next.Ok();
        (await With(api => api.CommitAsync([expired.OrderLineId], Ct))).Fails(InventoryErrorCodes.ReservationNotActive);
    }

    [Fact]
    public async Task Sweeper_releases_expired_holds_but_not_confirmed_ones()
    {
        var variantId = await factory.VariantWithStockAsync(2);
        var lapsed = new StockReservationLine(Guid.NewGuid(), variantId, 1);
        var confirmed = new StockReservationLine(Guid.NewGuid(), variantId, 1);
        var soon = DateTime.UtcNow.AddMinutes(30);
        (await With(api => api.ReserveAsync(Guid.NewGuid(), [lapsed, confirmed], soon, Ct))).Ok();
        (await With(api => api.ConfirmAsync([confirmed.OrderLineId], Ct))).Ok();

        await using var scope = factory.CreateScope();
        var clock = new FixedTimeProvider(DateTimeOffset.UtcNow.AddHours(1));
        var sweeper = ActivatorUtilities.CreateInstance<StockReservationSweeper>(scope.ServiceProvider, clock);
        await sweeper.ReleaseExpiredAsync(500, Ct);

        Assert.Equal(1, (await Availability(variantId)).Available);
        (await With(api => api.CommitAsync([confirmed.OrderLineId], Ct))).Ok();
    }

    [Fact]
    public async Task Availability_for_many_variants_includes_unknown_ones_as_zero()
    {
        var stocked = await factory.VariantWithStockAsync(4);
        var never = Guid.NewGuid();

        var availability = await With(api => api.GetAvailabilityAsync([stocked, never], Ct));

        Assert.True(availability[stocked].InStock);
        Assert.Equal(new StockAvailabilityDto(never, 0, false, false), availability[never]);
    }

    [Fact]
    public async Task The_stock_ledger_is_append_only_in_the_database()
    {
        var variantId = await factory.VariantWithStockAsync(1);
        await using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var update = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlAsync($"UPDATE inventory.stock_movements SET quantity = 99 WHERE variant_id = {variantId}", Ct));
        var delete = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlAsync($"DELETE FROM inventory.stock_movements WHERE variant_id = {variantId}", Ct));

        Assert.Contains("append-only", update.MessageText, StringComparison.Ordinal);
        Assert.Contains("append-only", delete.MessageText, StringComparison.Ordinal);
    }
}
