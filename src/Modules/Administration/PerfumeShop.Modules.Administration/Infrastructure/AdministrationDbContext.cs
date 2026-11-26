using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PerfumeShop.Modules.Administration.Domain;
using PerfumeShop.Shared.Infrastructure.Persistence;

namespace PerfumeShop.Modules.Administration.Infrastructure;

internal sealed class AdministrationDbContext(DbContextOptions<AdministrationDbContext> options) : ModuleDbContext(options)
{
    internal const string SchemaName = "administration";

    public DbSet<BusinessProfile> BusinessProfiles => Set<BusinessProfile>();

    public DbSet<AuditRecord> AuditRecords => Set<AuditRecord>();

    protected override string Schema => SchemaName;
}

internal sealed class BusinessProfileConfiguration : IEntityTypeConfiguration<BusinessProfile>
{
    public void Configure(EntityTypeBuilder<BusinessProfile> builder)
    {
        builder.ToTable("business_profile", t => t.HasCheckConstraint("ck_business_profile_singleton", "id = 1"));
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.ShopName).HasMaxLength(100);
        builder.Property(p => p.Address).HasMaxLength(200);
        builder.Property(p => p.Phone).HasMaxLength(30);
        builder.Property(p => p.Email).HasMaxLength(200);
        builder.Property(p => p.TaxNumber).HasMaxLength(20);
        builder.Property(p => p.UpdatedBy).HasMaxLength(200);
        builder.Property<uint>("xmin").IsRowVersion();
    }
}

internal sealed class AuditRecordConfiguration : IEntityTypeConfiguration<AuditRecord>
{
    public void Configure(EntityTypeBuilder<AuditRecord> builder)
    {
        builder.ToTable("audit_entries");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.ActorId).HasMaxLength(100);
        builder.Property(a => a.ActorEmail).HasMaxLength(200);
        builder.Property(a => a.Action).HasMaxLength(100);
        builder.Property(a => a.EntityType).HasMaxLength(50);
        builder.Property(a => a.EntityId).HasMaxLength(100);
        builder.Property(a => a.Summary).HasMaxLength(500);
        builder.Property(a => a.Changes).HasColumnType("jsonb");
        builder.Property(a => a.IpAddress).HasMaxLength(64);
        builder.HasIndex(a => a.OccurredAtUtc).HasDatabaseName("ix_audit_entries_occurred_at_utc");
        builder.HasIndex(a => new { a.Action, a.OccurredAtUtc }).HasDatabaseName("ix_audit_entries_action_occurred_at_utc");
        builder.HasIndex(a => new { a.EntityType, a.EntityId }).HasDatabaseName("ix_audit_entries_entity");
        builder.HasIndex(a => a.ActorId).HasDatabaseName("ix_audit_entries_actor_id");
    }
}
