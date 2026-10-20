using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase10Advances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "advance_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    number = table.Column<string>(type: "TEXT", nullable: false),
                    employee_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    purpose = table.Column<string>(type: "TEXT", nullable: false),
                    amount = table.Column<long>(type: "INTEGER", nullable: false),
                    request_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    due_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    decision_note = table.Column<string>(type: "TEXT", nullable: true),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    decided_by = table.Column<Guid>(type: "TEXT", nullable: true),
                    decided_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    payment_voucher_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    settlement_voucher_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    spent_amount = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_advance_requests", x => x.id);
                    table.CheckConstraint("ck_advance_requests_amount", "amount > 0");
                    table.ForeignKey(
                        name: "fk_advance_requests_business_partners_employee_id",
                        column: x => x.employee_id,
                        principalTable: "business_partners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cash_counts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    number = table.Column<string>(type: "TEXT", nullable: false),
                    cash_account_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    count_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    counted_total = table.Column<long>(type: "INTEGER", nullable: false),
                    book_balance = table.Column<long>(type: "INTEGER", nullable: true),
                    difference = table.Column<long>(type: "INTEGER", nullable: true),
                    note = table.Column<string>(type: "TEXT", nullable: true),
                    participants = table.Column<string>(type: "TEXT", nullable: true),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    confirmed_by = table.Column<Guid>(type: "TEXT", nullable: true),
                    confirmed_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    adjustment_voucher_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    cancel_reason = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cash_counts", x => x.id);
                    table.ForeignKey(
                        name: "fk_cash_counts_accounts_cash_account_id",
                        column: x => x.cash_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "advance_settlement_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    advance_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    line_no = table.Column<int>(type: "INTEGER", nullable: false),
                    expense_account_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    amount = table.Column<long>(type: "INTEGER", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: true),
                    document_no = table.Column<string>(type: "TEXT", nullable: true),
                    document_date = table.Column<DateOnly>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_advance_settlement_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_advance_settlement_lines_accounts_expense_account_id",
                        column: x => x.expense_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_advance_settlement_lines_advance_requests_advance_id",
                        column: x => x.advance_id,
                        principalTable: "advance_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cash_count_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    cash_count_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    denomination = table.Column<long>(type: "INTEGER", nullable: false),
                    quantity = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cash_count_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_cash_count_lines_cash_counts_cash_count_id",
                        column: x => x.cash_count_id,
                        principalTable: "cash_counts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_advance_requests_company_id_employee_id_status",
                table: "advance_requests",
                columns: new[] { "company_id", "employee_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_advance_requests_company_id_number",
                table: "advance_requests",
                columns: new[] { "company_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_advance_requests_employee_id",
                table: "advance_requests",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_advance_settlement_lines_advance_id_line_no",
                table: "advance_settlement_lines",
                columns: new[] { "advance_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_advance_settlement_lines_expense_account_id",
                table: "advance_settlement_lines",
                column: "expense_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_cash_count_lines_cash_count_id_denomination",
                table: "cash_count_lines",
                columns: new[] { "cash_count_id", "denomination" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cash_counts_cash_account_id",
                table: "cash_counts",
                column: "cash_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_cash_counts_company_id_cash_account_id_count_date",
                table: "cash_counts",
                columns: new[] { "company_id", "cash_account_id", "count_date" });

            migrationBuilder.CreateIndex(
                name: "ix_cash_counts_company_id_number",
                table: "cash_counts",
                columns: new[] { "company_id", "number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "advance_settlement_lines");

            migrationBuilder.DropTable(
                name: "cash_count_lines");

            migrationBuilder.DropTable(
                name: "advance_requests");

            migrationBuilder.DropTable(
                name: "cash_counts");
        }
    }
}
