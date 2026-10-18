using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase2SubledgerAndRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_vouchers_company_id_source_system_source_id",
                table: "vouchers");

            migrationBuilder.AddColumn<string>(
                name: "business_event",
                table: "vouchers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "main_account_id",
                table: "vouchers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "partner_id",
                table: "vouchers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "posting_rule_id",
                table: "vouchers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "posting_rule_version",
                table: "vouchers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "due_date",
                table: "voucher_lines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "settle_journal_line_id",
                table: "voucher_lines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "due_date",
                table: "journal_lines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "cash_kind",
                table: "accounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "negative_balance_policy",
                table: "accounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "open_item_nature",
                table: "accounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "account_role_mappings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    role_code = table.Column<string>(type: "TEXT", nullable: false),
                    account_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    row_version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_role_mappings", x => x.id);
                    table.ForeignKey(
                        name: "fk_account_role_mappings_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "closing_rule_sets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    code = table.Column<string>(type: "TEXT", nullable: false),
                    version = table.Column<int>(type: "INTEGER", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    description = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    activated_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    activated_by = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_closing_rule_sets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "posting_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    code = table.Column<string>(type: "TEXT", nullable: false),
                    version = table.Column<int>(type: "INTEGER", nullable: false),
                    document_type = table.Column<string>(type: "TEXT", nullable: false),
                    business_event = table.Column<string>(type: "TEXT", nullable: false),
                    priority = table.Column<int>(type: "INTEGER", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    definition_json = table.Column<string>(type: "TEXT", nullable: false),
                    test_cases_json = table.Column<string>(type: "TEXT", nullable: false),
                    last_test_run_json = table.Column<string>(type: "TEXT", nullable: true),
                    description = table.Column<string>(type: "TEXT", nullable: true),
                    regime_version_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    activated_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    activated_by = table.Column<Guid>(type: "TEXT", nullable: true),
                    row_version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_posting_rules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "settlement_allocations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    account_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    partner_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    debit_line_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    credit_line_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    amount = table.Column<long>(type: "INTEGER", nullable: false),
                    allocation_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    source_voucher_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    origin = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    voided_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    voided_by = table.Column<Guid>(type: "TEXT", nullable: true),
                    void_reason = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settlement_allocations", x => x.id);
                    table.CheckConstraint("ck_settlement_allocations_amount", "amount > 0");
                    table.ForeignKey(
                        name: "fk_settlement_allocations_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_settlement_allocations_business_partners_partner_id",
                        column: x => x.partner_id,
                        principalTable: "business_partners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_settlement_allocations_journal_lines_credit_line_id",
                        column: x => x.credit_line_id,
                        principalTable: "journal_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_settlement_allocations_journal_lines_debit_line_id",
                        column: x => x.debit_line_id,
                        principalTable: "journal_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "closing_steps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    rule_set_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: false),
                    source_prefixes = table.Column<string>(type: "TEXT", nullable: false),
                    target_account_id = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_closing_steps", x => x.id);
                    table.ForeignKey(
                        name: "fk_closing_steps_accounts_target_account_id",
                        column: x => x.target_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_closing_steps_closing_rule_sets_rule_set_id",
                        column: x => x.rule_set_id,
                        principalTable: "closing_rule_sets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_vouchers_company_id_source_system_source_id",
                table: "vouchers",
                columns: new[] { "company_id", "source_system", "source_id" },
                unique: true,
                filter: "source_id IS NOT NULL AND status <> 9");

            migrationBuilder.CreateIndex(
                name: "ix_account_role_mappings_account_id",
                table: "account_role_mappings",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_account_role_mappings_company_id_role_code",
                table: "account_role_mappings",
                columns: new[] { "company_id", "role_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_closing_rule_sets_company_id_code_version",
                table: "closing_rule_sets",
                columns: new[] { "company_id", "code", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_closing_steps_rule_set_id",
                table: "closing_steps",
                column: "rule_set_id");

            migrationBuilder.CreateIndex(
                name: "ix_closing_steps_target_account_id",
                table: "closing_steps",
                column: "target_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_posting_rules_company_id_code_version",
                table: "posting_rules",
                columns: new[] { "company_id", "code", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_posting_rules_company_id_document_type_business_event_status",
                table: "posting_rules",
                columns: new[] { "company_id", "document_type", "business_event", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_settlement_allocations_account_id",
                table: "settlement_allocations",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_settlement_allocations_company_id_account_id_partner_id",
                table: "settlement_allocations",
                columns: new[] { "company_id", "account_id", "partner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_settlement_allocations_company_id_credit_line_id",
                table: "settlement_allocations",
                columns: new[] { "company_id", "credit_line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_settlement_allocations_company_id_debit_line_id",
                table: "settlement_allocations",
                columns: new[] { "company_id", "debit_line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_settlement_allocations_credit_line_id",
                table: "settlement_allocations",
                column: "credit_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_settlement_allocations_debit_line_id",
                table: "settlement_allocations",
                column: "debit_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_settlement_allocations_partner_id",
                table: "settlement_allocations",
                column: "partner_id");

            // Allocations: never deleted; voided at most once and nothing else may change.
            migrationBuilder.Sql("CREATE TRIGGER trg_settlement_allocations_no_delete BEFORE DELETE ON settlement_allocations BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_settlement_allocations_void_once BEFORE UPDATE ON settlement_allocations
                WHEN OLD.voided_at IS NOT NULL OR NEW.voided_at IS NULL
                  OR NEW.amount IS NOT OLD.amount OR NEW.debit_line_id IS NOT OLD.debit_line_id OR NEW.credit_line_id IS NOT OLD.credit_line_id
                  OR NEW.allocation_date IS NOT OLD.allocation_date OR NEW.account_id IS NOT OLD.account_id OR NEW.partner_id IS NOT OLD.partner_id
                  OR NEW.company_id IS NOT OLD.company_id
                BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;
                """);

            // Posted vouchers: extend the frozen column list with the phase-2 columns.
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_vouchers_posted_frozen;");
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_vouchers_posted_frozen BEFORE UPDATE ON vouchers
                WHEN OLD.status IN (4, 5) AND NOT (
                    OLD.status = 4 AND NEW.status IN (4, 5)
                    AND NEW.company_id IS OLD.company_id AND NEW.number IS OLD.number
                    AND NEW.voucher_type IS OLD.voucher_type AND NEW.posting_date IS OLD.posting_date
                    AND NEW.voucher_date IS OLD.voucher_date AND NEW.description IS OLD.description
                    AND NEW.currency_code IS OLD.currency_code AND NEW.fiscal_year_id IS OLD.fiscal_year_id
                    AND NEW.posted_by IS OLD.posted_by AND NEW.posted_at IS OLD.posted_at
                    AND NEW.business_event IS OLD.business_event AND NEW.posting_rule_id IS OLD.posting_rule_id
                    AND NEW.posting_rule_version IS OLD.posting_rule_version AND NEW.main_account_id IS OLD.main_account_id
                    AND NEW.partner_id IS OLD.partner_id)
                BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_settlement_allocations_no_delete;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_settlement_allocations_void_once;");
            migrationBuilder.DropTable(
                name: "account_role_mappings");

            migrationBuilder.DropTable(
                name: "closing_steps");

            migrationBuilder.DropTable(
                name: "posting_rules");

            migrationBuilder.DropTable(
                name: "settlement_allocations");

            migrationBuilder.DropTable(
                name: "closing_rule_sets");

            migrationBuilder.DropIndex(
                name: "ix_vouchers_company_id_source_system_source_id",
                table: "vouchers");

            migrationBuilder.DropColumn(
                name: "business_event",
                table: "vouchers");

            migrationBuilder.DropColumn(
                name: "main_account_id",
                table: "vouchers");

            migrationBuilder.DropColumn(
                name: "partner_id",
                table: "vouchers");

            migrationBuilder.DropColumn(
                name: "posting_rule_id",
                table: "vouchers");

            migrationBuilder.DropColumn(
                name: "posting_rule_version",
                table: "vouchers");

            migrationBuilder.DropColumn(
                name: "due_date",
                table: "voucher_lines");

            migrationBuilder.DropColumn(
                name: "settle_journal_line_id",
                table: "voucher_lines");

            migrationBuilder.DropColumn(
                name: "due_date",
                table: "journal_lines");

            migrationBuilder.DropColumn(
                name: "cash_kind",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "negative_balance_policy",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "open_item_nature",
                table: "accounts");

            migrationBuilder.CreateIndex(
                name: "ix_vouchers_company_id_source_system_source_id",
                table: "vouchers",
                columns: new[] { "company_id", "source_system", "source_id" },
                unique: true,
                filter: "source_id IS NOT NULL");
        }
    }
}
