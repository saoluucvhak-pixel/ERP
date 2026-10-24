using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Phase17IdempotencyPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "void_effective_date",
                table: "settlement_allocations",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "required_permissions",
                table: "idempotency_records",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "partner_override_reason",
                table: "commercial_invoices",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "void_effective_date",
                table: "settlement_allocations");

            migrationBuilder.DropColumn(
                name: "required_permissions",
                table: "idempotency_records");

            migrationBuilder.DropColumn(
                name: "partner_override_reason",
                table: "commercial_invoices");
        }
    }
}
