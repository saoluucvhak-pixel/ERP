using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase7SalesDispatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sales_dispatch_orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    number = table.Column<string>(type: "TEXT", nullable: false),
                    order_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    partner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    dispatch_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ship_to = table.Column<string>(type: "TEXT", nullable: true),
                    note = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    cancel_reason = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    issued_by = table.Column<Guid>(type: "TEXT", nullable: true),
                    issued_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    row_version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_dispatch_orders", x => x.id);
                    table.ForeignKey(
                        name: "fk_sales_dispatch_orders_business_partners_partner_id",
                        column: x => x.partner_id,
                        principalTable: "business_partners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_dispatch_orders_trade_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "trade_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_dispatch_orders_warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_deliveries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    number = table.Column<string>(type: "TEXT", nullable: false),
                    dispatch_order_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    stock_document_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    delivery_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ship_to = table.Column<string>(type: "TEXT", nullable: true),
                    carrier = table.Column<string>(type: "TEXT", nullable: true),
                    delivery_staff = table.Column<string>(type: "TEXT", nullable: true),
                    waybill_no = table.Column<string>(type: "TEXT", nullable: true),
                    note = table.Column<string>(type: "TEXT", nullable: true),
                    is_cancelled = table.Column<bool>(type: "INTEGER", nullable: false),
                    cancel_reason = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    row_version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_deliveries", x => x.id);
                    table.ForeignKey(
                        name: "fk_sales_deliveries_sales_dispatch_orders_dispatch_order_id",
                        column: x => x.dispatch_order_id,
                        principalTable: "sales_dispatch_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_deliveries_stock_documents_stock_document_id",
                        column: x => x.stock_document_id,
                        principalTable: "stock_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_dispatch_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    dispatch_order_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    line_no = table.Column<int>(type: "INTEGER", nullable: false),
                    order_line_no = table.Column<int>(type: "INTEGER", nullable: false),
                    item_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    quantity = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_dispatch_lines", x => x.id);
                    table.CheckConstraint("ck_sales_dispatch_lines_qty", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_sales_dispatch_lines_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_dispatch_lines_sales_dispatch_orders_dispatch_order_id",
                        column: x => x.dispatch_order_id,
                        principalTable: "sales_dispatch_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_delivery_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    delivery_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    line_no = table.Column<int>(type: "INTEGER", nullable: false),
                    dispatch_line_no = table.Column<int>(type: "INTEGER", nullable: false),
                    item_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    quantity = table.Column<long>(type: "INTEGER", nullable: false),
                    stock_line_no = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_delivery_lines", x => x.id);
                    table.CheckConstraint("ck_sales_delivery_lines_qty", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_sales_delivery_lines_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_delivery_lines_sales_deliveries_delivery_id",
                        column: x => x.delivery_id,
                        principalTable: "sales_deliveries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sales_deliveries_company_id_dispatch_order_id",
                table: "sales_deliveries",
                columns: new[] { "company_id", "dispatch_order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_deliveries_company_id_number",
                table: "sales_deliveries",
                columns: new[] { "company_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_deliveries_company_id_stock_document_id",
                table: "sales_deliveries",
                columns: new[] { "company_id", "stock_document_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_deliveries_dispatch_order_id",
                table: "sales_deliveries",
                column: "dispatch_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_deliveries_stock_document_id",
                table: "sales_deliveries",
                column: "stock_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_delivery_lines_delivery_id_line_no",
                table: "sales_delivery_lines",
                columns: new[] { "delivery_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_delivery_lines_item_id",
                table: "sales_delivery_lines",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_dispatch_lines_dispatch_order_id_line_no",
                table: "sales_dispatch_lines",
                columns: new[] { "dispatch_order_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_dispatch_lines_item_id",
                table: "sales_dispatch_lines",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_dispatch_orders_company_id_number",
                table: "sales_dispatch_orders",
                columns: new[] { "company_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_dispatch_orders_company_id_order_id",
                table: "sales_dispatch_orders",
                columns: new[] { "company_id", "order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_dispatch_orders_order_id",
                table: "sales_dispatch_orders",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_dispatch_orders_partner_id",
                table: "sales_dispatch_orders",
                column: "partner_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_dispatch_orders_warehouse_id",
                table: "sales_dispatch_orders",
                column: "warehouse_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sales_delivery_lines");

            migrationBuilder.DropTable(
                name: "sales_dispatch_lines");

            migrationBuilder.DropTable(
                name: "sales_deliveries");

            migrationBuilder.DropTable(
                name: "sales_dispatch_orders");
        }
    }
}
