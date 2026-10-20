using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Phase9ExpenseSplit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "asset_expense_splits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expense_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "text", nullable: true),
                    percent = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asset_expense_splits", x => x.id);
                    table.ForeignKey(
                        name: "fk_asset_expense_splits_accounts_expense_account_id",
                        column: x => x.expense_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_asset_expense_splits_long_term_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "long_term_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "depreciation_entry_splits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expense_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "text", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_depreciation_entry_splits", x => x.id);
                    table.ForeignKey(
                        name: "fk_depreciation_entry_splits_accounts_expense_account_id",
                        column: x => x.expense_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_depreciation_entry_splits_depreciation_entries_entry_id",
                        column: x => x.entry_id,
                        principalTable: "depreciation_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_asset_expense_splits_asset_id_expense_account_id_label",
                table: "asset_expense_splits",
                columns: new[] { "asset_id", "expense_account_id", "label" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_asset_expense_splits_expense_account_id",
                table: "asset_expense_splits",
                column: "expense_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_depreciation_entry_splits_company_id_expense_account_id",
                table: "depreciation_entry_splits",
                columns: new[] { "company_id", "expense_account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_depreciation_entry_splits_entry_id",
                table: "depreciation_entry_splits",
                column: "entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_depreciation_entry_splits_expense_account_id",
                table: "depreciation_entry_splits",
                column: "expense_account_id");

            // Số chia chi phí đã ghi theo từng dòng khấu hao là chứng cứ: chỉ thêm, không sửa, không xóa.
            migrationBuilder.Sql("CREATE TRIGGER trg_depreciation_entry_splits_append_only BEFORE UPDATE OR DELETE ON depreciation_entry_splits FOR EACH ROW EXECUTE FUNCTION hak_ledger_immutable();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_depreciation_entry_splits_append_only ON depreciation_entry_splits;");
            migrationBuilder.DropTable(
                name: "asset_expense_splits");

            migrationBuilder.DropTable(
                name: "depreciation_entry_splits");
        }
    }
}
