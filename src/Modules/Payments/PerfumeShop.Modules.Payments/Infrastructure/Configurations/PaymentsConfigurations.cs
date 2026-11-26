using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PerfumeShop.Modules.Payments.Domain;
using PerfumeShop.Shared.Infrastructure.Persistence;

namespace PerfumeShop.Modules.Payments.Infrastructure.Configurations;

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments", t => t.HasCheckConstraint("ck_payments_amounts",
            "expected_amount > 0 AND (collected_amount IS NULL OR collected_amount >= 0) AND (courier_fee IS NULL OR courier_fee >= 0)"));
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.OrderNumber).HasMaxLength(20).IsRequired();
        builder.Property(p => p.Method).HasMaxLength(30).IsRequired();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.CourierName).HasMaxLength(100);
        builder.Property(p => p.TrackingNumber).HasMaxLength(100);
        builder.Property(p => p.SettlementReference).HasMaxLength(100);
        foreach (var amount in new[] { nameof(Payment.ExpectedAmount), nameof(Payment.CollectedAmount), nameof(Payment.CourierFee), nameof(Payment.NetReceived) })
        {
            builder.Property(amount).HasPrecision(10, 2);
        }

        builder.Property<string>("currency").HasMaxLength(3).IsFixedLength().HasDefaultValue("EUR");
        builder.Property<uint>("xmin").IsRowVersion();
        builder.Ignore(p => p.DomainEvents);

        builder.HasIndex(p => p.OrderId).IsUnique().HasDatabaseName("ux_payments_order_id");
        builder.HasIndex(p => new { p.Status, p.PlacedAtUtc }).HasDatabaseName("ix_payments_status_placed_at_utc");
        builder.HasIndex(p => p.ShippedAtUtc).HasDatabaseName("ix_payments_shipped_at_utc");
        builder.HasIndex(p => p.CollectedAtUtc).HasDatabaseName("ix_payments_collected_at_utc");
        builder.HasIndex(p => p.SettlementId).HasDatabaseName("ix_payments_settlement_id");
    }
}

internal sealed class FiscalReceiptConfiguration : IEntityTypeConfiguration<FiscalReceipt>
{
    public void Configure(EntityTypeBuilder<FiscalReceipt> builder)
    {
        builder.ToTable("fiscal_receipts");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.InternalNumber).HasMaxLength(30).IsRequired();
        builder.Property(r => r.OrderNumber).HasMaxLength(20).IsRequired();
        builder.Property(r => r.OriginalInternalNumber).HasMaxLength(30);
        builder.Property(r => r.Kind).HasConversion<string>().HasMaxLength(10);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property(r => r.Lines).HasJsonbConversion().IsRequired();
        builder.Property(r => r.VatBreakdown).HasJsonbConversion().IsRequired();
        builder.Property(r => r.Total).HasPrecision(10, 2);
        builder.Property(r => r.VatTotal).HasPrecision(10, 2);
        builder.Property(r => r.PaymentMethod).HasMaxLength(30);
        builder.Property(r => r.Provider).HasMaxLength(50);
        builder.Property(r => r.FiscalNumber).HasMaxLength(100);
        builder.Property(r => r.LastError).HasMaxLength(1000);
        builder.Property(r => r.VoidReason).HasMaxLength(500);
        builder.Property<string>("currency").HasMaxLength(3).IsFixedLength().HasDefaultValue("EUR");
        builder.Property<uint>("xmin").IsRowVersion();
        builder.Ignore(r => r.DomainEvents);
        builder.Ignore(r => r.CanBeIssued);

        builder.HasOne<FiscalReceipt>().WithMany().HasForeignKey(r => r.OriginalReceiptId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(r => r.Attempts).WithOne().HasForeignKey(a => a.ReceiptId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Attempts).HasField("_attempts").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(r => r.InternalNumber).IsUnique().HasDatabaseName("ux_fiscal_receipts_internal_number");
        // One sale and at most one storno per order, also when an event is delivered twice.
        builder.HasIndex(r => new { r.OrderId, r.Kind }).IsUnique().HasDatabaseName("ux_fiscal_receipts_order_id_kind");
        builder.HasIndex(r => new { r.Status, r.CreatedAtUtc }).HasDatabaseName("ix_fiscal_receipts_status_created_at_utc");
    }
}

internal sealed class FiscalReceiptAttemptConfiguration : IEntityTypeConfiguration<FiscalReceiptAttempt>
{
    public void Configure(EntityTypeBuilder<FiscalReceiptAttempt> builder)
    {
        builder.ToTable("fiscal_receipt_attempts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Provider).HasMaxLength(50);
        builder.Property(a => a.RequestJson).HasColumnType("jsonb");
        builder.Property(a => a.ResponseJson).HasColumnType("jsonb");
        builder.Property(a => a.Error).HasMaxLength(2000);
        builder.HasIndex(a => new { a.ReceiptId, a.AttemptedAtUtc }).HasDatabaseName("ix_fiscal_receipt_attempts_receipt_id_attempted_at_utc");
    }
}

internal sealed class ReceiptNumberCounterConfiguration : IEntityTypeConfiguration<ReceiptNumberCounter>
{
    public void Configure(EntityTypeBuilder<ReceiptNumberCounter> builder)
    {
        builder.ToTable("receipt_number_counters");
        builder.HasKey(c => c.Year);
        builder.Property(c => c.Year).ValueGeneratedNever();
    }
}
