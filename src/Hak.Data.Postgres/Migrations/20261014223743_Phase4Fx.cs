using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Postgres.Migrations
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
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revaluation_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    voucher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rates_json = table.Column<string>(type: "text", nullable: false),
                    rate_source = table.Column<string>(type: "text", nullable: false),
                    result_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    reversed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reversed_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fx_revaluation_runs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fx_revaluation_runs_company_id_revaluation_date",
                table: "fx_revaluation_runs",
                columns: new[] { "company_id", "revaluation_date" });

            migrationBuilder.Sql("CREATE TRIGGER trg_fx_revaluation_runs_no_delete BEFORE DELETE ON fx_revaluation_runs FOR EACH ROW EXECUTE FUNCTION hak_ledger_immutable();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_fx_revaluation_runs_no_delete ON fx_revaluation_runs;");
            migrationBuilder.DropTable(
                name: "fx_revaluation_runs");
        }
    }
}
