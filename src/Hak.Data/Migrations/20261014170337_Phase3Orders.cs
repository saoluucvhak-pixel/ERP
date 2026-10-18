using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase3Orders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "order_id",
                table: "stock_document_lines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "order_line_no",
                table: "stock_document_lines",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "order_id",
                table: "commercial_invoice_lines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "order_line_no",
                table: "commercial_invoice_lines",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "trade_orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    kind = table.Column<int>(type: "INTEGER", nullable: false),
                    number = table.Column<string>(type: "TEXT", nullable: false),
                    partner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    contract_no = table.Column<string>(type: "TEXT", nullable: true),
                    order_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    expected_date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    currency = table.Column<string>(type: "TEXT", nullable: true),
                    description = table.Column<string>(type: "TEXT", nullable: false),
                    over_delivery_tolerance_percent = table.Column<long>(type: "INTEGER", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    status_reason = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    confirmed_by = table.Column<Guid>(type: "TEXT", nullable: true),
                    confirmed_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    row_version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trade_orders", x => x.id);
                    table.CheckConstraint("ck_trade_orders_tolerance", "over_delivery_tolerance_percent >= 0 AND over_delivery_tolerance_percent <= 1000000");
                    table.ForeignKey(
                        name: "fk_trade_orders_business_partners_partner_id",
                        column: x => x.partner_id,
                        principalTable: "business_partners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trade_order_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    order_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    line_no = table.Column<int>(type: "INTEGER", nullable: false),
                    item_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    quantity = table.Column<long>(type: "INTEGER", nullable: false),
                    basis = table.Column<int>(type: "INTEGER", nullable: false),
                    unit_price = table.Column<long>(type: "INTEGER", nullable: false),
                    vat_rate = table.Column<decimal>(type: "TEXT", nullable: true),
                    description = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trade_order_lines", x => x.id);
                    table.CheckConstraint("ck_trade_order_lines_quantity", "quantity > 0 AND unit_price >= 0");
                    table.ForeignKey(
                        name: "fk_trade_order_lines_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_trade_order_lines_trade_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "trade_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stock_document_lines_company_id_order_id_order_line_no",
                table: "stock_document_lines",
                columns: new[] { "company_id", "order_id", "order_line_no" });

            migrationBuilder.CreateIndex(
                name: "ix_trade_order_lines_item_id",
                table: "trade_order_lines",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_trade_order_lines_order_id_line_no",
                table: "trade_order_lines",
                columns: new[] { "order_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trade_orders_company_id_kind_status",
                table: "trade_orders",
                columns: new[] { "company_id", "kind", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_trade_orders_company_id_number",
                table: "trade_orders",
                columns: new[] { "company_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trade_orders_partner_id",
                table: "trade_orders",
                column: "partner_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "trade_order_lines");

            migrationBuilder.DropTable(
                name: "trade_orders");

            migrationBuilder.DropIndex(
                name: "ix_stock_document_lines_company_id_order_id_order_line_no",
                table: "stock_document_lines");

            migrationBuilder.DropColumn(
                name: "order_id",
                table: "stock_document_lines");

            migrationBuilder.DropColumn(
                name: "order_line_no",
                table: "stock_document_lines");

            migrationBuilder.DropColumn(
                name: "order_id",
                table: "commercial_invoice_lines");

            migrationBuilder.DropColumn(
                name: "order_line_no",
                table: "commercial_invoice_lines");
        }
    }
}
