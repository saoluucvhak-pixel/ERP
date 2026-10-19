using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase8AssetRepairs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "asset_repairs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    number = table.Column<string>(type: "TEXT", nullable: false),
                    asset_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    kind = table.Column<int>(type: "INTEGER", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    reported_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: false),
                    vendor = table.Column<string>(type: "TEXT", nullable: true),
                    estimated_cost = table.Column<long>(type: "INTEGER", nullable: false),
                    actual_cost = table.Column<long>(type: "INTEGER", nullable: true),
                    completed_date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    voucher_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    note = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    completed_by = table.Column<Guid>(type: "TEXT", nullable: true),
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
                });

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "asset_repairs");
        }
    }
}
