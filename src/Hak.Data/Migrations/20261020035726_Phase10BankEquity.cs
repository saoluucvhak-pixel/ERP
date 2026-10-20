using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase10BankEquity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "company_bank_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    account_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    bank_name = table.Column<string>(type: "TEXT", nullable: false),
                    branch = table.Column<string>(type: "TEXT", nullable: true),
                    account_no = table.Column<string>(type: "TEXT", nullable: false),
                    account_name = table.Column<string>(type: "TEXT", nullable: false),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_bank_accounts", x => x.id);
                    table.ForeignKey(
                        name: "fk_company_bank_accounts_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dividend_resolutions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    number = table.Column<string>(type: "TEXT", nullable: false),
                    resolution_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: false),
                    total_amount = table.Column<long>(type: "INTEGER", nullable: false),
                    pit_rate_percent = table.Column<long>(type: "INTEGER", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    declaration_voucher_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    decision_note = table.Column<string>(type: "TEXT", nullable: true),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    approved_by = table.Column<Guid>(type: "TEXT", nullable: true),
                    approved_at = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dividend_resolutions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "partner_bank_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    partner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    bank_name = table.Column<string>(type: "TEXT", nullable: false),
                    branch = table.Column<string>(type: "TEXT", nullable: true),
                    account_no = table.Column<string>(type: "TEXT", nullable: false),
                    account_name = table.Column<string>(type: "TEXT", nullable: false),
                    is_default = table.Column<bool>(type: "INTEGER", nullable: false),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_partner_bank_accounts", x => x.id);
                    table.ForeignKey(
                        name: "fk_partner_bank_accounts_business_partners_partner_id",
                        column: x => x.partner_id,
                        principalTable: "business_partners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shareholders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    partner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    committed_capital = table.Column<long>(type: "INTEGER", nullable: false),
                    is_individual = table.Column<bool>(type: "INTEGER", nullable: false),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                    note = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shareholders", x => x.id);
                    table.ForeignKey(
                        name: "fk_shareholders_business_partners_partner_id",
                        column: x => x.partner_id,
                        principalTable: "business_partners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "capital_movements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    shareholder_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    kind = table.Column<int>(type: "INTEGER", nullable: false),
                    date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    amount = table.Column<long>(type: "INTEGER", nullable: false),
                    note = table.Column<string>(type: "TEXT", nullable: true),
                    voucher_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    cash_account_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_capital_movements", x => x.id);
                    table.CheckConstraint("ck_capital_movements_amount", "amount > 0");
                    table.ForeignKey(
                        name: "fk_capital_movements_shareholders_shareholder_id",
                        column: x => x.shareholder_id,
                        principalTable: "shareholders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dividend_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    resolution_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    shareholder_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    capital_basis = table.Column<long>(type: "INTEGER", nullable: false),
                    gross = table.Column<long>(type: "INTEGER", nullable: false),
                    pit_withheld = table.Column<long>(type: "INTEGER", nullable: false),
                    net = table.Column<long>(type: "INTEGER", nullable: false),
                    payment_voucher_id = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dividend_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_dividend_lines_dividend_resolutions_resolution_id",
                        column: x => x.resolution_id,
                        principalTable: "dividend_resolutions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_dividend_lines_shareholders_shareholder_id",
                        column: x => x.shareholder_id,
                        principalTable: "shareholders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_capital_movements_company_id_shareholder_id_date",
                table: "capital_movements",
                columns: new[] { "company_id", "shareholder_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_capital_movements_shareholder_id",
                table: "capital_movements",
                column: "shareholder_id");

            migrationBuilder.CreateIndex(
                name: "ix_company_bank_accounts_account_id",
                table: "company_bank_accounts",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_company_bank_accounts_company_id_account_no_bank_name",
                table: "company_bank_accounts",
                columns: new[] { "company_id", "account_no", "bank_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dividend_lines_resolution_id_shareholder_id",
                table: "dividend_lines",
                columns: new[] { "resolution_id", "shareholder_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dividend_lines_shareholder_id",
                table: "dividend_lines",
                column: "shareholder_id");

            migrationBuilder.CreateIndex(
                name: "ix_dividend_resolutions_company_id_number",
                table: "dividend_resolutions",
                columns: new[] { "company_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_partner_bank_accounts_company_id_partner_id_account_no_bank_name",
                table: "partner_bank_accounts",
                columns: new[] { "company_id", "partner_id", "account_no", "bank_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_partner_bank_accounts_partner_id",
                table: "partner_bank_accounts",
                column: "partner_id");

            migrationBuilder.CreateIndex(
                name: "ix_shareholders_company_id_partner_id",
                table: "shareholders",
                columns: new[] { "company_id", "partner_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shareholders_partner_id",
                table: "shareholders",
                column: "partner_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "capital_movements");

            migrationBuilder.DropTable(
                name: "company_bank_accounts");

            migrationBuilder.DropTable(
                name: "dividend_lines");

            migrationBuilder.DropTable(
                name: "partner_bank_accounts");

            migrationBuilder.DropTable(
                name: "dividend_resolutions");

            migrationBuilder.DropTable(
                name: "shareholders");
        }
    }
}
