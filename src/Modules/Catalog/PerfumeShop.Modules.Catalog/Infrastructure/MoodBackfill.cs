using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PerfumeShop.Modules.Catalog.Infrastructure;

/// <summary>
/// Fragrances saved before moods existed have none. Derive it once on startup (a few hundred rows at most);
/// rows without accords and family stay empty and are cheap to look at again.
/// </summary>
internal static class MoodBackfill
{
    public static async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var missing = await db.Fragrances.Where(f => f.Mood == null).ToListAsync(cancellationToken);
        foreach (var fragrance in missing)
        {
            fragrance.RefreshMood();
        }

        var updated = missing.Count(f => f.Mood != null);
        if (updated > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(MoodBackfill))
                .LogInformation("Derived the shop mood for {Count} fragrances", updated);
        }
    }
}
