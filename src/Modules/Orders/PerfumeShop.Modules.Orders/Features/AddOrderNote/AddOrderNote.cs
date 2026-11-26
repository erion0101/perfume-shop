using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PerfumeShop.Modules.Orders.Features.Shared;
using PerfumeShop.Modules.Orders.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Orders.Features.AddOrderNote;

internal sealed record AddOrderNoteRequest(string Text);

internal sealed class AddOrderNoteValidator : AbstractValidator<AddOrderNoteRequest>
{
    public AddOrderNoteValidator() => RuleFor(x => x.Text).NotEmpty().MaximumLength(1000);
}

/// <summary>Internal note for staff (e.g. "Klienti kërkoi dorëzim pas orës 17"). Never shown to the customer.</summary>
internal sealed class AddOrderNoteHandler(OrdersDbContext db, TimeProvider timeProvider) : IHandler
{
    public async Task<Result<OrderAdminDto>> HandleAsync(Guid id, ClaimsPrincipal user, AddOrderNoteRequest request, CancellationToken cancellationToken)
    {
        var order = await db.LoadOrderAsync(id, cancellationToken);
        if (order.IsFailure)
        {
            return order.Error;
        }

        var added = order.Value.AddNote(user.StaffActor(), request.Text, timeProvider.GetUtcNow().UtcDateTime);
        if (added.IsFailure)
        {
            return added.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return order.Value.ToAdminDto();
    }
}

internal static class AddOrderNoteEndpoint
{
    public static void Map(RouteGroupBuilder staff) =>
        staff.MapPost("/{id:guid}/notes", async (Guid id, AddOrderNoteRequest request, ClaimsPrincipal user, AddOrderNoteHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(id, user, request, ct)).ToOk())
            .WithValidation<AddOrderNoteRequest>()
            .WithName("AddOrderNote");
}
