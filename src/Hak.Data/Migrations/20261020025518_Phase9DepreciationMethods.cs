using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase9DepreciationMethods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "declining_factor",
                table: "long_term_assets",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "schedule_end_date",
                table: "long_term_assets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "total_units",
                table: "long_term_assets",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "unit_name",
                table: "long_term_assets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "asset_usages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    asset_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    period_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    quantity = table.Column<long>(type: "INTEGER", nullable: false),
                    recorded_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    recorded_by = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asset_usages", x => x.id);
                    table.ForeignKey(
                        name: "fk_asset_usages_accounting_periods_period_id",
                        column: x => x.period_id,
                        principalTable: "accounting_periods",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_asset_usages_long_term_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "long_term_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_asset_usages_asset_id_period_id",
                table: "asset_usages",
                columns: new[] { "asset_id", "period_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_asset_usages_period_id",
                table: "asset_usages",
                column: "period_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "asset_usages");

            migrationBuilder.DropColumn(
                name: "declining_factor",
                table: "long_term_assets");

            migrationBuilder.DropColumn(
                name: "schedule_end_date",
                table: "long_term_assets");

            migrationBuilder.DropColumn(
                name: "total_units",
                table: "long_term_assets");

            migrationBuilder.DropColumn(
                name: "unit_name",
                table: "long_term_assets");
        }
    }
}
