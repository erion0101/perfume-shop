using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PerfumeShop.Modules.Customers.Domain.Customers;
using PerfumeShop.Modules.Customers.Features.Shared;
using PerfumeShop.Modules.Customers.Infrastructure;
using PerfumeShop.Shared.Infrastructure.Auth;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Customers.Features.ManageMyAddresses;

/// <param name="IsDefault">The first address is always the default.</param>
/// <param name="CountryCode">XK (default), AL or MK.</param>
internal sealed record AddAddressRequest(string City, string Street, string? Notes, bool IsDefault = false, string? CountryCode = null);

internal sealed record UpdateAddressRequest(string City, string Street, string? Notes, string? CountryCode = null);

internal sealed class AddAddressValidator : AbstractValidator<AddAddressRequest>
{
    public AddAddressValidator()
    {
        RuleFor(x => x.City).NotEmpty().MaximumLength(Address.CityMaxLength);
        RuleFor(x => x.Street).NotEmpty().MaximumLength(Address.StreetMaxLength);
        RuleFor(x => x.Notes).MaximumLength(Address.NotesMaxLength);
    }
}

internal sealed class UpdateAddressValidator : AbstractValidator<UpdateAddressRequest>
{
    public UpdateAddressValidator()
    {
        RuleFor(x => x.City).NotEmpty().MaximumLength(Address.CityMaxLength);
        RuleFor(x => x.Street).NotEmpty().MaximumLength(Address.StreetMaxLength);
        RuleFor(x => x.Notes).MaximumLength(Address.NotesMaxLength);
    }
}

/// <summary>Address CRUD for the signed-in customer. Every change goes through the Customer aggregate.</summary>
internal sealed class ManageMyAddressesHandler(CustomersDbContext db) : IHandler
{
    public async Task<Result<IReadOnlyList<AddressDto>>> ListAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var customer = await db.LoadActiveAsync(customerId, cancellationToken);
        return customer.IsSuccess ? Result<IReadOnlyList<AddressDto>>.Success(customer.Value.AddressDtos()) : customer.Error;
    }

    public async Task<Result<AddressDto>> AddAsync(Guid customerId, AddAddressRequest request, CancellationToken cancellationToken)
    {
        var customer = await db.LoadActiveAsync(customerId, cancellationToken);
        if (customer.IsFailure)
        {
            return customer.Error;
        }

        var address = customer.Value.AddAddress(request.City, request.Street, request.Notes, request.IsDefault, request.CountryCode);
        if (address.IsFailure)
        {
            return address.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return address.Value.ToDto();
    }

    public Task<Result<IReadOnlyList<AddressDto>>> UpdateAsync(Guid customerId, Guid addressId, UpdateAddressRequest request, CancellationToken cancellationToken) =>
        ChangeAsync(customerId, c => c.UpdateAddress(addressId, request.City, request.Street, request.Notes, request.CountryCode), cancellationToken);

    public Task<Result<IReadOnlyList<AddressDto>>> SetDefaultAsync(Guid customerId, Guid addressId, CancellationToken cancellationToken) =>
        ChangeAsync(customerId, c => c.SetDefaultAddress(addressId), cancellationToken);

    public Task<Result<IReadOnlyList<AddressDto>>> DeleteAsync(Guid customerId, Guid addressId, CancellationToken cancellationToken) =>
        ChangeAsync(customerId, c => c.RemoveAddress(addressId), cancellationToken);

    private async Task<Result<IReadOnlyList<AddressDto>>> ChangeAsync(Guid customerId, Func<Customer, Result> change, CancellationToken cancellationToken)
    {
        var customer = await db.LoadActiveAsync(customerId, cancellationToken);
        if (customer.IsFailure)
        {
            return customer.Error;
        }

        var result = change(customer.Value);
        if (result.IsFailure)
        {
            return result.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result<IReadOnlyList<AddressDto>>.Success(customer.Value.AddressDtos());
    }
}

internal static class ManageMyAddressesEndpoint
{
    public static void Map(RouteGroupBuilder me)
    {
        me.MapGet("/addresses", async (ClaimsPrincipal user, ManageMyAddressesHandler handler, CancellationToken ct) =>
                (await handler.ListAsync(user.GetCustomerId()!.Value, ct)).ToOk())
            .WithName("ListMyAddresses");

        me.MapPost("/addresses", async (ClaimsPrincipal user, AddAddressRequest request, ManageMyAddressesHandler handler, CancellationToken ct) =>
                (await handler.AddAsync(user.GetCustomerId()!.Value, request, ct)).ToCreated(a => $"/api/customers/me/addresses/{a.Id}"))
            .WithValidation<AddAddressRequest>()
            .WithName("AddMyAddress")
            .Produces<AddressDto>(StatusCodes.Status201Created);

        me.MapPut("/addresses/{addressId:guid}", async (ClaimsPrincipal user, Guid addressId, UpdateAddressRequest request, ManageMyAddressesHandler handler, CancellationToken ct) =>
                (await handler.UpdateAsync(user.GetCustomerId()!.Value, addressId, request, ct)).ToOk())
            .WithValidation<UpdateAddressRequest>()
            .WithName("UpdateMyAddress");

        me.MapPost("/addresses/{addressId:guid}/default", async (ClaimsPrincipal user, Guid addressId, ManageMyAddressesHandler handler, CancellationToken ct) =>
                (await handler.SetDefaultAsync(user.GetCustomerId()!.Value, addressId, ct)).ToOk())
            .WithName("SetMyDefaultAddress");

        me.MapDelete("/addresses/{addressId:guid}", async (ClaimsPrincipal user, Guid addressId, ManageMyAddressesHandler handler, CancellationToken ct) =>
                (await handler.DeleteAsync(user.GetCustomerId()!.Value, addressId, ct)).ToOk())
            .WithName("DeleteMyAddress");
    }
}
