using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PerfumeShop.Modules.Shipping.Domain;
using PerfumeShop.Shared.Infrastructure.Persistence;

namespace PerfumeShop.Modules.Shipping.Infrastructure.Configurations;

internal sealed class CourierConfiguration : IEntityTypeConfiguration<Courier>
{
    public void Configure(EntityTypeBuilder<Courier> builder)
    {
        builder.ToTable("couriers", t => t.HasCheckConstraint("ck_couriers_fees",
            "delivery_fee >= 0 AND cod_fee_fixed >= 0 AND cod_fee_percent >= 0 AND cod_fee_percent <= 10 AND remittance_days >= 1"));
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.Property(c => c.ContactName).HasMaxLength(100);
        builder.Property(c => c.ContactPhone).HasMaxLength(30);
        builder.Property(c => c.ContactEmail).HasMaxLength(254);
        builder.Ignore(c => c.EnabledCountries);
        builder.Property<List<string>>("_countries").HasColumnName("enabled_countries").IsRequired();
        builder.Property(c => c.DeliveryFee).HasPrecision(10, 2);
        builder.Property(c => c.CodFeeFixed).HasPrecision(10, 2);
        builder.Property(c => c.CodFeePercent).HasPrecision(5, 2);
        builder.Property(c => c.RemittanceSchedule).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Notes).HasMaxLength(1000);
        builder.Property(c => c.AdapterCode).HasMaxLength(30);
        builder.Property<uint>("xmin").IsRowVersion();
        builder.Ignore(c => c.DomainEvents);
        builder.HasIndex(c => c.Name).IsUnique().HasDatabaseName("ux_couriers_name");
    }
}

internal sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        builder.ToTable("shipments", t =>
        {
            t.HasCheckConstraint("ck_shipments_parcels", "parcel_count BETWEEN 1 AND 10 AND (weight_grams IS NULL OR weight_grams > 0)");
            t.HasCheckConstraint("ck_shipments_cod", "cod_amount > 0");
        });
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.OrderNumber).HasMaxLength(20).IsRequired();
        builder.Property(s => s.CourierName).HasMaxLength(100).IsRequired();
        builder.Property(s => s.TrackingNumber).HasMaxLength(Shipment.TextMaxLength).IsRequired();
        builder.Property(s => s.CodAmount).HasPrecision(10, 2);
        builder.Property(s => s.CollectedAmount).HasPrecision(10, 2);
        builder.Property(s => s.CodFee).HasPrecision(10, 2);
        builder.Property(s => s.Note).HasMaxLength(500);
        builder.Property(s => s.ReceiptOverrideReason).HasMaxLength(500);
        builder.Property(s => s.ReceiptOverrideBy).HasMaxLength(100);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(30);
        builder.ComplexProperty(s => s.Recipient, r =>
        {
            r.Property(x => x.FullName).HasColumnName("recipient_full_name").HasMaxLength(100);
            r.Property(x => x.Phone).HasColumnName("recipient_phone").HasMaxLength(16);
            r.Property(x => x.CountryCode).HasColumnName("recipient_country_code").HasMaxLength(2).IsFixedLength();
            r.Property(x => x.City).HasColumnName("recipient_city").HasMaxLength(100);
            r.Property(x => x.Street).HasColumnName("recipient_street").HasMaxLength(200);
            r.Property(x => x.Notes).HasColumnName("recipient_notes").HasMaxLength(500);
        });
        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasOne<Courier>().WithMany().HasForeignKey(s => s.CourierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(s => s.History).WithOne().HasForeignKey(h => h.ShipmentId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.History).HasField("_history").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(s => s.DomainEvents);
        builder.Ignore(s => s.IsSettled);

        // One shipment per order; tracking numbers are unique (they are how courier files are matched).
        builder.HasIndex(s => s.OrderId).IsUnique().HasDatabaseName("ux_shipments_order_id");
        builder.HasIndex(s => s.TrackingNumber).IsUnique().HasDatabaseName("ux_shipments_tracking_number");
        builder.HasIndex(s => new { s.CourierId, s.Status }).HasDatabaseName("ix_shipments_courier_id_status");
        builder.HasIndex(s => s.CustomerId).HasDatabaseName("ix_shipments_customer_id");
        builder.HasIndex(s => s.SettlementId).HasDatabaseName("ix_shipments_settlement_id");
    }
}

