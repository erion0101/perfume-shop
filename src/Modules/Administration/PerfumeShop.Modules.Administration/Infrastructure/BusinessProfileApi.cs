using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PerfumeShop.Modules.Administration.Contracts;
using PerfumeShop.Modules.Administration.Domain;

namespace PerfumeShop.Modules.Administration.Infrastructure;

internal sealed class BusinessProfileApi(AdministrationDbContext db, IMemoryCache cache) : IBusinessProfileApi
{
    internal const string CacheKey = "administration:business-profile";

    public async Task<BusinessProfileDto> GetAsync(CancellationToken cancellationToken = default) =>
        (await cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
            var profile = await db.BusinessProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken)
                ?? BusinessProfile.Seed(null, null, null, null);
            return ToDto(profile);
        }))!;

    public static BusinessProfileDto ToDto(BusinessProfile p) =>
        new(p.ShopName, p.Address, p.Phone, p.Email, p.TaxNumber, p.PlaceholderFields(), p.UpdatedAtUtc, p.UpdatedBy);
}
