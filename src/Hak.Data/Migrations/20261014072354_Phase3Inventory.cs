using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase3Inventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "costing_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    period_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    period_start = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    period_end = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    voucher_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    result_json = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    reversed_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    reversed_by = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_costing_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    code = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    unit = table.Column<string>(type: "TEXT", nullable: false),
                    inventory_account_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_items_accounts_inventory_account_id",
                        column: x => x.inventory_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "warehouses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    code = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_warehouses", x => x.id);
                    table.ForeignKey(
                        name: "fk_warehouses_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "costing_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    method = table.Column<int>(type: "INTEGER", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    note = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_costing_policies", x => x.id);
                    table.ForeignKey(
                        name: "fk_costing_policies_warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    number = table.Column<string>(type: "TEXT", nullable: false),
                    type = table.Column<int>(type: "INTEGER", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    to_warehouse_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    document_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    posting_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: false),
                    partner_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    voucher_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    reversal_voucher_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    reverses_document_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    posted_by = table.Column<Guid>(type: "TEXT", nullable: true),
                    posted_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    row_version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_documents_warehouses_to_warehouse_id",
                        column: x => x.to_warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_documents_warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_movements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    item_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    document_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    line_no = table.Column<int>(type: "INTEGER", nullable: false),
                    movement_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    quantity = table.Column<long>(type: "INTEGER", nullable: false),
                    counter_account_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    partner_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    reverses_movement_id = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_movements", x => x.id);
                    table.CheckConstraint("ck_stock_movements_quantity", "quantity <> 0");
                    table.ForeignKey(
                        name: "fk_stock_movements_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_movements_warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_document_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    document_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    line_no = table.Column<int>(type: "INTEGER", nullable: false),
                    item_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    quantity = table.Column<long>(type: "INTEGER", nullable: false),
                    amount = table.Column<long>(type: "INTEGER", nullable: true),
                    counter_account_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    description = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_document_lines", x => x.id);
                    table.CheckConstraint("ck_stock_document_lines_quantity", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_stock_document_lines_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_document_lines_stock_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "stock_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_valuations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    movement_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    value = table.Column<long>(type: "INTEGER", nullable: false),
                    costing_run_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    voided_at = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_valuations", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_valuations_stock_movements_movement_id",
                        column: x => x.movement_id,
                        principalTable: "stock_movements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_costing_policies_company_id_warehouse_id_effective_from",
                table: "costing_policies",
                columns: new[] { "company_id", "warehouse_id", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_costing_policies_warehouse_id",
                table: "costing_policies",
                column: "warehouse_id");

            migrationBuilder.CreateIndex(
                name: "ix_costing_runs_company_id_warehouse_id_period_id",
                table: "costing_runs",
                columns: new[] { "company_id", "warehouse_id", "period_id" },
                unique: true,
                filter: "status = 1");

            migrationBuilder.CreateIndex(
                name: "ix_items_company_id_code",
                table: "items",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_items_inventory_account_id",
                table: "items",
                column: "inventory_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_document_lines_document_id_line_no",
                table: "stock_document_lines",
                columns: new[] { "document_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_document_lines_item_id",
                table: "stock_document_lines",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_documents_company_id_number",
                table: "stock_documents",
                columns: new[] { "company_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_documents_company_id_reverses_document_id",
                table: "stock_documents",
                columns: new[] { "company_id", "reverses_document_id" },
                unique: true,
                filter: "reverses_document_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stock_documents_to_warehouse_id",
                table: "stock_documents",
                column: "to_warehouse_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_documents_warehouse_id",
                table: "stock_documents",
                column: "warehouse_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_company_id_document_id",
                table: "stock_movements",
                columns: new[] { "company_id", "document_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_company_id_sequence",
                table: "stock_movements",
                columns: new[] { "company_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_company_id_warehouse_id_item_id_sequence",
                table: "stock_movements",
                columns: new[] { "company_id", "warehouse_id", "item_id", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_item_id",
                table: "stock_movements",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_warehouse_id",
                table: "stock_movements",
                column: "warehouse_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_valuations_company_id_movement_id",
                table: "stock_valuations",
                columns: new[] { "company_id", "movement_id" },
                unique: true,
                filter: "voided_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stock_valuations_movement_id",
                table: "stock_valuations",
                column: "movement_id");

            migrationBuilder.CreateIndex(
                name: "ix_warehouses_company_id_code",
                table: "warehouses",
                columns: new[] { "company_id", "code" },
                unique: true);

            // Stock ledger: quantities are immutable; valuations can only be voided once (month-end recosting).
            migrationBuilder.Sql("CREATE TRIGGER trg_stock_movements_no_update BEFORE UPDATE ON stock_movements BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
            migrationBuilder.Sql("CREATE TRIGGER trg_stock_movements_no_delete BEFORE DELETE ON stock_movements BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
            migrationBuilder.Sql("CREATE TRIGGER trg_stock_valuations_no_delete BEFORE DELETE ON stock_valuations BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_stock_valuations_void_once BEFORE UPDATE ON stock_valuations
                WHEN OLD.voided_at IS NOT NULL OR NEW.voided_at IS NULL OR NEW.value IS NOT OLD.value
                  OR NEW.movement_id IS NOT OLD.movement_id OR NEW.costing_run_id IS NOT OLD.costing_run_id
                BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;
                """);
            migrationBuilder.Sql("CREATE TRIGGER trg_costing_runs_no_delete BEFORE DELETE ON costing_runs BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var t in new[] { "trg_stock_movements_no_update", "trg_stock_movements_no_delete", "trg_stock_valuations_no_delete", "trg_stock_valuations_void_once", "trg_costing_runs_no_delete" })
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS {t};");
            migrationBuilder.DropTable(
                name: "costing_policies");

            migrationBuilder.DropTable(
                name: "costing_runs");

            migrationBuilder.DropTable(
                name: "stock_document_lines");

            migrationBuilder.DropTable(
                name: "stock_valuations");

            migrationBuilder.DropTable(
                name: "stock_documents");

            migrationBuilder.DropTable(
                name: "stock_movements");

            migrationBuilder.DropTable(
                name: "items");

            migrationBuilder.DropTable(
                name: "warehouses");
        }
    }
}
