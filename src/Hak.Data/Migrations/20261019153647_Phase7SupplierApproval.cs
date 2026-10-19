using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
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
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "supplier_decided_at",
                table: "business_partners",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "supplier_decided_by",
                table: "business_partners",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "supplier_status",
                table: "business_partners",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "supplier_status_reason",
                table: "business_partners",
                type: "TEXT",
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
