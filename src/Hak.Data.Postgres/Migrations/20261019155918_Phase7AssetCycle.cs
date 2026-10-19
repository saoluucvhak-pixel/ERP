using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Phase7AssetCycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "asset_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    department = table.Column<string>(type: "text", nullable: false),
                    custodian = table.Column<string>(type: "text", nullable: true),
                    location = table.Column<string>(type: "text", nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asset_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_asset_assignments_long_term_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "long_term_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "asset_counts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "text", nullable: false),
                    count_date = table.Column<DateOnly>(type: "date", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    completed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asset_counts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "asset_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "text", nullable: false),
                    @class = table.Column<int>(name: "class", type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    department = table.Column<string>(type: "text", nullable: true),
                    estimated_cost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    needed_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "text", nullable: true),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asset_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_asset_requests_long_term_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "long_term_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "asset_count_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    count_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    book_department = table.Column<string>(type: "text", nullable: true),
                    result = table.Column<int>(type: "integer", nullable: false),
                    actual_department = table.Column<string>(type: "text", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asset_count_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_asset_count_lines_asset_counts_count_id",
                        column: x => x.count_id,
                        principalTable: "asset_counts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_asset_count_lines_long_term_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "long_term_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_asset_assignments_asset_id",
                table: "asset_assignments",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_asset_assignments_company_id_asset_id_start_date",
                table: "asset_assignments",
                columns: new[] { "company_id", "asset_id", "start_date" });

            migrationBuilder.CreateIndex(
                name: "ix_asset_count_lines_asset_id",
                table: "asset_count_lines",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_asset_count_lines_count_id_asset_id",
                table: "asset_count_lines",
                columns: new[] { "count_id", "asset_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_asset_counts_company_id_number",
                table: "asset_counts",
                columns: new[] { "company_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_asset_requests_asset_id",
                table: "asset_requests",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_asset_requests_company_id_number",
                table: "asset_requests",
                columns: new[] { "company_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_asset_requests_company_id_status",
                table: "asset_requests",
                columns: new[] { "company_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "asset_assignments");

            migrationBuilder.DropTable(
                name: "asset_count_lines");

            migrationBuilder.DropTable(
                name: "asset_requests");

            migrationBuilder.DropTable(
                name: "asset_counts");
        }
    }
}
