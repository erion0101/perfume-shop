using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PerfumeShop.Modules.Catalog.Domain.Brands;
using PerfumeShop.Modules.Catalog.Domain.Common;

namespace PerfumeShop.Modules.Catalog.Infrastructure.Configurations;

internal sealed class BrandConfiguration : IEntityTypeConfiguration<Brand>
{
    public void Configure(EntityTypeBuilder<Brand> builder)
    {
        builder.ToTable("brands");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();

        builder.Property(b => b.Name).HasMaxLength(Brand.NameMaxLength).IsRequired();
        builder.Property(b => b.Slug).HasConversion(CatalogConverters.Slug).HasMaxLength(Slug.MaxLength).IsRequired();
        builder.Property(b => b.CountryCode).HasMaxLength(2).IsFixedLength();
        builder.Property(b => b.LogoUrl).HasMaxLength(HttpUrl.MaxLength);
        builder.Property(b => b.Description);

        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasIndex(b => b.Slug).IsUnique().HasDatabaseName("ux_brands_slug");
        builder.HasIndex(b => b.Name).IsUnique().HasDatabaseName("ux_brands_name");
        builder.HasIndex(b => b.Name, "ix_brands_name_trgm").HasMethod("gin").HasOperators("gin_trgm_ops");

        builder.Ignore(b => b.DomainEvents);
    }
}
