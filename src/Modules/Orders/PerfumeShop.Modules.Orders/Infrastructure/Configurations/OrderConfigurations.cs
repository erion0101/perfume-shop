using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PerfumeShop.Modules.Orders.Domain;
using PerfumeShop.Shared.Infrastructure.Persistence;

namespace PerfumeShop.Modules.Orders.Infrastructure.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders", t =>
        {
            t.HasCheckConstraint("ck_orders_totals", "subtotal > 0 AND delivery_fee >= 0 AND total = subtotal + delivery_fee AND vat_amount >= 0");
            t.HasCheckConstraint("ck_orders_cash_collected", "cash_collected_amount IS NULL OR cash_collected_amount >= 0");
        });
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.Number).HasMaxLength(20).IsRequired();
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.CustomerNote).HasMaxLength(Order.NoteMaxLength);
        builder.Property(o => o.CancellationReason).HasMaxLength(Order.NoteMaxLength);
        builder.Property(o => o.CourierName).HasMaxLength(Order.TextMaxLength);
        builder.Property(o => o.TrackingNumber).HasMaxLength(Order.TextMaxLength);
        builder.Property(o => o.IdempotencyKey).HasMaxLength(100).IsRequired();
        builder.Property(o => o.RequestHash).HasMaxLength(64).IsFixedLength().IsRequired();
        foreach (var money in new[] { nameof(Order.Subtotal), nameof(Order.DeliveryFee), nameof(Order.Total), nameof(Order.VatAmount), nameof(Order.CashCollectedAmount) })
        {
            builder.Property(money).HasPrecision(10, 2);
        }

        builder.Property<string>("currency").HasMaxLength(3).IsFixedLength().HasDefaultValue(Order.Currency);
        builder.Property<string>("payment_method").HasMaxLength(30).HasDefaultValue(Order.PaymentMethod);

        builder.ComplexProperty(o => o.Customer, c =>
        {
            c.Property(x => x.FullName).HasColumnName("customer_full_name").HasMaxLength(Order.TextMaxLength);
            c.Property(x => x.Phone).HasColumnName("customer_phone").HasMaxLength(16);
        });
        builder.ComplexProperty(o => o.Address, a =>
        {
            a.Property(x => x.AddressId).HasColumnName("delivery_address_id");
            a.Property(x => x.CountryCode).HasColumnName("delivery_country_code").HasMaxLength(2).IsFixedLength();
            a.Property(x => x.City).HasColumnName("delivery_city").HasMaxLength(100);
            a.Property(x => x.Street).HasColumnName("delivery_street").HasMaxLength(200);
            a.Property(x => x.Notes).HasColumnName("delivery_notes").HasMaxLength(500);
        });

        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasMany(o => o.Lines).WithOne().HasForeignKey(l => l.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(o => o.History).WithOne().HasForeignKey(h => h.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(o => o.Notes).WithOne().HasForeignKey(n => n.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(o => o.History).HasField("_history").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(o => o.Notes).HasField("_notes").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(o => o.DomainEvents);

        builder.HasIndex(o => o.Number).IsUnique().HasDatabaseName("ux_orders_number");
        // Idempotency: one order per customer and key, so a double click cannot create two orders.
        builder.HasIndex(o => new { o.CustomerId, o.IdempotencyKey }).IsUnique().HasDatabaseName("ux_orders_customer_id_idempotency_key");
        builder.HasIndex(o => new { o.CustomerId, o.Status }).HasDatabaseName("ix_orders_customer_id_status");
        builder.HasIndex(o => new { o.Status, o.PlacedAtUtc }).HasDatabaseName("ix_orders_status_placed_at_utc");
        builder.HasIndex(o => o.ExpiresAtUtc).HasFilter("status = 'Pending'").HasDatabaseName("ix_orders_pending_expires_at_utc");
        // ix_orders_customer_phone (staff search by phone) is created in the migration with SQL:
        // EF cannot index a property of a complex type.
    }
}

internal sealed class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        builder.ToTable("order_lines", t => t.HasCheckConstraint("ck_order_lines_amounts", "quantity > 0 AND unit_price > 0 AND line_total = unit_price * quantity"));
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.Sku).HasMaxLength(64).IsRequired();
        builder.Property(l => l.BrandName).HasMaxLength(100).IsRequired();
        builder.Property(l => l.FragranceName).HasMaxLength(200).IsRequired();
        builder.Property(l => l.VariantType).HasMaxLength(30).IsRequired();
        builder.Property(l => l.SizeValue).HasPrecision(6, 1);
        builder.Property(l => l.SizeUnit).HasMaxLength(10);
        builder.Property(l => l.UnitPrice).HasPrecision(10, 2);
        builder.Property(l => l.LineTotal).HasPrecision(10, 2);
        builder.Property(l => l.VatAmount).HasPrecision(10, 2);
        builder.Property(l => l.Batches).HasJsonbConversion().IsRequired();
        builder.HasIndex(l => l.VariantId).HasDatabaseName("ix_order_lines_variant_id");
    }
}

