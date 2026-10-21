using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase12Reservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stock_reservations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    item_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    order_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    order_line_no = table.Column<int>(type: "INTEGER", nullable: false),
                    quantity = table.Column<long>(type: "INTEGER", nullable: false),
                    consumed_quantity = table.Column<long>(type: "INTEGER", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    closed_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    closed_by = table.Column<Guid>(type: "TEXT", nullable: true),
                    close_reason = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_reservations", x => x.id);
                    table.CheckConstraint("ck_stock_reservations_quantity", "quantity > 0 AND consumed_quantity >= 0 AND consumed_quantity <= quantity");
                    table.ForeignKey(
                        name: "fk_stock_reservations_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_reservations_trade_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "trade_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_reservations_warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_company_id_item_id_warehouse_id_status",
                table: "stock_reservations",
                columns: new[] { "company_id", "item_id", "warehouse_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_company_id_order_id_order_line_no",
                table: "stock_reservations",
                columns: new[] { "company_id", "order_id", "order_line_no" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_item_id",
                table: "stock_reservations",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_order_id",
                table: "stock_reservations",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_warehouse_id",
                table: "stock_reservations",
                column: "warehouse_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_reservations");
        }
    }
}
