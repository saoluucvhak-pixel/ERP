using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase8AssetMaintenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "maintenance_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    number = table.Column<string>(type: "TEXT", nullable: false),
                    asset_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    priority = table.Column<int>(type: "INTEGER", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: false),
                    reported_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    note = table.Column<string>(type: "TEXT", nullable: true),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    row_version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_maintenance_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_maintenance_requests_long_term_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "long_term_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "preventive_schedules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    asset_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    task_name = table.Column<string>(type: "TEXT", nullable: false),
                    frequency_days = table.Column<int>(type: "INTEGER", nullable: false),
                    last_performed_at = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    next_due_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                    row_version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_preventive_schedules", x => x.id);
                    table.ForeignKey(
                        name: "fk_preventive_schedules_long_term_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "long_term_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "asset_repairs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    number = table.Column<string>(type: "TEXT", nullable: false),
                    asset_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    request_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    schedule_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    type = table.Column<int>(type: "INTEGER", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    reported_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: false),
                    vendor = table.Column<string>(type: "TEXT", nullable: true),
                    assigned_tech = table.Column<string>(type: "TEXT", nullable: true),
                    root_cause = table.Column<string>(type: "TEXT", nullable: true),
                    solution_plan = table.Column<string>(type: "TEXT", nullable: true),
                    estimated_labor_cost = table.Column<long>(type: "INTEGER", nullable: false),
                    estimated_material_cost = table.Column<long>(type: "INTEGER", nullable: false),
                    actual_labor_cost = table.Column<long>(type: "INTEGER", nullable: true),
                    approved_by = table.Column<Guid>(type: "TEXT", nullable: true),
                    approved_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    start_time = table.Column<DateTime>(type: "TEXT", nullable: true),
                    end_time = table.Column<DateTime>(type: "TEXT", nullable: true),
                    note = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    closed_by = table.Column<Guid>(type: "TEXT", nullable: true),
                    closed_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    row_version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asset_repairs", x => x.id);
                    table.ForeignKey(
                        name: "fk_asset_repairs_long_term_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "long_term_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_asset_repairs_maintenance_requests_request_id",
                        column: x => x.request_id,
                        principalTable: "maintenance_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_asset_repairs_preventive_schedules_schedule_id",
                        column: x => x.schedule_id,
                        principalTable: "preventive_schedules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "asset_repair_materials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    repair_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    item_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    qty_requested = table.Column<long>(type: "INTEGER", nullable: false),
                    qty_issued = table.Column<long>(type: "INTEGER", nullable: false),
                    unit_price = table.Column<long>(type: "INTEGER", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    stock_document_id = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asset_repair_materials", x => x.id);
                    table.ForeignKey(
                        name: "fk_asset_repair_materials_asset_repairs_repair_id",
                        column: x => x.repair_id,
                        principalTable: "asset_repairs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_asset_repair_materials_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_asset_repair_materials_item_id",
                table: "asset_repair_materials",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_asset_repair_materials_repair_id",
                table: "asset_repair_materials",
                column: "repair_id");

            migrationBuilder.CreateIndex(
                name: "ix_asset_repairs_asset_id",
                table: "asset_repairs",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_asset_repairs_company_id_asset_id",
                table: "asset_repairs",
                columns: new[] { "company_id", "asset_id" });

            migrationBuilder.CreateIndex(
                name: "ix_asset_repairs_company_id_number",
                table: "asset_repairs",
                columns: new[] { "company_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_asset_repairs_request_id",
                table: "asset_repairs",
                column: "request_id");

            migrationBuilder.CreateIndex(
                name: "ix_asset_repairs_schedule_id",
                table: "asset_repairs",
                column: "schedule_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_requests_asset_id",
                table: "maintenance_requests",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_requests_company_id_asset_id",
                table: "maintenance_requests",
                columns: new[] { "company_id", "asset_id" });

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_requests_company_id_number",
                table: "maintenance_requests",
                columns: new[] { "company_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_preventive_schedules_asset_id",
                table: "preventive_schedules",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_preventive_schedules_company_id_asset_id",
                table: "preventive_schedules",
                columns: new[] { "company_id", "asset_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "asset_repair_materials");

            migrationBuilder.DropTable(
                name: "asset_repairs");

            migrationBuilder.DropTable(
                name: "maintenance_requests");

            migrationBuilder.DropTable(
                name: "preventive_schedules");
        }
    }
}
