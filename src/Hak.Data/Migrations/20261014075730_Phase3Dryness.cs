using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase3Dryness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "tracks_dryness",
                table: "items",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "dryness_methods",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    code = table.Column<string>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: false),
                    dryness_decimals = table.Column<int>(type: "INTEGER", nullable: false),
                    bdmt_decimals = table.Column<int>(type: "INTEGER", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    approval_note = table.Column<string>(type: "TEXT", nullable: false),
                    approved_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    approved_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dryness_methods", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock_measurements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    document_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    line_no = table.Column<int>(type: "INTEGER", nullable: false),
                    item_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    kind = table.Column<int>(type: "INTEGER", nullable: false),
                    measured_mt = table.Column<long>(type: "INTEGER", nullable: false),
                    dryness_percent = table.Column<long>(type: "INTEGER", nullable: false),
                    bdmt = table.Column<long>(type: "INTEGER", nullable: false),
                    method_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    evidence_type = table.Column<int>(type: "INTEGER", nullable: false),
                    evidence_no = table.Column<string>(type: "TEXT", nullable: false),
                    evidence_issuer = table.Column<string>(type: "TEXT", nullable: true),
                    evidence_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    note = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    superseded_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    superseded_by = table.Column<Guid>(type: "TEXT", nullable: true),
                    supersede_reason = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_measurements", x => x.id);
                    table.CheckConstraint("ck_stock_measurements_dryness", "dryness_percent > 0 AND dryness_percent <= 1000000");
                    table.CheckConstraint("ck_stock_measurements_mt", "measured_mt > 0");
                    table.ForeignKey(
                        name: "fk_stock_measurements_dryness_methods_method_id",
                        column: x => x.method_id,
                        principalTable: "dryness_methods",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_measurements_stock_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "stock_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_dryness_methods_company_id_code",
                table: "dryness_methods",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_measurements_company_id_document_id_line_no",
                table: "stock_measurements",
                columns: new[] { "company_id", "document_id", "line_no" },
                unique: true,
                filter: "superseded_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stock_measurements_company_id_evidence_no",
                table: "stock_measurements",
                columns: new[] { "company_id", "evidence_no" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_measurements_document_id",
                table: "stock_measurements",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_measurements_method_id",
                table: "stock_measurements",
                column: "method_id");

            // Dryness measurements are evidence: never deleted; a correction supersedes the row exactly once.
            migrationBuilder.Sql("CREATE TRIGGER trg_stock_measurements_no_delete BEFORE DELETE ON stock_measurements BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_stock_measurements_supersede_once BEFORE UPDATE ON stock_measurements
                WHEN OLD.superseded_at IS NOT NULL OR NEW.superseded_at IS NULL
                  OR NEW.measured_mt IS NOT OLD.measured_mt OR NEW.dryness_percent IS NOT OLD.dryness_percent OR NEW.bdmt IS NOT OLD.bdmt
                  OR NEW.evidence_no IS NOT OLD.evidence_no OR NEW.document_id IS NOT OLD.document_id OR NEW.line_no IS NOT OLD.line_no
                BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_stock_measurements_no_delete;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_stock_measurements_supersede_once;");
            migrationBuilder.DropTable(
                name: "stock_measurements");

            migrationBuilder.DropTable(
                name: "dryness_methods");

            migrationBuilder.DropColumn(
                name: "tracks_dryness",
                table: "items");
        }
    }
}
