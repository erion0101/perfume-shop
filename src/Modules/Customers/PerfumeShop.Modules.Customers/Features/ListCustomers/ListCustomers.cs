using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Customers.Domain.Common;
using PerfumeShop.Modules.Customers.Domain.Customers;
using PerfumeShop.Modules.Customers.Infrastructure;
using PerfumeShop.Modules.Orders.Contracts;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Modules;

namespace PerfumeShop.Modules.Customers.Features.ListCustomers;

/// <param name="Search">A phone number (any format, exact match) or part of the name/email.</param>
/// <param name="Status">Active or Blocked.</param>
/// <param name="HasRefusals">true: only customers with at least one refused parcel.</param>
internal sealed record ListCustomersQuery(string? Search, string? Status, bool? HasRefusals, int Page = 1, int PageSize = 25);

internal sealed record CustomerListItemDto(
    Guid Id,
    string Phone,
    string? FullName,
    string? Email,
    string Status,
    int AddressCount,
    DateTime CreatedAtUtc,
    DateTime? LastLoginAtUtc,
    int OrderCount,
    int RefusedCount);

internal sealed class ListCustomersValidator : AbstractValidator<ListCustomersQuery>
{
    public ListCustomersValidator()
    {
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.Status).IsEnumName(typeof(CustomerStatus), caseSensitive: false).When(x => x.Status is not null);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

internal sealed class ListCustomersHandler(CustomersDbContext db, IOrdersApi orders) : IHandler
{
    public async Task<PagedResponse<CustomerListItemDto>> HandleAsync(ListCustomersQuery query, CancellationToken cancellationToken)
    {
        var customers = db.Customers.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var phone = PhoneNumber.Create(query.Search);
            if (phone.IsSuccess)
            {
                customers = customers.Where(c => c.Phone == phone.Value);
            }
            else
            {
                var term = query.Search.Trim().ToLowerInvariant();
                customers = customers.Where(c => (c.FullName != null && c.FullName.ToLower().Contains(term)) || (c.Email != null && c.Email.Contains(term)));
            }
        }

        if (Enum.TryParse<CustomerStatus>(query.Status, ignoreCase: true, out var status))
        {
            customers = customers.Where(c => c.Status == status);
        }

        if (query.HasRefusals == true)
        {
            var refusing = (await orders.GetCustomerIdsWithRefusalsAsync(cancellationToken)).ToList();
            customers = customers.Where(c => refusing.Contains(c.Id));
        }

        var total = await customers.CountAsync(cancellationToken);
        var rows = await customers
            .OrderByDescending(c => c.CreatedAtUtc)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(c => new { c.Id, c.Phone, c.FullName, c.Email, c.Status, AddressCount = c.Addresses.Count, c.CreatedAtUtc, c.LastLoginAtUtc })
            .ToListAsync(cancellationToken);

        var stats = await orders.GetCustomerOrderStatsAsync([.. rows.Select(r => r.Id)], cancellationToken);
        return new PagedResponse<CustomerListItemDto>(
            [.. rows.Select(r => new CustomerListItemDto(r.Id, r.Phone.Value, r.FullName, r.Email, r.Status.ToString(), r.AddressCount, r.CreatedAtUtc, r.LastLoginAtUtc,
                stats.GetValueOrDefault(r.Id)?.Orders ?? 0, stats.GetValueOrDefault(r.Id)?.Refused ?? 0))],
            query.Page,
            query.PageSize,
            total);
    }
}

internal static class ListCustomersEndpoint
{
    public static void Map(RouteGroupBuilder admin) =>
        admin.MapGet("/", async ([AsParameters] ListCustomersQuery query, ListCustomersHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(query, ct)))
            .WithValidation<ListCustomersQuery>()
            .WithName("ListCustomers");
}
