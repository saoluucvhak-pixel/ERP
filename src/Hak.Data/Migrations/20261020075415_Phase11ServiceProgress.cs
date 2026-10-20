using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase11ServiceProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "service_milestones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    order_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    order_line_no = table.Column<int>(type: "INTEGER", nullable: false),
                    milestone_no = table.Column<int>(type: "INTEGER", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    percentage = table.Column<long>(type: "INTEGER", nullable: false),
                    due_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    acceptance_id = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_milestones", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "service_time_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    order_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    order_line_no = table.Column<int>(type: "INTEGER", nullable: false),
                    work_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    worker = table.Column<string>(type: "TEXT", nullable: false),
                    hours = table.Column<long>(type: "INTEGER", nullable: false),
                    note = table.Column<string>(type: "TEXT", nullable: true),
                    acceptance_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_time_entries", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_service_milestones_acceptance_id",
                table: "service_milestones",
                column: "acceptance_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_milestones_order_id_order_line_no_milestone_no",
                table: "service_milestones",
                columns: new[] { "order_id", "order_line_no", "milestone_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_service_time_entries_acceptance_id",
                table: "service_time_entries",
                column: "acceptance_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_time_entries_order_id_order_line_no_work_date",
                table: "service_time_entries",
                columns: new[] { "order_id", "order_line_no", "work_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "service_milestones");

            migrationBuilder.DropTable(
                name: "service_time_entries");
        }
    }
}