internal sealed class OrderStatusChangeConfiguration : IEntityTypeConfiguration<OrderStatusChange>
{
    public void Configure(EntityTypeBuilder<OrderStatusChange> builder)
    {
        builder.ToTable("order_status_changes");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).ValueGeneratedNever();
        builder.Property(h => h.FromStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(h => h.ToStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(h => h.ActorKind).HasConversion<string>().HasMaxLength(20);
        builder.Property(h => h.ActorId).HasMaxLength(100);
        builder.Property(h => h.Note).HasMaxLength(Order.NoteMaxLength + 100);
        builder.HasIndex(h => new { h.OrderId, h.ChangedAtUtc }).HasDatabaseName("ix_order_status_changes_order_id_changed_at_utc");
    }
}

internal sealed class OrderNoteConfiguration : IEntityTypeConfiguration<OrderNote>
{
    public void Configure(EntityTypeBuilder<OrderNote> builder)
    {
        builder.ToTable("order_notes");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedNever();
        builder.Property(n => n.AuthorId).HasMaxLength(100);
        builder.Property(n => n.Text).HasMaxLength(1000);
    }
}

internal sealed class DeliveryRateConfiguration : IEntityTypeConfiguration<DeliveryRate>
{
    public void Configure(EntityTypeBuilder<DeliveryRate> builder)
    {
        builder.ToTable("delivery_rates");
        builder.HasKey(r => r.CountryCode);
        builder.Property(r => r.CountryCode).HasMaxLength(2).IsFixedLength();
        builder.Property(r => r.CountryName).HasMaxLength(60);
        builder.Property(r => r.Fee).HasPrecision(10, 2);
        builder.Property(r => r.FreeDeliveryFrom).HasPrecision(10, 2);

        // Placeholders: Kosovo only at launch; Admin changes fees and enables countries via the API.
        var seeded = new DateTime(2026, 11, 26, 0, 0, 0, DateTimeKind.Utc);
        builder.HasData(
            new { CountryCode = "XK", CountryName = "Kosovo", IsEnabled = true, Fee = 2.00m, FreeDeliveryFrom = (decimal?)40.00m, UpdatedAtUtc = seeded },
            new { CountryCode = "AL", CountryName = "Albania", IsEnabled = false, Fee = 5.00m, FreeDeliveryFrom = (decimal?)80.00m, UpdatedAtUtc = seeded },
            new { CountryCode = "MK", CountryName = "North Macedonia", IsEnabled = false, Fee = 5.00m, FreeDeliveryFrom = (decimal?)80.00m, UpdatedAtUtc = seeded });
    }
}

internal sealed class OrderSettingsConfiguration : IEntityTypeConfiguration<OrderSettings>
{
    public void Configure(EntityTypeBuilder<OrderSettings> builder)
    {
        builder.ToTable("order_settings", t => t.HasCheckConstraint("ck_order_settings_singleton", "id = 1"));
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.HasData(new
        {
            Id = OrderSettings.SingletonId,
            MaxOpenOrdersPerCustomer = 2,
            MaxRefusedOrders = 2,
            PendingExpiryHours = 48,
            UpdatedAtUtc = new DateTime(2026, 11, 26, 0, 0, 0, DateTimeKind.Utc),
        });
    }
}

internal sealed class OrderNumberCounterConfiguration : IEntityTypeConfiguration<OrderNumberCounter>
{
    public void Configure(EntityTypeBuilder<OrderNumberCounter> builder)
    {
        builder.ToTable("order_number_counters");
        builder.HasKey(c => c.Year);
        builder.Property(c => c.Year).ValueGeneratedNever();
    }
}
