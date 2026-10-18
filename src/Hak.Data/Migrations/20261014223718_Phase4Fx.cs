using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase4Fx : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fx_revaluation_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    revaluation_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    voucher_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    rates_json = table.Column<string>(type: "TEXT", nullable: false),
                    rate_source = table.Column<string>(type: "TEXT", nullable: false),
                    result_json = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    reversed_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    reversed_by = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fx_revaluation_runs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fx_revaluation_runs_company_id_revaluation_date",
                table: "fx_revaluation_runs",
                columns: new[] { "company_id", "revaluation_date" });

            migrationBuilder.Sql("CREATE TRIGGER trg_fx_revaluation_runs_no_delete BEFORE DELETE ON fx_revaluation_runs BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_fx_revaluation_runs_no_delete;");
            migrationBuilder.DropTable(
                name: "fx_revaluation_runs");
        }
    }
}
