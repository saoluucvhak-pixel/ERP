using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase4PriceAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stock_value_adjustments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    item_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    target_movement_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    adjustment_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    value = table.Column<long>(type: "INTEGER", nullable: false),
                    source_invoice_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    reason = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_value_adjustments", x => x.id);
                    table.CheckConstraint("ck_stock_value_adjustments_value", "value <> 0");
                    table.ForeignKey(
                        name: "fk_stock_value_adjustments_stock_movements_target_movement_id",
                        column: x => x.target_movement_id,
                        principalTable: "stock_movements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stock_value_adjustments_company_id_sequence",
                table: "stock_value_adjustments",
                columns: new[] { "company_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_value_adjustments_company_id_warehouse_id_item_id_sequence",
                table: "stock_value_adjustments",
                columns: new[] { "company_id", "warehouse_id", "item_id", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_value_adjustments_target_movement_id",
                table: "stock_value_adjustments",
                column: "target_movement_id");

            migrationBuilder.Sql("CREATE TRIGGER trg_stock_value_adjustments_no_update BEFORE UPDATE ON stock_value_adjustments BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
            migrationBuilder.Sql("CREATE TRIGGER trg_stock_value_adjustments_no_delete BEFORE DELETE ON stock_value_adjustments BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_value_adjustments");
        }
    }
}
