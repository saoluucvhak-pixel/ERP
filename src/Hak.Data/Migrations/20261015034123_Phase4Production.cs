using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase4Production : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cost_objects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    code = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    item_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    wip_account_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    planned_unit_cost = table.Column<long>(type: "INTEGER", nullable: false),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cost_objects", x => x.id);
                    table.ForeignKey(
                        name: "fk_cost_objects_accounts_wip_account_id",
                        column: x => x.wip_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cost_objects_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "production_cost_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    cost_object_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    period_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    period_start = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    period_end = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    wip_balance_before = table.Column<long>(type: "INTEGER", nullable: false),
                    provisional_value = table.Column<long>(type: "INTEGER", nullable: false),
                    closing_wip = table.Column<long>(type: "INTEGER", nullable: false),
                    output_quantity = table.Column<long>(type: "INTEGER", nullable: false),
                    actual_cost = table.Column<long>(type: "INTEGER", nullable: false),
                    unit_cost = table.Column<decimal>(type: "TEXT", nullable: false),
                    difference = table.Column<long>(type: "INTEGER", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    voucher_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    note = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    reversed_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    reversed_by = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_production_cost_runs", x => x.id);
                    table.ForeignKey(
                        name: "fk_production_cost_runs_cost_objects_cost_object_id",
                        column: x => x.cost_object_id,
                        principalTable: "cost_objects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cost_objects_company_id_code",
                table: "cost_objects",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cost_objects_company_id_wip_account_id",
                table: "cost_objects",
                columns: new[] { "company_id", "wip_account_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cost_objects_item_id",
                table: "cost_objects",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_cost_objects_wip_account_id",
                table: "cost_objects",
                column: "wip_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_production_cost_runs_cost_object_id_period_id",
                table: "production_cost_runs",
                columns: new[] { "cost_object_id", "period_id" },
                unique: true,
                filter: "status = 1");
        
            migrationBuilder.Sql("CREATE TRIGGER trg_production_cost_runs_no_delete BEFORE DELETE ON production_cost_runs BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_production_cost_runs_no_delete;");
            migrationBuilder.DropTable(
                name: "production_cost_runs");

            migrationBuilder.DropTable(
                name: "cost_objects");
        }
    }
}
