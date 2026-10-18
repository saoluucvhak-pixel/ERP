using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase6Reports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "report_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    code = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    version = table.Column<int>(type: "INTEGER", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    category = table.Column<string>(type: "TEXT", nullable: false),
                    legal_reference = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    verification_note = table.Column<string>(type: "TEXT", nullable: true),
                    verified_by = table.Column<Guid>(type: "TEXT", nullable: true),
                    verified_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    row_version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_report_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "report_checks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    definition_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    line_no = table.Column<int>(type: "INTEGER", nullable: false),
                    rule = table.Column<string>(type: "TEXT", nullable: false),
                    message = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_report_checks", x => x.id);
                    table.ForeignKey(
                        name: "fk_report_checks_report_definitions_definition_id",
                        column: x => x.definition_id,
                        principalTable: "report_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "report_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    definition_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    line_no = table.Column<int>(type: "INTEGER", nullable: false),
                    code = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    formula = table.Column<string>(type: "TEXT", nullable: true),
                    indent = table.Column<int>(type: "INTEGER", nullable: false),
                    bold = table.Column<bool>(type: "INTEGER", nullable: false),
                    note = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_report_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_report_lines_report_definitions_definition_id",
                        column: x => x.definition_id,
                        principalTable: "report_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "report_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    definition_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    definition_code = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    definition_version = table.Column<int>(type: "INTEGER", nullable: false),
                    definition_verified = table.Column<bool>(type: "INTEGER", nullable: false),
                    from_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    to_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    compare_from = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    compare_to = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    result_json = table.Column<string>(type: "TEXT", nullable: false),
                    sha256 = table.Column<string>(type: "TEXT", nullable: false),
                    note = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_report_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "fk_report_snapshots_report_definitions_definition_id",
                        column: x => x.definition_id,
                        principalTable: "report_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_report_checks_definition_id_line_no",
                table: "report_checks",
                columns: new[] { "definition_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_report_definitions_company_id_code_version",
                table: "report_definitions",
                columns: new[] { "company_id", "code", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_report_lines_definition_id_code",
                table: "report_lines",
                columns: new[] { "definition_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_report_lines_definition_id_line_no",
                table: "report_lines",
                columns: new[] { "definition_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_report_snapshots_company_id_definition_code_to_date",
                table: "report_snapshots",
                columns: new[] { "company_id", "definition_code", "to_date" });

            migrationBuilder.CreateIndex(
                name: "ix_report_snapshots_definition_id",
                table: "report_snapshots",
                column: "definition_id");

            foreach (var t in Triggers) migrationBuilder.Sql(t.Create);
        }

        // Verified templates (status 2) and their lines/checks are frozen; snapshots are append-only.
        private static readonly (string Name, string Create)[] Triggers =
        [
            ("trg_report_definitions_frozen_upd", "CREATE TRIGGER trg_report_definitions_frozen_upd BEFORE UPDATE ON report_definitions WHEN OLD.status = 2 BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;"),
            ("trg_report_definitions_frozen_del", "CREATE TRIGGER trg_report_definitions_frozen_del BEFORE DELETE ON report_definitions WHEN OLD.status = 2 BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;"),
            ("trg_report_lines_frozen_ins", "CREATE TRIGGER trg_report_lines_frozen_ins BEFORE INSERT ON report_lines WHEN (SELECT status FROM report_definitions WHERE id = NEW.definition_id) = 2 BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;"),
            ("trg_report_lines_frozen_upd", "CREATE TRIGGER trg_report_lines_frozen_upd BEFORE UPDATE ON report_lines WHEN (SELECT status FROM report_definitions WHERE id = OLD.definition_id) = 2 BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;"),
            ("trg_report_lines_frozen_del", "CREATE TRIGGER trg_report_lines_frozen_del BEFORE DELETE ON report_lines WHEN (SELECT status FROM report_definitions WHERE id = OLD.definition_id) = 2 BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;"),
            ("trg_report_checks_frozen_ins", "CREATE TRIGGER trg_report_checks_frozen_ins BEFORE INSERT ON report_checks WHEN (SELECT status FROM report_definitions WHERE id = NEW.definition_id) = 2 BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;"),
            ("trg_report_checks_frozen_upd", "CREATE TRIGGER trg_report_checks_frozen_upd BEFORE UPDATE ON report_checks WHEN (SELECT status FROM report_definitions WHERE id = OLD.definition_id) = 2 BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;"),
            ("trg_report_checks_frozen_del", "CREATE TRIGGER trg_report_checks_frozen_del BEFORE DELETE ON report_checks WHEN (SELECT status FROM report_definitions WHERE id = OLD.definition_id) = 2 BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;"),
            ("trg_report_snapshots_no_update", "CREATE TRIGGER trg_report_snapshots_no_update BEFORE UPDATE ON report_snapshots BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;"),
            ("trg_report_snapshots_no_delete", "CREATE TRIGGER trg_report_snapshots_no_delete BEFORE DELETE ON report_snapshots BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;"),
        ];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var t in Triggers) migrationBuilder.Sql($"DROP TRIGGER IF EXISTS {t.Name};");
            migrationBuilder.DropTable(
                name: "report_checks");

            migrationBuilder.DropTable(
                name: "report_lines");

            migrationBuilder.DropTable(
                name: "report_snapshots");

            migrationBuilder.DropTable(
                name: "report_definitions");
        }
    }
}
