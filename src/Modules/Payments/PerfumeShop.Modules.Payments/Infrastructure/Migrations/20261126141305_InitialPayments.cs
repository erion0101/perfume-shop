using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PerfumeShop.Modules.Payments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "payments");

            migrationBuilder.CreateTable(
                name: "fiscal_receipts",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    internal_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    original_receipt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    original_internal_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    lines = table.Column<string>(type: "jsonb", nullable: false),
                    vat_breakdown = table.Column<string>(type: "jsonb", nullable: false),
                    total = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    vat_total = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    payment_method = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_fiscal = table.Column<bool>(type: "boolean", nullable: false),
                    fiscal_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    issued_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    void_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true, defaultValue: "EUR"),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fiscal_receipts", x => x.id);
                    table.ForeignKey(
                        name: "fk_fiscal_receipts_fiscal_receipts_original_receipt_id",
                        column: x => x.original_receipt_id,
                        principalSchema: "payments",
                        principalTable: "fiscal_receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "payments",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consumer = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    processed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_messages", x => new { x.event_id, x.consumer });
                });

            migrationBuilder.CreateTable(
                name: "payments",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    method = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    expected_amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    collected_amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    courier_fee = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    net_received = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    courier_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    tracking_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    placed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    shipped_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    collected_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    settlement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    settlement_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    settled_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    closed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true, defaultValue: "EUR"),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.CheckConstraint("ck_payments_amounts", "expected_amount > 0 AND (collected_amount IS NULL OR collected_amount >= 0) AND (courier_fee IS NULL OR courier_fee >= 0)");
                });

            migrationBuilder.CreateTable(
                name: "receipt_number_counters",
                schema: "payments",
                columns: table => new
                {
                    year = table.Column<int>(type: "integer", nullable: false),
                    last_number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receipt_number_counters", x => x.year);
                });

            migrationBuilder.CreateTable(
                name: "fiscal_receipt_attempts",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    request_json = table.Column<string>(type: "jsonb", nullable: false),
                    response_json = table.Column<string>(type: "jsonb", nullable: true),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    attempted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fiscal_receipt_attempts", x => x.id);
                    table.ForeignKey(
                        name: "fk_fiscal_receipt_attempts_fiscal_receipts_receipt_id",
                        column: x => x.receipt_id,
                        principalSchema: "payments",
                        principalTable: "fiscal_receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_receipt_attempts_receipt_id_attempted_at_utc",
                schema: "payments",
                table: "fiscal_receipt_attempts",
                columns: new[] { "receipt_id", "attempted_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_receipts_original_receipt_id",
                schema: "payments",
                table: "fiscal_receipts",
                column: "original_receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_receipts_status_created_at_utc",
                schema: "payments",
                table: "fiscal_receipts",
                columns: new[] { "status", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_fiscal_receipts_internal_number",
                schema: "payments",
                table: "fiscal_receipts",
                column: "internal_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_fiscal_receipts_order_id_kind",
                schema: "payments",
                table: "fiscal_receipts",
                columns: new[] { "order_id", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payments_collected_at_utc",
                schema: "payments",
                table: "payments",
                column: "collected_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_payments_settlement_id",
                schema: "payments",
                table: "payments",
                column: "settlement_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_shipped_at_utc",
                schema: "payments",
                table: "payments",
                column: "shipped_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_payments_status_placed_at_utc",
                schema: "payments",
                table: "payments",
                columns: new[] { "status", "placed_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_payments_order_id",
                schema: "payments",
                table: "payments",
                column: "order_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fiscal_receipt_attempts",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "payments",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "receipt_number_counters",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "fiscal_receipts",
                schema: "payments");
        }
    }
}
