using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Postgres.Migrations
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
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "text", nullable: false),
                    legal_reference = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    verification_note = table.Column<string>(type: "text", nullable: true),
                    verified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    verified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_report_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "report_checks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    rule = table.Column<string>(type: "text", nullable: false),
                    message = table.Column<string>(type: "text", nullable: false)
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
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    formula = table.Column<string>(type: "text", nullable: true),
                    indent = table.Column<int>(type: "integer", nullable: false),
                    bold = table.Column<bool>(type: "boolean", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true)
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
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    definition_version = table.Column<int>(type: "integer", nullable: false),
                    definition_verified = table.Column<bool>(type: "boolean", nullable: false),
                    from_date = table.Column<DateOnly>(type: "date", nullable: false),
                    to_date = table.Column<DateOnly>(type: "date", nullable: false),
                    compare_from = table.Column<DateOnly>(type: "date", nullable: true),
                    compare_to = table.Column<DateOnly>(type: "date", nullable: true),
                    result_json = table.Column<string>(type: "text", nullable: false),
                    sha256 = table.Column<string>(type: "text", nullable: false),
                    note = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false)
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

            foreach (var sql in ImmutabilityTriggers.ReportStatements()) migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in ImmutabilityTriggers.ReportDownStatements()) migrationBuilder.Sql(sql);
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
