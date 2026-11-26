using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PerfumeShop.Modules.Shipping.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ShipmentReceiptOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "receipt_override_at_utc",
                schema: "shipping",
                table: "shipments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "receipt_override_by",
                schema: "shipping",
                table: "shipments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "receipt_override_reason",
                schema: "shipping",
                table: "shipments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "receipt_override_at_utc",
                schema: "shipping",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "receipt_override_by",
                schema: "shipping",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "receipt_override_reason",
                schema: "shipping",
                table: "shipments");
        }
    }
}
