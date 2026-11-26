using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PerfumeShop.Modules.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.CreateTable(
                name: "brands",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    country_code = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: true),
                    logo_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_brands", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fragrances",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    brand_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    concentration = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    gender = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    olfactive_family = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    launch_year = table.Column<int>(type: "integer", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "jsonb", nullable: false),
                    accords = table.Column<string>(type: "jsonb", nullable: false),
                    longevity = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    sillage = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    seasons = table.Column<int>(type: "integer", nullable: false),
                    occasions = table.Column<int>(type: "integer", nullable: false),
                    similar_to = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    fragella_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    fragella_raw = table.Column<string>(type: "jsonb", nullable: true),
                    fragella_imported_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    perfumers = table.Column<List<string>>(type: "text[]", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fragrances", x => x.id);
                    table.ForeignKey(
                        name: "fk_fragrances_brands_brand_id",
                        column: x => x.brand_id,
                        principalSchema: "catalog",
                        principalTable: "brands",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fragrance_variants",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fragrance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ean = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    size_ml = table.Column<decimal>(type: "numeric(6,1)", precision: 6, scale: 1, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    images = table.Column<string>(type: "jsonb", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    compare_at_price_amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    compare_at_price_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    price_amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    price_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fragrance_variants", x => x.id);
                    table.CheckConstraint("ck_fragrance_variants_active_requires_price", "NOT is_active OR price_amount IS NOT NULL");
                    table.CheckConstraint("ck_fragrance_variants_compare_at_above_price", "compare_at_price_amount IS NULL OR (price_amount IS NOT NULL AND compare_at_price_amount > price_amount)");
                    table.CheckConstraint("ck_fragrance_variants_price_positive", "price_amount IS NULL OR price_amount > 0");
                    table.CheckConstraint("ck_fragrance_variants_size_positive", "size_ml IS NULL OR size_ml > 0");
                    table.ForeignKey(
                        name: "fk_fragrance_variants_fragrances_fragrance_id",
                        column: x => x.fragrance_id,
                        principalSchema: "catalog",
                        principalTable: "fragrances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_brands_name",
                schema: "catalog",
                table: "brands",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_brands_slug",
                schema: "catalog",
                table: "brands",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fragrance_variants_fragrance_id",
                schema: "catalog",
                table: "fragrance_variants",
                column: "fragrance_id");

            migrationBuilder.CreateIndex(
                name: "ux_fragrance_variants_ean",
                schema: "catalog",
                table: "fragrance_variants",
                column: "ean",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_fragrance_variants_sku",
                schema: "catalog",
                table: "fragrance_variants",
                column: "sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fragrances_notes",
                schema: "catalog",
                table: "fragrances",
                column: "notes")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "jsonb_path_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_fragrances_status",
                schema: "catalog",
                table: "fragrances",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ux_fragrances_brand_id_name_concentration",
                schema: "catalog",
                table: "fragrances",
                columns: new[] { "brand_id", "name", "concentration" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_fragrances_fragella_id",
                schema: "catalog",
                table: "fragrances",
                column: "fragella_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_fragrances_slug",
                schema: "catalog",
                table: "fragrances",
                column: "slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fragrance_variants",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "fragrances",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "brands",
                schema: "catalog");
        }
    }
}
