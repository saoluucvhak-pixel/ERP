using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Phase10SalesApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "source_invoice_id",
                table: "stock_document_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "source_invoice_line_no",
                table: "stock_document_lines",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "requires_qc",
                table: "items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "require_sales_adjustment_approval",
                table: "companies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "adjustment_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "text", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    decision_note = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_adjustment_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_adjustment_requests_commercial_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "commercial_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "debt_write_offs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "text", nullable: false),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    basis = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    decision_note = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    voucher_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_debt_write_offs", x => x.id);
                    table.ForeignKey(
                        name: "fk_debt_write_offs_business_partners_partner_id",
                        column: x => x.partner_id,
                        principalTable: "business_partners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "adjustment_request_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_line_no = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_adjustment_request_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_adjustment_request_lines_adjustment_requests_request_id",
                        column: x => x.request_id,
                        principalTable: "adjustment_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "debt_write_off_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    write_off_id = table.Column<Guid>(type: "uuid", nullable: false),
                    journal_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    provision_used = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_debt_write_off_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_debt_write_off_items_debt_write_offs_write_off_id",
                        column: x => x.write_off_id,
                        principalTable: "debt_write_offs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_adjustment_request_lines_request_id_original_line_no",
                table: "adjustment_request_lines",
                columns: new[] { "request_id", "original_line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_adjustment_requests_company_id_invoice_id_status",
                table: "adjustment_requests",
                columns: new[] { "company_id", "invoice_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_adjustment_requests_company_id_number",
                table: "adjustment_requests",
                columns: new[] { "company_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_adjustment_requests_invoice_id",
                table: "adjustment_requests",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_debt_write_off_items_write_off_id_journal_line_id",
                table: "debt_write_off_items",
                columns: new[] { "write_off_id", "journal_line_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_debt_write_offs_company_id_number",
                table: "debt_write_offs",
                columns: new[] { "company_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_debt_write_offs_partner_id",
                table: "debt_write_offs",
                column: "partner_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "adjustment_request_lines");

            migrationBuilder.DropTable(
                name: "debt_write_off_items");

            migrationBuilder.DropTable(
                name: "adjustment_requests");

            migrationBuilder.DropTable(
                name: "debt_write_offs");

            migrationBuilder.DropColumn(
                name: "source_invoice_id",
                table: "stock_document_lines");

            migrationBuilder.DropColumn(
                name: "source_invoice_line_no",
                table: "stock_document_lines");

            migrationBuilder.DropColumn(
                name: "requires_qc",
                table: "items");

            migrationBuilder.DropColumn(
                name: "require_sales_adjustment_approval",
                table: "companies");
        }
    }
}
