using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PerfumeShop.Modules.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inventory");

            migrationBuilder.CreateTable(
                name: "stock_items",
                schema: "inventory",
                columns: table => new
                {
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    on_hand = table.Column<int>(type: "integer", nullable: false),
                    reserved = table.Column<int>(type: "integer", nullable: false),
                    low_stock_threshold = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_items", x => x.variant_id);
                    table.CheckConstraint("ck_stock_items_on_hand", "on_hand >= 0");
                    table.CheckConstraint("ck_stock_items_reserved", "reserved >= 0 AND reserved <= on_hand");
                    table.CheckConstraint("ck_stock_items_threshold", "low_stock_threshold >= 0");
                });

            migrationBuilder.CreateTable(
                name: "stock_batches",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    quantity_received = table.Column<int>(type: "integer", nullable: false),
                    quantity_remaining = table.Column<int>(type: "integer", nullable: false),
                    supplier_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    supplier_invoice_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    supplier_invoice_date = table.Column<DateOnly>(type: "date", nullable: false),
                    received_on = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    unit_cost_amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    unit_cost_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_batches", x => x.id);
                    table.CheckConstraint("ck_stock_batches_received", "quantity_received > 0");
                    table.CheckConstraint("ck_stock_batches_remaining", "quantity_remaining >= 0 AND quantity_remaining <= quantity_received");
                    table.CheckConstraint("ck_stock_batches_unit_cost", "unit_cost_amount >= 0");
                    table.ForeignKey(
                        name: "fk_stock_batches_stock_items_variant_id",
                        column: x => x.variant_id,
                        principalSchema: "inventory",
                        principalTable: "stock_items",
                        principalColumn: "variant_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_movements",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    on_hand_delta = table.Column<int>(type: "integer", nullable: false),
                    reserved_delta = table.Column<int>(type: "integer", nullable: false),
                    on_hand_after = table.Column<int>(type: "integer", nullable: false),
                    reserved_after = table.Column<int>(type: "integer", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    order_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    batch_codes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    actor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_movements", x => x.id);
                    table.CheckConstraint("ck_stock_movements_quantity", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_stock_movements_stock_items_variant_id",
                        column: x => x.variant_id,
                        principalSchema: "inventory",
                        principalTable: "stock_items",
                        principalColumn: "variant_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_reservations",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    committed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    released_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    returned_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_reservations", x => x.id);
                    table.CheckConstraint("ck_stock_reservations_quantity", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_stock_reservations_stock_items_variant_id",
                        column: x => x.variant_id,
                        principalSchema: "inventory",
                        principalTable: "stock_items",
                        principalColumn: "variant_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_allocations",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reservation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_allocations", x => x.id);
                    table.CheckConstraint("ck_stock_allocations_quantity", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_stock_allocations_stock_batch_batch_id",
                        column: x => x.batch_id,
                        principalSchema: "inventory",
                        principalTable: "stock_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_allocations_stock_reservations_reservation_id",
                        column: x => x.reservation_id,
                        principalSchema: "inventory",
                        principalTable: "stock_reservations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stock_allocations_batch_id",
                schema: "inventory",
                table: "stock_allocations",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_allocations_reservation_id",
                schema: "inventory",
                table: "stock_allocations",
                column: "reservation_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_batches_batch_code",
                schema: "inventory",
                table: "stock_batches",
                column: "batch_code");

            migrationBuilder.CreateIndex(
                name: "ix_stock_batches_receipt_id",
                schema: "inventory",
                table: "stock_batches",
                column: "receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_batches_supplier_invoice_number",
                schema: "inventory",
                table: "stock_batches",
                column: "supplier_invoice_number");

            migrationBuilder.CreateIndex(
                name: "ix_stock_batches_variant_id_received_on",
                schema: "inventory",
                table: "stock_batches",
                columns: new[] { "variant_id", "received_on" });

            migrationBuilder.CreateIndex(
                name: "ux_stock_batches_variant_id_sequence",
                schema: "inventory",
                table: "stock_batches",
                columns: new[] { "variant_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_order_id",
                schema: "inventory",
                table: "stock_movements",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_variant_id_occurred_at_utc",
                schema: "inventory",
                table: "stock_movements",
                columns: new[] { "variant_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_expiring",
                schema: "inventory",
                table: "stock_reservations",
                column: "expires_at_utc",
                filter: "status = 'Reserved' AND expires_at_utc IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_order_id",
                schema: "inventory",
                table: "stock_reservations",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_variant_id_status",
                schema: "inventory",
                table: "stock_reservations",
                columns: new[] { "variant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_stock_reservations_order_line_id",
                schema: "inventory",
                table: "stock_reservations",
                column: "order_line_id",
                unique: true);

            // The stock ledger is append-only: corrections are new movements, never edits.
            migrationBuilder.Sql("""
                CREATE FUNCTION inventory.reject_stock_movement_change() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'inventory.stock_movements is append-only (% is not allowed)', TG_OP
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;

                CREATE TRIGGER stock_movements_append_only
                    BEFORE UPDATE OR DELETE ON inventory.stock_movements
                    FOR EACH ROW EXECUTE FUNCTION inventory.reject_stock_movement_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS stock_movements_append_only ON inventory.stock_movements;
                DROP FUNCTION IF EXISTS inventory.reject_stock_movement_change();
                """);

            migrationBuilder.DropTable(
                name: "stock_allocations",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_movements",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_batches",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_reservations",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_items",
                schema: "inventory");
        }
    }
}
