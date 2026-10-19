using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Phase8ProductionOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "boms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_boms", x => x.id);
                    table.ForeignKey(
                        name: "fk_boms_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "production_orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "text", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cost_object_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    planned_quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    planned_start = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    actual_quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    completed_date = table.Column<DateOnly>(type: "date", nullable: true),
                    material_cost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    labor_cost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    overhead_cost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    total_cost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    receipt_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    allocation_voucher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cancel_reason = table.Column<string>(type: "text", nullable: true),
                    released_by = table.Column<Guid>(type: "uuid", nullable: true),
                    released_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_production_orders", x => x.id);
                    table.ForeignKey(
                        name: "fk_production_orders_cost_objects_cost_object_id",
                        column: x => x.cost_object_id,
                        principalTable: "cost_objects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_production_orders_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_production_orders_warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "work_centers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    labor_rate_per_hour = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    overhead_rate_per_hour = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_centers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bom_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bom_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    raw_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity_per_unit = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    scrap_rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bom_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_bom_lines_boms_bom_id",
                        column: x => x.bom_id,
                        principalTable: "boms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_bom_lines_items_raw_item_id",
                        column: x => x.raw_item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "production_order_issues",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_document_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_production_order_issues", x => x.id);
                    table.ForeignKey(
                        name: "fk_production_order_issues_production_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "production_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_production_order_issues_stock_documents_stock_document_id",
                        column: x => x.stock_document_id,
                        principalTable: "stock_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "production_confirmations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_date = table.Column<DateOnly>(type: "date", nullable: false),
                    labor_hours = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    machine_hours = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    labor_rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    overhead_rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    labor_cost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    overhead_cost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    voided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    void_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_production_confirmations", x => x.id);
                    table.ForeignKey(
                        name: "fk_production_confirmations_production_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "production_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_production_confirmations_work_centers_work_center_id",
                        column: x => x.work_center_id,
                        principalTable: "work_centers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bom_lines_bom_id_raw_item_id",
                table: "bom_lines",
                columns: new[] { "bom_id", "raw_item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bom_lines_raw_item_id",
                table: "bom_lines",
                column: "raw_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_boms_company_id_item_id",
                table: "boms",
                columns: new[] { "company_id", "item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_boms_item_id",
                table: "boms",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_production_confirmations_order_id_work_date",
                table: "production_confirmations",
                columns: new[] { "order_id", "work_date" });

            migrationBuilder.CreateIndex(
                name: "ix_production_confirmations_work_center_id",
                table: "production_confirmations",
                column: "work_center_id");

            migrationBuilder.CreateIndex(
                name: "ix_production_order_issues_order_id",
                table: "production_order_issues",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_production_order_issues_stock_document_id",
                table: "production_order_issues",
                column: "stock_document_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_production_orders_company_id_number",
                table: "production_orders",
                columns: new[] { "company_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_production_orders_company_id_status",
                table: "production_orders",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_production_orders_cost_object_id",
                table: "production_orders",
                column: "cost_object_id");

            migrationBuilder.CreateIndex(
                name: "ix_production_orders_item_id",
                table: "production_orders",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_production_orders_warehouse_id",
                table: "production_orders",
                column: "warehouse_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_centers_company_id_code",
                table: "work_centers",
                columns: new[] { "company_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bom_lines");

            migrationBuilder.DropTable(
                name: "production_confirmations");

            migrationBuilder.DropTable(
                name: "production_order_issues");

            migrationBuilder.DropTable(
                name: "boms");

            migrationBuilder.DropTable(
                name: "work_centers");

            migrationBuilder.DropTable(
                name: "production_orders");
        }
    }
}
