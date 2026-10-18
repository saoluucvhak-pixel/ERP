using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase2Banking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bank_statements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    bank_account_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    from_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    to_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    opening_balance = table.Column<long>(type: "INTEGER", nullable: false),
                    closing_balance = table.Column<long>(type: "INTEGER", nullable: false),
                    source_name = table.Column<string>(type: "TEXT", nullable: false),
                    source_sha256 = table.Column<string>(type: "TEXT", nullable: false),
                    imported_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    imported_by = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_statements", x => x.id);
                    table.ForeignKey(
                        name: "fk_bank_statements_accounts_bank_account_id",
                        column: x => x.bank_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    number = table.Column<string>(type: "TEXT", nullable: false),
                    bank_account_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    partner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    business_event = table.Column<string>(type: "TEXT", nullable: false),
                    amount = table.Column<long>(type: "INTEGER", nullable: false),
                    requested_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: false),
                    beneficiary_account_no = table.Column<string>(type: "TEXT", nullable: true),
                    beneficiary_bank = table.Column<string>(type: "TEXT", nullable: true),
                    settle_journal_line_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    approved_by = table.Column<Guid>(type: "TEXT", nullable: true),
                    sent_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    sent_by = table.Column<Guid>(type: "TEXT", nullable: true),
                    executed_date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    bank_reference = table.Column<string>(type: "TEXT", nullable: true),
                    voucher_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    cancel_reason = table.Column<string>(type: "TEXT", nullable: true),
                    row_version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_orders", x => x.id);
                    table.CheckConstraint("ck_payment_orders_amount", "amount > 0");
                    table.ForeignKey(
                        name: "fk_payment_orders_accounts_bank_account_id",
                        column: x => x.bank_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payment_orders_business_partners_partner_id",
                        column: x => x.partner_id,
                        principalTable: "business_partners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bank_statement_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    statement_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    bank_account_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    line_no = table.Column<int>(type: "INTEGER", nullable: false),
                    transaction_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    amount = table.Column<long>(type: "INTEGER", nullable: false),
                    bank_reference = table.Column<string>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: false),
                    counterparty_name = table.Column<string>(type: "TEXT", nullable: true),
                    counterparty_account = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    candidates_json = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_statement_lines", x => x.id);
                    table.CheckConstraint("ck_bank_statement_lines_amount", "amount <> 0");
                    table.ForeignKey(
                        name: "fk_bank_statement_lines_bank_statements_statement_id",
                        column: x => x.statement_id,
                        principalTable: "bank_statements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reconciliation_matches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    statement_line_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    journal_line_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    origin = table.Column<int>(type: "INTEGER", nullable: false),
                    score = table.Column<int>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    voided_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    voided_by = table.Column<Guid>(type: "TEXT", nullable: true),
                    void_reason = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reconciliation_matches", x => x.id);
                    table.ForeignKey(
                        name: "fk_reconciliation_matches_bank_statement_lines_statement_line_id",
                        column: x => x.statement_line_id,
                        principalTable: "bank_statement_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reconciliation_matches_journal_lines_journal_line_id",
                        column: x => x.journal_line_id,
                        principalTable: "journal_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bank_statement_lines_company_id_bank_account_id_bank_reference",
                table: "bank_statement_lines",
                columns: new[] { "company_id", "bank_account_id", "bank_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bank_statement_lines_company_id_bank_account_id_status",
                table: "bank_statement_lines",
                columns: new[] { "company_id", "bank_account_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_bank_statement_lines_statement_id",
                table: "bank_statement_lines",
                column: "statement_id");

            migrationBuilder.CreateIndex(
                name: "ix_bank_statements_bank_account_id",
                table: "bank_statements",
                column: "bank_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_bank_statements_company_id_bank_account_id_source_sha256",
                table: "bank_statements",
                columns: new[] { "company_id", "bank_account_id", "source_sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_orders_bank_account_id",
                table: "payment_orders",
                column: "bank_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_orders_company_id_number",
                table: "payment_orders",
                columns: new[] { "company_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_orders_company_id_status",
                table: "payment_orders",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_orders_company_id_voucher_id",
                table: "payment_orders",
                columns: new[] { "company_id", "voucher_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_orders_partner_id",
                table: "payment_orders",
                column: "partner_id");

            migrationBuilder.CreateIndex(
                name: "ix_reconciliation_matches_company_id_journal_line_id",
                table: "reconciliation_matches",
                columns: new[] { "company_id", "journal_line_id" },
                unique: true,
                filter: "voided_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_reconciliation_matches_company_id_statement_line_id",
                table: "reconciliation_matches",
                columns: new[] { "company_id", "statement_line_id" },
                unique: true,
                filter: "voided_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_reconciliation_matches_journal_line_id",
                table: "reconciliation_matches",
                column: "journal_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_reconciliation_matches_statement_line_id",
                table: "reconciliation_matches",
                column: "statement_line_id");

            // Imported statements are evidence: header and amounts never change, nothing is deleted.
            migrationBuilder.Sql("CREATE TRIGGER trg_bank_statements_no_update BEFORE UPDATE ON bank_statements BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
            migrationBuilder.Sql("CREATE TRIGGER trg_bank_statements_no_delete BEFORE DELETE ON bank_statements BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
            migrationBuilder.Sql("CREATE TRIGGER trg_bank_statement_lines_no_delete BEFORE DELETE ON bank_statement_lines BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_bank_statement_lines_frozen BEFORE UPDATE ON bank_statement_lines
                WHEN NEW.amount IS NOT OLD.amount OR NEW.transaction_date IS NOT OLD.transaction_date
                  OR NEW.bank_reference IS NOT OLD.bank_reference OR NEW.description IS NOT OLD.description
                  OR NEW.bank_account_id IS NOT OLD.bank_account_id OR NEW.statement_id IS NOT OLD.statement_id
                  OR NEW.company_id IS NOT OLD.company_id
                BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;
                """);
            migrationBuilder.Sql("CREATE TRIGGER trg_reconciliation_matches_no_delete BEFORE DELETE ON reconciliation_matches BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_reconciliation_matches_void_once BEFORE UPDATE ON reconciliation_matches
                WHEN OLD.voided_at IS NOT NULL OR NEW.voided_at IS NULL
                  OR NEW.statement_line_id IS NOT OLD.statement_line_id OR NEW.journal_line_id IS NOT OLD.journal_line_id
                BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var t in new[] { "trg_bank_statements_no_update", "trg_bank_statements_no_delete", "trg_bank_statement_lines_no_delete", "trg_bank_statement_lines_frozen", "trg_reconciliation_matches_no_delete", "trg_reconciliation_matches_void_once" })
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS {t};");
            migrationBuilder.DropTable(
                name: "payment_orders");

            migrationBuilder.DropTable(
                name: "reconciliation_matches");

            migrationBuilder.DropTable(
                name: "bank_statement_lines");

            migrationBuilder.DropTable(
                name: "bank_statements");
        }
    }
}
