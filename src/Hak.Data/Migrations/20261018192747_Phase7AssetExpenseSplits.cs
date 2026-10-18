using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase7AssetExpenseSplits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "asset_expense_splits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    asset_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    expense_account_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    basis_points = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asset_expense_splits", x => x.id);
                    table.CheckConstraint("ck_asset_expense_splits_share", "basis_points BETWEEN 1 AND 10000");
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

            migrationBuilder.CreateIndex(
                name: "ix_asset_expense_splits_asset_id_expense_account_id",
                table: "asset_expense_splits",
                columns: new[] { "asset_id", "expense_account_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_asset_expense_splits_expense_account_id",
                table: "asset_expense_splits",
                column: "expense_account_id");

            // Append-only like asset_cost_sources: the split is part of the frozen card.
            migrationBuilder.Sql("CREATE TRIGGER trg_asset_expense_splits_no_update BEFORE UPDATE ON asset_expense_splits BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
            migrationBuilder.Sql("CREATE TRIGGER trg_asset_expense_splits_no_delete BEFORE DELETE ON asset_expense_splits BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_asset_expense_splits_no_update;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_asset_expense_splits_no_delete;");
            migrationBuilder.DropTable(
                name: "asset_expense_splits");
        }
    }
}
