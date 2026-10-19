using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Postgres.Migrations
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
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "text", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    reported_date = table.Column<DateOnly>(type: "date", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    vendor = table.Column<string>(type: "text", nullable: true),
                    estimated_cost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    actual_cost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    completed_date = table.Column<DateOnly>(type: "date", nullable: true),
                    voucher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    completed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
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