internal sealed class ShipmentStatusChangeConfiguration : IEntityTypeConfiguration<ShipmentStatusChange>
{
    public void Configure(EntityTypeBuilder<ShipmentStatusChange> builder)
    {
        builder.ToTable("shipment_status_changes");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).ValueGeneratedNever();
        builder.Property(h => h.FromStatus).HasConversion<string>().HasMaxLength(30);
        builder.Property(h => h.ToStatus).HasConversion<string>().HasMaxLength(30);
        builder.Property(h => h.Source).HasConversion<string>().HasMaxLength(20);
        builder.Property(h => h.ActorId).HasMaxLength(100);
        builder.Property(h => h.Note).HasMaxLength(500);
        builder.HasIndex(h => new { h.ShipmentId, h.ChangedAtUtc }).HasDatabaseName("ix_shipment_status_changes_shipment_id_changed_at_utc");
    }
}

internal sealed class SettlementConfiguration : IEntityTypeConfiguration<Settlement>
{
    public void Configure(EntityTypeBuilder<Settlement> builder)
    {
        builder.ToTable("settlements");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.CourierName).HasMaxLength(100);
        builder.Property(s => s.Reference).HasMaxLength(100);
        builder.Property(s => s.FileHash).HasMaxLength(64).IsFixedLength();
        builder.Property(s => s.ImportedBy).HasMaxLength(100);
        builder.HasOne<Courier>().WithMany().HasForeignKey(s => s.CourierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(s => s.Lines).WithOne().HasForeignKey(l => l.SettlementId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(s => s.DomainEvents);
        builder.Ignore(s => s.TotalCollected);
        builder.Ignore(s => s.TotalFees);
        builder.Ignore(s => s.NetPaid);
        builder.Ignore(s => s.MatchedAmount);
        builder.Ignore(s => s.MatchedCount);

        // The same statement file imported twice is recognised by its content hash.
        builder.HasIndex(s => new { s.CourierId, s.FileHash }).IsUnique().HasDatabaseName("ux_settlements_courier_id_file_hash");
    }
}

internal sealed class SettlementLineConfiguration : IEntityTypeConfiguration<SettlementLine>
{
    public void Configure(EntityTypeBuilder<SettlementLine> builder)
    {
        builder.ToTable("settlement_lines");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.TrackingNumber).HasMaxLength(Shipment.TextMaxLength);
        builder.Property(l => l.OrderNumber).HasMaxLength(20);
        builder.Property(l => l.CollectedAmount).HasPrecision(10, 2);
        builder.Property(l => l.Fee).HasPrecision(10, 2);
        builder.Property(l => l.ExpectedAmount).HasPrecision(10, 2);
        builder.Property(l => l.ExpectedFee).HasPrecision(10, 2);
        builder.Property(l => l.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(l => l.Note).HasMaxLength(500);
        builder.HasIndex(l => l.TrackingNumber).HasDatabaseName("ix_settlement_lines_tracking_number");
    }
}

internal sealed class StatusImportConfiguration : IEntityTypeConfiguration<StatusImport>
{
    public void Configure(EntityTypeBuilder<StatusImport> builder)
    {
        builder.ToTable("status_imports");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.FileHash).HasMaxLength(64).IsFixedLength();
        builder.Property(i => i.ImportedBy).HasMaxLength(100);
        builder.Property(i => i.Results).HasJsonbConversion().IsRequired();
        builder.HasOne<Courier>().WithMany().HasForeignKey(i => i.CourierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(i => new { i.CourierId, i.FileHash }).IsUnique().HasDatabaseName("ux_status_imports_courier_id_file_hash");
    }
}
