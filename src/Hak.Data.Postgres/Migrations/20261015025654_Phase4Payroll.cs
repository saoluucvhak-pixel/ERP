using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Phase4Payroll : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payroll_sheets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_system = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    source_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    package_id = table.Column<string>(type: "text", nullable: false),
                    period_from = table.Column<DateOnly>(type: "date", nullable: false),
                    period_to = table.Column<DateOnly>(type: "date", nullable: false),
                    posting_date = table.Column<DateOnly>(type: "date", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    voucher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_sheets", x => x.id);
                    table.ForeignKey(
                        name: "fk_payroll_sheets_vouchers_voucher_id",
                        column: x => x.voucher_id,
                        principalTable: "vouchers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payroll_sheet_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sheet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expense_group = table.Column<int>(type: "integer", nullable: false),
                    gross = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    social_insurance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    health_insurance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    unemployment_insurance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    personal_income_tax = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    other_deduction = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    net = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    employer_social_insurance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    employer_health_insurance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    employer_unemployment_insurance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    employer_union_fee = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_sheet_lines", x => x.id);
                    table.CheckConstraint("ck_payroll_sheet_lines_balance", "gross - social_insurance - health_insurance - unemployment_insurance - personal_income_tax - other_deduction = net");
                    table.ForeignKey(
                        name: "fk_payroll_sheet_lines_business_partners_employee_id",
                        column: x => x.employee_id,
                        principalTable: "business_partners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payroll_sheet_lines_payroll_sheets_sheet_id",
                        column: x => x.sheet_id,
                        principalTable: "payroll_sheets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_sheet_lines_employee_id",
                table: "payroll_sheet_lines",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_sheet_lines_sheet_id_employee_id",
                table: "payroll_sheet_lines",
                columns: new[] { "sheet_id", "employee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payroll_sheet_lines_sheet_id_line_no",
                table: "payroll_sheet_lines",
                columns: new[] { "sheet_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payroll_sheets_company_id_posting_date",
                table: "payroll_sheets",
                columns: new[] { "company_id", "posting_date" });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_sheets_company_id_source_system_source_id_source_ve~",
                table: "payroll_sheets",
                columns: new[] { "company_id", "source_system", "source_id", "source_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payroll_sheets_voucher_id",
                table: "payroll_sheets",
                column: "voucher_id");

            // Payroll sheets are evidence received from the payroll system: append-only.
            migrationBuilder.Sql("CREATE TRIGGER trg_payroll_sheets_append_only BEFORE UPDATE OR DELETE ON payroll_sheets FOR EACH ROW EXECUTE FUNCTION hak_ledger_immutable();");
            migrationBuilder.Sql("CREATE TRIGGER trg_payroll_sheet_lines_append_only BEFORE UPDATE OR DELETE ON payroll_sheet_lines FOR EACH ROW EXECUTE FUNCTION hak_ledger_immutable();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_payroll_sheets_append_only ON payroll_sheets; DROP TRIGGER IF EXISTS trg_payroll_sheet_lines_append_only ON payroll_sheet_lines;");
            migrationBuilder.DropTable(
                name: "payroll_sheet_lines");

            migrationBuilder.DropTable(
                name: "payroll_sheets");
        }
    }
}
