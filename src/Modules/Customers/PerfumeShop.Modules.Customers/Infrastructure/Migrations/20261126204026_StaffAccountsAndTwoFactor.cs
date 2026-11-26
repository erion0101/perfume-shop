using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PerfumeShop.Modules.Customers.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StaffAccountsAndTwoFactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_deactivated",
                schema: "identity",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_login_at_utc",
                schema: "identity",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "must_change_password",
                schema: "identity",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_deactivated",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "last_login_at_utc",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "must_change_password",
                schema: "identity",
                table: "users");
        }
    }
}
