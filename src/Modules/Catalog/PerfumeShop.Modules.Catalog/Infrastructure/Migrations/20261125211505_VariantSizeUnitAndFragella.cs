using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PerfumeShop.Modules.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class VariantSizeUnitAndFragella : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_fragrance_variants_size_positive",
                schema: "catalog",
                table: "fragrance_variants");

            migrationBuilder.RenameColumn(
                name: "size_ml",
                schema: "catalog",
                table: "fragrance_variants",
                newName: "size_value");

            migrationBuilder.AddColumn<string>(
                name: "size_unit",
                schema: "catalog",
                table: "fragrance_variants",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            // Every existing size was in millilitres.
            migrationBuilder.Sql("UPDATE catalog.fragrance_variants SET size_unit = 'Ml' WHERE size_value IS NOT NULL;");

            migrationBuilder.CreateTable(
                name: "fragella_fragrances",
                schema: "catalog",
                columns: table => new
                {
                    fragella_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    brand = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    raw = table.Column<string>(type: "jsonb", nullable: false),
                    fetched_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fragella_fragrances", x => x.fragella_id);
                });

            migrationBuilder.CreateTable(
                name: "fragella_search_cache",
                schema: "catalog",
                columns: table => new
                {
                    query = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    fragella_ids = table.Column<List<string>>(type: "text[]", nullable: false),
                    fetched_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fragella_search_cache", x => x.query);
                });

            migrationBuilder.CreateTable(
                name: "fragella_usage",
                schema: "catalog",
                columns: table => new
                {
                    month = table.Column<DateOnly>(type: "date", nullable: false),
                    request_count = table.Column<int>(type: "integer", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fragella_usage", x => x.month);
                    table.CheckConstraint("ck_fragella_usage_count", "request_count >= 0");
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_fragrance_variants_size_complete",
                schema: "catalog",
                table: "fragrance_variants",
                sql: "(size_value IS NULL) = (size_unit IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fragrance_variants_size_positive",
                schema: "catalog",
                table: "fragrance_variants",
                sql: "size_value IS NULL OR size_value > 0");

            migrationBuilder.CreateIndex(
                name: "ix_fragella_search_cache_expires_at_utc",
                schema: "catalog",
                table: "fragella_search_cache",
                column: "expires_at_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fragella_fragrances",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "fragella_search_cache",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "fragella_usage",
                schema: "catalog");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fragrance_variants_size_complete",
                schema: "catalog",
                table: "fragrance_variants");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fragrance_variants_size_positive",
                schema: "catalog",
                table: "fragrance_variants");

            migrationBuilder.DropColumn(
                name: "size_unit",
                schema: "catalog",
                table: "fragrance_variants");

            migrationBuilder.RenameColumn(
                name: "size_value",
                schema: "catalog",
                table: "fragrance_variants",
                newName: "size_ml");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fragrance_variants_size_positive",
                schema: "catalog",
                table: "fragrance_variants",
                sql: "size_ml IS NULL OR size_ml > 0");
        }
    }
}
