using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Postgres.Migrations
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
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expense_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    basis_points = table.Column<int>(type: "integer", nullable: false)
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
        
            foreach (var sql in ImmutabilityTriggers.AssetSplitStatements()) migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in ImmutabilityTriggers.AssetSplitDownStatements()) migrationBuilder.Sql(sql);
            migrationBuilder.DropTable(
                name: "asset_expense_splits");
        }
    }
}
