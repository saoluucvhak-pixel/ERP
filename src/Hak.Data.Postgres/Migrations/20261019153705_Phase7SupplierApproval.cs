using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Phase7SupplierApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "require_supplier_approval",
                table: "companies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "supplier_decided_at",
                table: "business_partners",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "supplier_decided_by",
                table: "business_partners",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "supplier_status",
                table: "business_partners",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "supplier_status_reason",
                table: "business_partners",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "require_supplier_approval",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "supplier_decided_at",
                table: "business_partners");

            migrationBuilder.DropColumn(
                name: "supplier_decided_by",
                table: "business_partners");

            migrationBuilder.DropColumn(
                name: "supplier_status",
                table: "business_partners");

            migrationBuilder.DropColumn(
                name: "supplier_status_reason",
                table: "business_partners");
        }
    }
}
