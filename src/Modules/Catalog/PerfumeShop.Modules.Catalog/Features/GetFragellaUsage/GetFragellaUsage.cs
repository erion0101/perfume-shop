using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PerfumeShop.Modules.Catalog.Infrastructure.Fragella;

namespace PerfumeShop.Modules.Catalog.Features.GetFragellaUsage;

internal static class GetFragellaUsageEndpoint
{
    public static void Map(RouteGroupBuilder admin) =>
        admin.MapGet("/fragella/usage", async (IFragellaBudget budget, CancellationToken ct) =>
                TypedResults.Ok(await budget.GetUsageAsync(ct)))
            .WithName("GetFragellaUsage")
            .WithDescription("Fragella requests used this month against Fragella:MonthlyRequestLimit.");
}
