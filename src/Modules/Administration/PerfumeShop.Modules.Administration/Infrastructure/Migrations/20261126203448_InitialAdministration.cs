using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PerfumeShop.Modules.Administration.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "administration");

            migrationBuilder.CreateTable(
                name: "audit_entries",
                schema: "administration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    actor_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    actor_email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    changes = table.Column<string>(type: "jsonb", nullable: true),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "business_profile",
                schema: "administration",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    shop_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    address = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    tax_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    updated_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_business_profile", x => x.id);
                    table.CheckConstraint("ck_business_profile_singleton", "id = 1");
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_action_occurred_at_utc",
                schema: "administration",
                table: "audit_entries",
                columns: new[] { "action", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_actor_id",
                schema: "administration",
                table: "audit_entries",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_entity",
                schema: "administration",
                table: "audit_entries",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_occurred_at_utc",
                schema: "administration",
                table: "audit_entries",
                column: "occurred_at_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_entries",
                schema: "administration");

            migrationBuilder.DropTable(
                name: "business_profile",
                schema: "administration");
        }
    }
}
