using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PerfumeShop.Modules.Shipping.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialShipping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "shipping");

            migrationBuilder.CreateTable(
                name: "couriers",
                schema: "shipping",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    contact_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    contact_phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    contact_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    delivery_fee = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    cod_fee_fixed = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    cod_fee_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    accepts_flammable_goods = table.Column<bool>(type: "boolean", nullable: false),
                    remittance_schedule = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    remittance_days = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    adapter_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    enabled_countries = table.Column<List<string>>(type: "text[]", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_couriers", x => x.id);
                    table.CheckConstraint("ck_couriers_fees", "delivery_fee >= 0 AND cod_fee_fixed >= 0 AND cod_fee_percent >= 0 AND cod_fee_percent <= 10 AND remittance_days >= 1");
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "shipping",
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
                name: "settlements",
                schema: "shipping",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    courier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    courier_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    file_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    imported_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    imported_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settlements", x => x.id);
                    table.ForeignKey(
                        name: "fk_settlements_couriers_courier_id",
                        column: x => x.courier_id,
                        principalSchema: "shipping",
                        principalTable: "couriers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shipments",
                schema: "shipping",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    courier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    courier_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    tracking_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    cod_amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    parcel_count = table.Column<int>(type: "integer", nullable: false),
                    weight_grams = table.Column<int>(type: "integer", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    handed_over_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    in_transit_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    delivered_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    refused_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    returned_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    settlement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    settled_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    collected_amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    cod_fee = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    recipient_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    recipient_country_code = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    recipient_full_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    recipient_notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    recipient_phone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    recipient_street = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipments", x => x.id);
                    table.CheckConstraint("ck_shipments_cod", "cod_amount > 0");
                    table.CheckConstraint("ck_shipments_parcels", "parcel_count BETWEEN 1 AND 10 AND (weight_grams IS NULL OR weight_grams > 0)");
                    table.ForeignKey(
                        name: "fk_shipments_couriers_courier_id",
                        column: x => x.courier_id,
                        principalSchema: "shipping",
                        principalTable: "couriers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "status_imports",
                schema: "shipping",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    courier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    imported_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    imported_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    results = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_status_imports", x => x.id);
                    table.ForeignKey(
                        name: "fk_status_imports_couriers_courier_id",
                        column: x => x.courier_id,
                        principalSchema: "shipping",
                        principalTable: "couriers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "settlement_lines",
                schema: "shipping",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    settlement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    tracking_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    collected_amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    fee = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    order_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    expected_amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    expected_fee = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settlement_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_settlement_lines_settlements_settlement_id",
                        column: x => x.settlement_id,
                        principalSchema: "shipping",
                        principalTable: "settlements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shipment_status_changes",
                schema: "shipping",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    to_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    actor_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    changed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipment_status_changes", x => x.id);
                    table.ForeignKey(
                        name: "fk_shipment_status_changes_shipments_shipment_id",
                        column: x => x.shipment_id,
                        principalSchema: "shipping",
                        principalTable: "shipments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_couriers_name",
                schema: "shipping",
                table: "couriers",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_unprocessed",
                schema: "shipping",
                table: "outbox_messages",
                column: "occurred_on_utc",
                filter: "processed_on_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_settlement_lines_settlement_id",
                schema: "shipping",
                table: "settlement_lines",
                column: "settlement_id");

            migrationBuilder.CreateIndex(
                name: "ix_settlement_lines_tracking_number",
                schema: "shipping",
                table: "settlement_lines",
                column: "tracking_number");

            migrationBuilder.CreateIndex(
                name: "ux_settlements_courier_id_file_hash",
                schema: "shipping",
                table: "settlements",
                columns: new[] { "courier_id", "file_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shipment_status_changes_shipment_id_changed_at_utc",
                schema: "shipping",
                table: "shipment_status_changes",
                columns: new[] { "shipment_id", "changed_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_shipments_courier_id_status",
                schema: "shipping",
                table: "shipments",
                columns: new[] { "courier_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_shipments_customer_id",
                schema: "shipping",
                table: "shipments",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipments_settlement_id",
                schema: "shipping",
                table: "shipments",
                column: "settlement_id");

            migrationBuilder.CreateIndex(
                name: "ux_shipments_order_id",
                schema: "shipping",
                table: "shipments",
                column: "order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_shipments_tracking_number",
                schema: "shipping",
                table: "shipments",
                column: "tracking_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_status_imports_courier_id_file_hash",
                schema: "shipping",
                table: "status_imports",
                columns: new[] { "courier_id", "file_hash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "shipping");

            migrationBuilder.DropTable(
                name: "settlement_lines",
                schema: "shipping");

            migrationBuilder.DropTable(
                name: "shipment_status_changes",
                schema: "shipping");

            migrationBuilder.DropTable(
                name: "status_imports",
                schema: "shipping");

            migrationBuilder.DropTable(
                name: "settlements",
                schema: "shipping");

            migrationBuilder.DropTable(
                name: "shipments",
                schema: "shipping");

            migrationBuilder.DropTable(
                name: "couriers",
                schema: "shipping");
        }
    }
}
