using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PerfumeShop.Modules.Orders.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "orders");

            migrationBuilder.CreateTable(
                name: "delivery_rates",
                schema: "orders",
                columns: table => new
                {
                    country_code = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    country_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    fee = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    free_delivery_from = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_rates", x => x.country_code);
                });

            migrationBuilder.CreateTable(
                name: "order_number_counters",
                schema: "orders",
                columns: table => new
                {
                    year = table.Column<int>(type: "integer", nullable: false),
                    last_number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_number_counters", x => x.year);
                });

            migrationBuilder.CreateTable(
                name: "order_settings",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    max_open_orders_per_customer = table.Column<int>(type: "integer", nullable: false),
                    max_refused_orders = table.Column<int>(type: "integer", nullable: false),
                    pending_expiry_hours = table.Column<int>(type: "integer", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_settings", x => x.id);
                    table.CheckConstraint("ck_order_settings_singleton", "id = 1");
                });

            migrationBuilder.CreateTable(
                name: "orders",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    customer_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    subtotal = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    delivery_fee = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    total = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    vat_amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    request_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    placed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    confirmed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    shipped_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    delivered_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cash_collected_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    closed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    courier_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    tracking_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    cash_collected_amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true, defaultValue: "EUR"),
                    payment_method = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true, defaultValue: "CashOnDelivery"),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    delivery_address_id = table.Column<Guid>(type: "uuid", nullable: true),
                    delivery_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    delivery_country_code = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    delivery_notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    delivery_street = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    customer_full_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    customer_phone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orders", x => x.id);
                    table.CheckConstraint("ck_orders_cash_collected", "cash_collected_amount IS NULL OR cash_collected_amount >= 0");
                    table.CheckConstraint("ck_orders_totals", "subtotal > 0 AND delivery_fee >= 0 AND total = subtotal + delivery_fee AND vat_amount >= 0");
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    content = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_attempt_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "order_lines",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fragrance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    brand_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    fragrance_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    variant_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    size_value = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: true),
                    size_unit = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    unit_price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    line_total = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    vat_amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    batches = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_lines", x => x.id);
                    table.CheckConstraint("ck_order_lines_amounts", "quantity > 0 AND unit_price > 0 AND line_total = unit_price * quantity");
                    table.ForeignKey(
                        name: "fk_order_lines_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "orders",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_notes",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_notes", x => x.id);
                    table.ForeignKey(
                        name: "fk_order_notes_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "orders",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_status_changes",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    to_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    actor_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    actor_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    note = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    changed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_status_changes", x => x.id);
                    table.ForeignKey(
                        name: "fk_order_status_changes_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "orders",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "orders",
                table: "delivery_rates",
                columns: new[] { "country_code", "country_name", "fee", "free_delivery_from", "is_enabled", "updated_at_utc" },
                values: new object[,]
                {
                    { "AL", "Albania", 5.00m, 80.00m, false, new DateTime(2026, 11, 26, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "MK", "North Macedonia", 5.00m, 80.00m, false, new DateTime(2026, 11, 26, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "XK", "Kosovo", 2.00m, 40.00m, true, new DateTime(2026, 11, 26, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.InsertData(
                schema: "orders",
                table: "order_settings",
                columns: new[] { "id", "max_open_orders_per_customer", "max_refused_orders", "pending_expiry_hours", "updated_at_utc" },
                values: new object[] { 1, 2, 2, 48, new DateTime(2026, 11, 26, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.CreateIndex(
                name: "ix_order_lines_order_id",
                schema: "orders",
                table: "order_lines",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_lines_variant_id",
                schema: "orders",
                table: "order_lines",
                column: "variant_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_notes_order_id",
                schema: "orders",
                table: "order_notes",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_status_changes_order_id_changed_at_utc",
                schema: "orders",
                table: "order_status_changes",
                columns: new[] { "order_id", "changed_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_customer_id_status",
                schema: "orders",
                table: "orders",
                columns: new[] { "customer_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_pending_expires_at_utc",
                schema: "orders",
                table: "orders",
                column: "expires_at_utc",
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_orders_status_placed_at_utc",
                schema: "orders",
                table: "orders",
                columns: new[] { "status", "placed_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_orders_customer_id_idempotency_key",
                schema: "orders",
                table: "orders",
                columns: new[] { "customer_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_orders_number",
                schema: "orders",
                table: "orders",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_unprocessed",
                schema: "orders",
                table: "outbox_messages",
                column: "occurred_on_utc",
                filter: "processed_on_utc IS NULL");

            // Staff search by phone (EF cannot index a property of the complex Customer snapshot).
            migrationBuilder.Sql("CREATE INDEX ix_orders_customer_phone ON orders.orders (customer_phone);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delivery_rates",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "order_lines",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "order_notes",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "order_number_counters",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "order_settings",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "order_status_changes",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "orders",
                schema: "orders");
        }
    }
}
