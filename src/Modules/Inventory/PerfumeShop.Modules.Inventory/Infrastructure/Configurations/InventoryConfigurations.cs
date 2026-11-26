using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PerfumeShop.Modules.Inventory.Domain;

namespace PerfumeShop.Modules.Inventory.Infrastructure.Configurations;

internal sealed class StockItemConfiguration : IEntityTypeConfiguration<StockItem>
{
    public void Configure(EntityTypeBuilder<StockItem> builder)
    {
        builder.ToTable("stock_items", t =>
        {
            t.HasCheckConstraint("ck_stock_items_on_hand", "on_hand >= 0");
            t.HasCheckConstraint("ck_stock_items_reserved", "reserved >= 0 AND reserved <= on_hand");
            t.HasCheckConstraint("ck_stock_items_threshold", "low_stock_threshold >= 0");
        });
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("variant_id").ValueGeneratedNever();

        // Optimistic concurrency: two orders reserving the last bottle both read the same row
        // version; the second UPDATE matches no row and is retried against fresh numbers.
        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasMany(i => i.Batches).WithOne().HasForeignKey(b => b.VariantId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(i => i.Batches).HasField("_batches").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(i => i.Available);
        builder.Ignore(i => i.IsLowStock);
        builder.Ignore(i => i.PendingMovements);
        builder.Ignore(i => i.DomainEvents);
    }
}

internal sealed class StockBatchConfiguration : IEntityTypeConfiguration<StockBatch>
{
    public void Configure(EntityTypeBuilder<StockBatch> builder)
    {
        builder.ToTable("stock_batches", t =>
        {
            t.HasCheckConstraint("ck_stock_batches_received", "quantity_received > 0");
            t.HasCheckConstraint("ck_stock_batches_remaining", "quantity_remaining >= 0 AND quantity_remaining <= quantity_received");
            t.HasCheckConstraint("ck_stock_batches_unit_cost", "unit_cost_amount >= 0");
        });
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();
        builder.Property(b => b.BatchCode).HasMaxLength(StockBatch.BatchCodeMaxLength).IsRequired();
        builder.Property(b => b.SupplierName).HasMaxLength(StockBatch.SupplierMaxLength).IsRequired();
        builder.Property(b => b.SupplierInvoiceNumber).HasMaxLength(StockBatch.InvoiceNumberMaxLength).IsRequired();
        builder.ComplexProperty(b => b.UnitCost, cost =>
        {
            cost.Property(m => m.Amount).HasColumnName("unit_cost_amount").HasPrecision(10, 2);
            cost.Property(m => m.Currency).HasColumnName("unit_cost_currency").HasMaxLength(3).IsFixedLength();
        });

        builder.HasIndex(b => new { b.VariantId, b.ReceivedOn }).HasDatabaseName("ix_stock_batches_variant_id_received_on");
        builder.HasIndex(b => new { b.VariantId, b.Sequence }).IsUnique().HasDatabaseName("ux_stock_batches_variant_id_sequence");
        builder.HasIndex(b => b.BatchCode).HasDatabaseName("ix_stock_batches_batch_code");
        builder.HasIndex(b => b.SupplierInvoiceNumber).HasDatabaseName("ix_stock_batches_supplier_invoice_number");
        builder.HasIndex(b => b.ReceiptId).HasDatabaseName("ix_stock_batches_receipt_id");
    }
}

internal sealed class StockReservationConfiguration : IEntityTypeConfiguration<StockReservation>
{
    public void Configure(EntityTypeBuilder<StockReservation> builder)
    {
        builder.ToTable("stock_reservations", t => t.HasCheckConstraint("ck_stock_reservations_quantity", "quantity > 0"));
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasOne<StockItem>().WithMany().HasForeignKey(r => r.VariantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(r => r.Allocations).WithOne().HasForeignKey(a => a.ReservationId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Allocations).HasField("_allocations").UsePropertyAccessMode(PropertyAccessMode.Field);

        // Idempotency key: an order line can hold at most one reservation.
        builder.HasIndex(r => r.OrderLineId).IsUnique().HasDatabaseName("ux_stock_reservations_order_line_id");
        builder.HasIndex(r => r.OrderId).HasDatabaseName("ix_stock_reservations_order_id");
        builder.HasIndex(r => new { r.VariantId, r.Status }).HasDatabaseName("ix_stock_reservations_variant_id_status");
        builder.HasIndex(r => r.ExpiresAtUtc)
            .HasFilter("status = 'Reserved' AND expires_at_utc IS NOT NULL")
            .HasDatabaseName("ix_stock_reservations_expiring");
    }
}

internal sealed class StockAllocationConfiguration : IEntityTypeConfiguration<StockAllocation>
{
    public void Configure(EntityTypeBuilder<StockAllocation> builder)
    {
        builder.ToTable("stock_allocations", t => t.HasCheckConstraint("ck_stock_allocations_quantity", "quantity > 0"));
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.BatchCode).HasMaxLength(StockBatch.BatchCodeMaxLength).IsRequired();
        builder.HasOne<StockBatch>().WithMany().HasForeignKey(a => a.BatchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => a.BatchId).HasDatabaseName("ix_stock_allocations_batch_id");
    }
}

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("stock_movements", t => t.HasCheckConstraint("ck_stock_movements_quantity", "quantity > 0"));
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.BatchCodes).HasMaxLength(500);
        builder.Property(m => m.Reason).HasMaxLength(StockItem.ReasonMaxLength);
        builder.Property(m => m.Actor).HasMaxLength(100);
        builder.HasOne<StockItem>().WithMany().HasForeignKey(m => m.VariantId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => new { m.VariantId, m.OccurredAtUtc }).HasDatabaseName("ix_stock_movements_variant_id_occurred_at_utc");
        builder.HasIndex(m => m.OrderId).HasDatabaseName("ix_stock_movements_order_id");
        // Append-only: a trigger in the migration rejects UPDATE and DELETE.
    }
}
