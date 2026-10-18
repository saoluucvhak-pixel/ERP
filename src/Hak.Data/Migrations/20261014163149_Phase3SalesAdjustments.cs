using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase3SalesAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "adjusts_invoice_id",
                table: "commercial_invoices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "purpose",
                table: "commercial_invoices",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "adjusts_line_no",
                table: "commercial_invoice_lines",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_commercial_invoices_adjusts_invoice_id",
                table: "commercial_invoices",
                column: "adjusts_invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_commercial_invoices_company_id_adjusts_invoice_id",
                table: "commercial_invoices",
                columns: new[] { "company_id", "adjusts_invoice_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_commercial_invoices_commercial_invoices_adjusts_invoice_id",
                table: "commercial_invoices",
                column: "adjusts_invoice_id",
                principalTable: "commercial_invoices",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_commercial_invoices_commercial_invoices_adjusts_invoice_id",
                table: "commercial_invoices");

            migrationBuilder.DropIndex(
                name: "ix_commercial_invoices_adjusts_invoice_id",
                table: "commercial_invoices");

            migrationBuilder.DropIndex(
                name: "ix_commercial_invoices_company_id_adjusts_invoice_id",
                table: "commercial_invoices");

            migrationBuilder.DropColumn(
                name: "adjusts_invoice_id",
                table: "commercial_invoices");

            migrationBuilder.DropColumn(
                name: "purpose",
                table: "commercial_invoices");

            migrationBuilder.DropColumn(
                name: "adjusts_line_no",
                table: "commercial_invoice_lines");
        }
    }
}
