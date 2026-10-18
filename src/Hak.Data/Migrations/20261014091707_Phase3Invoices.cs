using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase3Invoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "commercial_invoices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    kind = table.Column<int>(type: "INTEGER", nullable: false),
                    number = table.Column<string>(type: "TEXT", nullable: false),
                    invoice_series = table.Column<string>(type: "TEXT", nullable: false),
                    invoice_no = table.Column<string>(type: "TEXT", nullable: false),
                    invoice_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    posting_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    due_date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    partner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    currency = table.Column<string>(type: "TEXT", nullable: true),
                    exchange_rate = table.Column<decimal>(type: "TEXT", nullable: true),
                    description = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    voucher_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    posted_by = table.Column<Guid>(type: "TEXT", nullable: true),
                    posted_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    row_version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_commercial_invoices", x => x.id);
                    table.ForeignKey(
                        name: "fk_commercial_invoices_business_partners_partner_id",
                        column: x => x.partner_id,
                        principalTable: "business_partners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "commercial_invoice_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    invoice_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    line_no = table.Column<int>(type: "INTEGER", nullable: false),
                    item_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    stock_document_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    stock_line_no = table.Column<int>(type: "INTEGER", nullable: true),
                    stock_quantity = table.Column<long>(type: "INTEGER", nullable: true),
                    account_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    basis = table.Column<int>(type: "INTEGER", nullable: false),
                    basis_quantity = table.Column<long>(type: "INTEGER", nullable: false),
                    unit_price = table.Column<decimal>(type: "TEXT", nullable: false),
                    amount = table.Column<long>(type: "INTEGER", nullable: false),
                    functional_amount = table.Column<long>(type: "INTEGER", nullable: false),
                    vat_rate = table.Column<decimal>(type: "TEXT", nullable: true),
                    vat_amount = table.Column<long>(type: "INTEGER", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_commercial_invoice_lines", x => x.id);
                    table.CheckConstraint("ck_commercial_invoice_lines_amount", "amount >= 0 AND vat_amount >= 0");
                    table.ForeignKey(
                        name: "fk_commercial_invoice_lines_commercial_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "commercial_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_commercial_invoice_lines_company_id_stock_document_id_stock_line_no",
                table: "commercial_invoice_lines",
                columns: new[] { "company_id", "stock_document_id", "stock_line_no" });

            migrationBuilder.CreateIndex(
                name: "ix_commercial_invoice_lines_invoice_id_line_no",
                table: "commercial_invoice_lines",
                columns: new[] { "invoice_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_commercial_invoices_company_id_kind_partner_id_invoice_series_invoice_no",
                table: "commercial_invoices",
                columns: new[] { "company_id", "kind", "partner_id", "invoice_series", "invoice_no" });

            migrationBuilder.CreateIndex(
                name: "ix_commercial_invoices_company_id_number",
                table: "commercial_invoices",
                columns: new[] { "company_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_commercial_invoices_partner_id",
                table: "commercial_invoices",
                column: "partner_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "commercial_invoice_lines");

            migrationBuilder.DropTable(
                name: "commercial_invoices");
        }
    }
}
