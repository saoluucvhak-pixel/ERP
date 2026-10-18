using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <summary>
    /// Second layer of ledger immutability (docs/02 ADR-07): rejects writes even when they bypass EF.
    /// SQLite syntax; the PostgreSQL provider will need a plpgsql equivalent in its own migration set.
    /// Voucher status codes: 1 Draft, 4 Posted, 5 Reversed.
    /// </summary>
    public partial class LedgerImmutabilityTriggers : Migration
    {
        private static readonly string[] AppendOnlyTables = ["journal_entries", "journal_lines", "audit_logs", "voucher_history"];

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var t in AppendOnlyTables)
            {
                migrationBuilder.Sql($"CREATE TRIGGER trg_{t}_no_update BEFORE UPDATE ON {t} BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
                migrationBuilder.Sql($"CREATE TRIGGER trg_{t}_no_delete BEFORE DELETE ON {t} BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
            }

            migrationBuilder.Sql("CREATE TRIGGER trg_vouchers_no_delete BEFORE DELETE ON vouchers BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");

            // Posted vouchers may only move Posted -> Reversed (row_version bump allowed); content columns are frozen.
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_vouchers_posted_frozen BEFORE UPDATE ON vouchers
                WHEN OLD.status IN (4, 5) AND NOT (
                    OLD.status = 4 AND NEW.status IN (4, 5)
                    AND NEW.company_id IS OLD.company_id AND NEW.number IS OLD.number
                    AND NEW.voucher_type IS OLD.voucher_type AND NEW.posting_date IS OLD.posting_date
                    AND NEW.voucher_date IS OLD.voucher_date AND NEW.description IS OLD.description
                    AND NEW.currency_code IS OLD.currency_code AND NEW.fiscal_year_id IS OLD.fiscal_year_id
                    AND NEW.posted_by IS OLD.posted_by AND NEW.posted_at IS OLD.posted_at)
                BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;
                """);

            foreach (var op in new[] { "INSERT", "UPDATE", "DELETE" })
            {
                var row = op == "INSERT" ? "NEW" : "OLD";
                migrationBuilder.Sql($"""
                    CREATE TRIGGER trg_voucher_lines_draft_only_{op.ToLowerInvariant()} BEFORE {op} ON voucher_lines
                    WHEN (SELECT status FROM vouchers WHERE id = {row}.voucher_id) <> 1
                    BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;
                    """);
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var t in AppendOnlyTables)
            {
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS trg_{t}_no_update;");
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS trg_{t}_no_delete;");
            }
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_vouchers_no_delete;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_vouchers_posted_frozen;");
            foreach (var op in new[] { "insert", "update", "delete" })
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS trg_voucher_lines_draft_only_{op};");
        }
    }
}
