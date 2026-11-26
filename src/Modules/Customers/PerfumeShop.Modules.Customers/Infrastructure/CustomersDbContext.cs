using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Customers.Domain.Customers;
using PerfumeShop.Modules.Customers.Domain.Verification;
using PerfumeShop.Modules.Customers.Infrastructure.Identity;

namespace PerfumeShop.Modules.Customers.Infrastructure;

/// <summary>
/// The Customers module's only DbContext. It owns two schemas: "customers" (profiles, addresses,
/// OTP codes) and "identity" (ASP.NET Identity + refresh tokens). One context means creating a
/// customer and its login on first OTP verification is a single transaction.
/// </summary>
internal sealed class CustomersDbContext(DbContextOptions<CustomersDbContext> options)
    : IdentityDbContext<AppUser, AppRole, Guid>(options)
{
    internal const string SchemaName = "customers";
    internal const string IdentitySchema = "identity";

    public DbSet<Customer> Customers => Set<Customer>();

    public IQueryable<Address> Addresses => Set<Address>();

    public DbSet<PhoneVerification> PhoneVerifications => Set<PhoneVerification>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(SchemaName);

        // Identity tables: "AspNetUserRoles" → identity.user_roles, etc.
        var identityEntities = builder.Model.GetEntityTypes()
            .Where(e => e.ClrType.Namespace?.StartsWith("Microsoft.AspNetCore.Identity", StringComparison.Ordinal) == true
                || e.ClrType == typeof(AppUser) || e.ClrType == typeof(AppRole))
            .ToList();
        foreach (var entity in identityEntities)
        {
            var name = entity.GetTableName()!.Replace("AspNet", string.Empty, StringComparison.Ordinal);
            entity.SetSchema(IdentitySchema);
            entity.SetTableName(ToSnakeCase(name));
        }

        // Foreign keys were named before the rename ("fk_user_roles_asp_net_roles_role_id"); rename them too.
        foreach (var foreignKey in identityEntities.SelectMany(e => e.GetForeignKeys()))
        {
            var columns = string.Join('_', foreignKey.Properties.Select(p => ToSnakeCase(p.Name)));
            foreignKey.SetConstraintName($"fk_{foreignKey.DeclaringEntityType.GetTableName()}_{foreignKey.PrincipalEntityType.GetTableName()}_{columns}");
        }

        builder.ApplyConfigurationsFromAssembly(typeof(CustomersDbContext).Assembly);
    }

    private static string ToSnakeCase(string name) =>
        string.Concat(name.Select((ch, i) => i > 0 && char.IsUpper(ch) ? "_" + char.ToLowerInvariant(ch) : char.ToLowerInvariant(ch).ToString()));
}
