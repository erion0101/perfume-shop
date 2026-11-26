using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PerfumeShop.Modules.Catalog.Domain.Fragrances;
using PerfumeShop.Shared.Infrastructure.Persistence;

namespace PerfumeShop.Modules.Catalog.Infrastructure.Configurations;

internal sealed class FragranceVariantConfiguration : IEntityTypeConfiguration<FragranceVariant>
{
    public void Configure(EntityTypeBuilder<FragranceVariant> builder)
    {
        builder.ToTable("fragrance_variants", table =>
        {
            // Mirror the domain rules in the database as a last line of defence.
            table.HasCheckConstraint("ck_fragrance_variants_price_positive", "price_amount IS NULL OR price_amount > 0");
            table.HasCheckConstraint(
                "ck_fragrance_variants_compare_at_above_price",
                "compare_at_price_amount IS NULL OR (price_amount IS NOT NULL AND compare_at_price_amount > price_amount)");
            table.HasCheckConstraint("ck_fragrance_variants_size_positive", "size_value IS NULL OR size_value > 0");
            table.HasCheckConstraint(
                "ck_fragrance_variants_size_complete",
                "(size_value IS NULL) = (size_unit IS NULL)");
            table.HasCheckConstraint("ck_fragrance_variants_active_requires_price", "NOT is_active OR price_amount IS NOT NULL");
        });

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();

        builder.Property(v => v.Sku).HasConversion(CatalogConverters.Sku).HasMaxLength(Sku.MaxLength).IsRequired();
        builder.Property(v => v.Ean).HasConversion(CatalogConverters.Ean).HasMaxLength(Ean.MaxLength);
        builder.Property(v => v.Type).HasConversion<string>().HasMaxLength(30);
        builder.ComplexProperty(v => v.Size, size =>
        {
            size.Property(s => s.Value).HasColumnName("size_value").HasPrecision(6, 1);
            size.Property(s => s.Unit).HasColumnName("size_unit").HasConversion<string>().HasMaxLength(10);
        });
        builder.Property(v => v.IsActive);
        builder.Property(v => v.Images).HasJsonbConversion().IsRequired();

        builder.ComplexProperty(v => v.Price, price =>
        {
            price.Property(m => m.Amount).HasColumnName("price_amount").HasPrecision(10, 2);
            price.Property(m => m.Currency).HasColumnName("price_currency").HasMaxLength(3).IsFixedLength();
        });
        builder.ComplexProperty(v => v.CompareAtPrice, price =>
        {
            price.Property(m => m.Amount).HasColumnName("compare_at_price_amount").HasPrecision(10, 2);
            price.Property(m => m.Currency).HasColumnName("compare_at_price_currency").HasMaxLength(3).IsFixedLength();
        });

        builder.HasIndex(v => v.Sku).IsUnique().HasDatabaseName("ux_fragrance_variants_sku");
        builder.HasIndex(v => v.Ean).IsUnique().HasDatabaseName("ux_fragrance_variants_ean");
        builder.HasIndex(v => v.FragranceId).HasDatabaseName("ix_fragrance_variants_fragrance_id");
    }
}
