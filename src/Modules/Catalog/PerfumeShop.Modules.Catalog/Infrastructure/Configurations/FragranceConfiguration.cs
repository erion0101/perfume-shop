using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PerfumeShop.Modules.Catalog.Domain.Brands;
using PerfumeShop.Modules.Catalog.Domain.Common;
using PerfumeShop.Modules.Catalog.Domain.Fragrances;
using PerfumeShop.Shared.Infrastructure.Persistence;

namespace PerfumeShop.Modules.Catalog.Infrastructure.Configurations;

internal sealed class FragranceConfiguration : IEntityTypeConfiguration<Fragrance>
{
    private const int EnumMaxLength = 30;

    public void Configure(EntityTypeBuilder<Fragrance> builder)
    {
        builder.ToTable("fragrances");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedNever();

        builder.Property(f => f.Name).HasMaxLength(Fragrance.NameMaxLength).IsRequired();
        builder.Property(f => f.Slug).HasConversion(CatalogConverters.Slug).HasMaxLength(Slug.MaxLength).IsRequired();
        builder.Property(f => f.Concentration).HasConversion<string>().HasMaxLength(EnumMaxLength);
        builder.Property(f => f.Gender).HasConversion<string>().HasMaxLength(EnumMaxLength);
        builder.Property(f => f.OlfactiveFamily).HasConversion<string>().HasMaxLength(EnumMaxLength);
        builder.Property(f => f.Longevity).HasConversion<string>().HasMaxLength(EnumMaxLength);
        builder.Property(f => f.Sillage).HasConversion<string>().HasMaxLength(EnumMaxLength);
        builder.Property(f => f.Status).HasConversion<string>().HasMaxLength(EnumMaxLength);
        builder.Property(f => f.Seasons);
        builder.Property(f => f.Occasions);
        builder.Property(f => f.LaunchYear);
        builder.Property(f => f.Description);

        // text[]; the domain exposes it read-only.
        builder.Ignore(f => f.Perfumers);
        builder.Property<List<string>>("_perfumers").HasColumnName("perfumers").IsRequired();

        builder.Property(f => f.Notes).HasJsonbConversion().IsRequired();
        builder.Property(f => f.Accords).HasJsonbConversion().IsRequired();
        builder.Property(f => f.SimilarTo).HasJsonbConversion().IsRequired();

        builder.Property(f => f.FragellaId).HasMaxLength(Fragrance.FragellaIdMaxLength);
        builder.Property(f => f.FragellaRaw).HasColumnType("jsonb");
        builder.Property(f => f.FragellaImportedAtUtc);

        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasOne<Brand>()
            .WithMany()
            .HasForeignKey(f => f.BrandId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(f => f.Variants)
            .WithOne()
            .HasForeignKey(v => v.FragranceId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(f => f.Variants).HasField("_variants").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(f => f.Slug).IsUnique().HasDatabaseName("ux_fragrances_slug");
        // Also serves brand_id lookups (leading column), so no separate brand_id index.
        builder.HasIndex(f => new { f.BrandId, f.Name, f.Concentration })
            .IsUnique()
            .HasDatabaseName("ux_fragrances_brand_id_name_concentration");
        builder.HasIndex(f => f.FragellaId).IsUnique().HasDatabaseName("ux_fragrances_fragella_id");
        builder.HasIndex(f => f.Status).HasDatabaseName("ix_fragrances_status");
        builder.HasIndex(f => f.Notes)
            .HasMethod("gin")
            .HasOperators("jsonb_path_ops")
            .HasDatabaseName("ix_fragrances_notes");

        builder.Ignore(f => f.DomainEvents);
    }
}
