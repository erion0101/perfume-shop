using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PerfumeShop.Modules.Catalog.Infrastructure.Fragella;

namespace PerfumeShop.Modules.Catalog.Infrastructure.Configurations;

internal sealed class FragellaUsageConfiguration : IEntityTypeConfiguration<FragellaUsage>
{
    public void Configure(EntityTypeBuilder<FragellaUsage> builder)
    {
        builder.ToTable("fragella_usage", t => t.HasCheckConstraint("ck_fragella_usage_count", "request_count >= 0"));
        builder.HasKey(u => u.Month);
        builder.Property(u => u.Month).ValueGeneratedNever();
    }
}

internal sealed class FragellaSearchCacheConfiguration : IEntityTypeConfiguration<FragellaSearchCacheEntry>
{
    public void Configure(EntityTypeBuilder<FragellaSearchCacheEntry> builder)
    {
        builder.ToTable("fragella_search_cache");
        builder.HasKey(c => c.Query);
        builder.Property(c => c.Query).HasMaxLength(100);
        builder.Property(c => c.FragellaIds).IsRequired();
        builder.HasIndex(c => c.ExpiresAtUtc).HasDatabaseName("ix_fragella_search_cache_expires_at_utc");
    }
}

internal sealed class FragellaFragranceSnapshotConfiguration : IEntityTypeConfiguration<FragellaFragranceSnapshot>
{
    public void Configure(EntityTypeBuilder<FragellaFragranceSnapshot> builder)
    {
        builder.ToTable("fragella_fragrances");
        builder.HasKey(s => s.FragellaId);
        builder.Property(s => s.FragellaId).HasMaxLength(100);
        builder.Property(s => s.Name).HasMaxLength(300);
        builder.Property(s => s.Brand).HasMaxLength(200);
        builder.Property(s => s.Raw).HasColumnType("jsonb");
    }
}
