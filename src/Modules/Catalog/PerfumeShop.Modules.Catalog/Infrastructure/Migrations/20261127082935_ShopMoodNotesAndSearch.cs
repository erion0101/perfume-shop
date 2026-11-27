using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PerfumeShop.Modules.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ShopMoodNotesAndSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.AddColumn<string>(
                name: "mood",
                schema: "catalog",
                table: "fragrances",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mood_override",
                schema: "catalog",
                table: "fragrances",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "note_keys",
                schema: "catalog",
                table: "fragrances",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            // Existing rows: lower-case note names from the jsonb pyramid. Moods are filled on startup
            // (CatalogModule.InitializeAsync), because the classifier lives in C#.
            migrationBuilder.Sql("""
                UPDATE catalog.fragrances f
                SET note_keys = COALESCE((
                    SELECT array_agg(DISTINCT lower(n ->> 'name'))
                    FROM jsonb_array_elements(
                        COALESCE(f.notes -> 'top', '[]'::jsonb) || COALESCE(f.notes -> 'heart', '[]'::jsonb) || COALESCE(f.notes -> 'base', '[]'::jsonb)) n
                    WHERE n ->> 'name' IS NOT NULL), '{}');
                """);

            migrationBuilder.CreateIndex(
                name: "ix_fragrances_mood",
                schema: "catalog",
                table: "fragrances",
                column: "mood");

            migrationBuilder.CreateIndex(
                name: "ix_fragrances_name",
                schema: "catalog",
                table: "fragrances",
                column: "name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_fragrances_note_keys",
                schema: "catalog",
                table: "fragrances",
                column: "note_keys")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_brands_name",
                schema: "catalog",
                table: "brands",
                column: "name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_fragrances_mood",
                schema: "catalog",
                table: "fragrances");

            migrationBuilder.DropIndex(
                name: "ix_fragrances_name",
                schema: "catalog",
                table: "fragrances");

            migrationBuilder.DropIndex(
                name: "ix_fragrances_note_keys",
                schema: "catalog",
                table: "fragrances");

            migrationBuilder.DropIndex(
                name: "ix_brands_name",
                schema: "catalog",
                table: "brands");

            migrationBuilder.DropColumn(
                name: "mood",
                schema: "catalog",
                table: "fragrances");

            migrationBuilder.DropColumn(
                name: "mood_override",
                schema: "catalog",
                table: "fragrances");

            migrationBuilder.DropColumn(
                name: "note_keys",
                schema: "catalog",
                table: "fragrances");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
        }
    }
}
