using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PerfumeShop.Modules.Customers.Domain.Common;
using PerfumeShop.Modules.Customers.Domain.Customers;
using PerfumeShop.Modules.Customers.Domain.Verification;
using PerfumeShop.Modules.Customers.Infrastructure.Identity;

namespace PerfumeShop.Modules.Customers.Infrastructure.Configurations;

internal static class CustomerConverters
{
    public static readonly ValueConverter<PhoneNumber, string> Phone =
        new(phone => phone.Value, value => PhoneNumber.Create(value).Value);
}

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Phone).HasConversion(CustomerConverters.Phone).HasMaxLength(PhoneNumber.MaxLength).IsRequired();
        builder.Property(c => c.FullName).HasMaxLength(Customer.FullNameMaxLength);
        builder.Property(c => c.Email).HasMaxLength(Customer.EmailMaxLength);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.BlockReason).HasMaxLength(Customer.BlockReasonMaxLength);
        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasMany(c => c.Addresses).WithOne().HasForeignKey(a => a.CustomerId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Addresses).HasField("_addresses").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(c => c.DefaultAddress);
        builder.Ignore(c => c.IsBlocked);
        builder.Ignore(c => c.DomainEvents);

        builder.HasIndex(c => c.Phone).IsUnique().HasDatabaseName("ux_customers_phone");
        builder.HasIndex(c => c.Email).HasDatabaseName("ix_customers_email");
        builder.HasIndex(c => c.Status).HasDatabaseName("ix_customers_status");
    }
}

internal sealed class AddressConfiguration : IEntityTypeConfiguration<Address>
{
    public void Configure(EntityTypeBuilder<Address> builder)
    {
        builder.ToTable("customer_addresses");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.CountryCode).HasMaxLength(2).IsFixedLength().HasDefaultValue(Address.DefaultCountry).IsRequired();
        builder.Property(a => a.City).HasMaxLength(Address.CityMaxLength).IsRequired();
        builder.Property(a => a.Street).HasMaxLength(Address.StreetMaxLength).IsRequired();
        builder.Property(a => a.Notes).HasMaxLength(Address.NotesMaxLength);
        builder.HasIndex(a => a.CustomerId).HasDatabaseName("ix_customer_addresses_customer_id");
        // "At most one default per customer" is a DEFERRABLE exclusion constraint added in the migration
        // (a plain partial unique index would fail while EF swaps the default between two rows).
    }
}

internal sealed class PhoneVerificationConfiguration : IEntityTypeConfiguration<PhoneVerification>
{
    public void Configure(EntityTypeBuilder<PhoneVerification> builder)
    {
        builder.ToTable("phone_verifications", t => t.HasCheckConstraint("ck_phone_verifications_attempts", "attempts >= 0 AND attempts <= max_attempts"));
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();
        builder.Property(v => v.Phone).HasConversion(CustomerConverters.Phone).HasMaxLength(PhoneNumber.MaxLength).IsRequired();
        builder.Property(v => v.CodeHash).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(v => v.IpAddress).HasMaxLength(PhoneVerification.IpAddressMaxLength);
        builder.HasIndex(v => new { v.Phone, v.CreatedAtUtc }).HasDatabaseName("ix_phone_verifications_phone_created_at_utc");
        builder.HasIndex(v => new { v.IpAddress, v.CreatedAtUtc }).HasDatabaseName("ix_phone_verifications_ip_address_created_at_utc");
    }
}

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.Property(u => u.DisplayName).HasMaxLength(100);
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(u => u.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(u => u.CustomerId).IsUnique().HasDatabaseName("ux_users_customer_id");
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens", CustomersDbContext.IdentitySchema);
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.TokenHash).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(t => t.CreatedByIp).HasMaxLength(PhoneVerification.IpAddressMaxLength);
        builder.Property(t => t.RevocationReason).HasMaxLength(30);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(t => t.TokenHash).IsUnique().HasDatabaseName("ux_refresh_tokens_token_hash");
        builder.HasIndex(t => t.UserId).HasDatabaseName("ix_refresh_tokens_user_id");
        builder.HasIndex(t => t.FamilyId).HasDatabaseName("ix_refresh_tokens_family_id");
    }
}
