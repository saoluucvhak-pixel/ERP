using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Postgres.Migrations
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
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    wip_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    planned_unit_cost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false)
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
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cost_object_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    wip_balance_before = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    provisional_value = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    closing_wip = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    output_quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    actual_cost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    difference = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    voucher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    reversed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reversed_by = table.Column<Guid>(type: "uuid", nullable: true)
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
        
            migrationBuilder.Sql("CREATE TRIGGER trg_production_cost_runs_no_delete BEFORE DELETE ON production_cost_runs FOR EACH ROW EXECUTE FUNCTION hak_ledger_immutable();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_production_cost_runs_no_delete ON production_cost_runs;");
            migrationBuilder.DropTable(
                name: "production_cost_runs");

            migrationBuilder.DropTable(
                name: "cost_objects");
        }
    }
}
