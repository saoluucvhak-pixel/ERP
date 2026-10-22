namespace Hak.Data.Postgres;

/// <summary>
/// PostgreSQL equivalent of the SQLite immutability triggers (docs/02 ADR-07): the database itself refuses to change
/// the ledger and other evidence even when someone bypasses the application. Same error text: LEDGER_IMMUTABLE.
/// </summary>
internal static class ImmutabilityTriggers
{
    private static readonly string[] AppendOnly =
    [
        "journal_entries", "journal_lines", "audit_logs", "voucher_history", "bank_statements", "stock_movements",
        "e_invoice_records", "e_invoice_rate_lines", "e_invoice_import_batches", "e_invoice_status_changes", "stock_value_adjustments",
    ];

    private static readonly string[] NoDelete =
    [
        "vouchers", "settlement_allocations", "bank_statement_lines", "reconciliation_matches", "stock_valuations", "costing_runs", "stock_measurements",
    ];

    public const string Up = """
        CREATE OR REPLACE FUNCTION hak_ledger_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION 'LEDGER_IMMUTABLE'; END $$;

        -- Posted vouchers: only Posted -> Reversed (row_version bump) is allowed; content is frozen.
        CREATE OR REPLACE FUNCTION hak_vouchers_posted_frozen() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF OLD.status IN (4, 5) AND NOT (
               OLD.status = 4 AND NEW.status IN (4, 5)
               AND NEW.company_id IS NOT DISTINCT FROM OLD.company_id AND NEW.number IS NOT DISTINCT FROM OLD.number
               AND NEW.voucher_type IS NOT DISTINCT FROM OLD.voucher_type AND NEW.posting_date IS NOT DISTINCT FROM OLD.posting_date
               AND NEW.voucher_date IS NOT DISTINCT FROM OLD.voucher_date AND NEW.description IS NOT DISTINCT FROM OLD.description
               AND NEW.currency_code IS NOT DISTINCT FROM OLD.currency_code AND NEW.fiscal_year_id IS NOT DISTINCT FROM OLD.fiscal_year_id
               AND NEW.posted_by IS NOT DISTINCT FROM OLD.posted_by AND NEW.posted_at IS NOT DISTINCT FROM OLD.posted_at
               AND NEW.business_event IS NOT DISTINCT FROM OLD.business_event AND NEW.posting_rule_id IS NOT DISTINCT FROM OLD.posting_rule_id
               AND NEW.posting_rule_version IS NOT DISTINCT FROM OLD.posting_rule_version AND NEW.main_account_id IS NOT DISTINCT FROM OLD.main_account_id
               AND NEW.partner_id IS NOT DISTINCT FROM OLD.partner_id) THEN
            RAISE EXCEPTION 'LEDGER_IMMUTABLE';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER trg_vouchers_posted_frozen BEFORE UPDATE ON vouchers FOR EACH ROW EXECUTE FUNCTION hak_vouchers_posted_frozen();

        -- Voucher lines may change only while their voucher is Draft (status 1).
        CREATE OR REPLACE FUNCTION hak_voucher_lines_draft_only() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE v_status integer;
        BEGIN
          SELECT status INTO v_status FROM vouchers WHERE id = CASE WHEN TG_OP = 'DELETE' THEN OLD.voucher_id ELSE NEW.voucher_id END;
          IF v_status IS DISTINCT FROM 1 THEN RAISE EXCEPTION 'LEDGER_IMMUTABLE'; END IF;
          IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER trg_voucher_lines_draft_only BEFORE INSERT OR UPDATE OR DELETE ON voucher_lines FOR EACH ROW EXECUTE FUNCTION hak_voucher_lines_draft_only();

        CREATE OR REPLACE FUNCTION hak_settlement_void_once() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF OLD.voided_at IS NOT NULL OR NEW.voided_at IS NULL
             OR NEW.amount IS DISTINCT FROM OLD.amount OR NEW.debit_line_id IS DISTINCT FROM OLD.debit_line_id
             OR NEW.credit_line_id IS DISTINCT FROM OLD.credit_line_id OR NEW.allocation_date IS DISTINCT FROM OLD.allocation_date
             OR NEW.account_id IS DISTINCT FROM OLD.account_id OR NEW.partner_id IS DISTINCT FROM OLD.partner_id
             OR NEW.company_id IS DISTINCT FROM OLD.company_id THEN
            RAISE EXCEPTION 'LEDGER_IMMUTABLE';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER trg_settlement_allocations_void_once BEFORE UPDATE ON settlement_allocations FOR EACH ROW EXECUTE FUNCTION hak_settlement_void_once();

        CREATE OR REPLACE FUNCTION hak_statement_lines_frozen() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF NEW.amount IS DISTINCT FROM OLD.amount OR NEW.transaction_date IS DISTINCT FROM OLD.transaction_date
             OR NEW.bank_reference IS DISTINCT FROM OLD.bank_reference OR NEW.description IS DISTINCT FROM OLD.description
             OR NEW.bank_account_id IS DISTINCT FROM OLD.bank_account_id OR NEW.statement_id IS DISTINCT FROM OLD.statement_id
             OR NEW.company_id IS DISTINCT FROM OLD.company_id THEN
            RAISE EXCEPTION 'LEDGER_IMMUTABLE';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER trg_bank_statement_lines_frozen BEFORE UPDATE ON bank_statement_lines FOR EACH ROW EXECUTE FUNCTION hak_statement_lines_frozen();

        CREATE OR REPLACE FUNCTION hak_match_void_once() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF OLD.voided_at IS NOT NULL OR NEW.voided_at IS NULL
             OR NEW.statement_line_id IS DISTINCT FROM OLD.statement_line_id OR NEW.journal_line_id IS DISTINCT FROM OLD.journal_line_id THEN
            RAISE EXCEPTION 'LEDGER_IMMUTABLE';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER trg_reconciliation_matches_void_once BEFORE UPDATE ON reconciliation_matches FOR EACH ROW EXECUTE FUNCTION hak_match_void_once();

        CREATE OR REPLACE FUNCTION hak_valuation_void_once() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF OLD.voided_at IS NOT NULL OR NEW.voided_at IS NULL OR NEW.value IS DISTINCT FROM OLD.value
             OR NEW.movement_id IS DISTINCT FROM OLD.movement_id OR NEW.costing_run_id IS DISTINCT FROM OLD.costing_run_id THEN
            RAISE EXCEPTION 'LEDGER_IMMUTABLE';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER trg_stock_valuations_void_once BEFORE UPDATE ON stock_valuations FOR EACH ROW EXECUTE FUNCTION hak_valuation_void_once();

        CREATE OR REPLACE FUNCTION hak_measurement_supersede_once() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF OLD.superseded_at IS NOT NULL OR NEW.superseded_at IS NULL
             OR NEW.measured_mt IS DISTINCT FROM OLD.measured_mt OR NEW.dryness_percent IS DISTINCT FROM OLD.dryness_percent OR NEW.bdmt IS DISTINCT FROM OLD.bdmt
             OR NEW.evidence_no IS DISTINCT FROM OLD.evidence_no OR NEW.document_id IS DISTINCT FROM OLD.document_id OR NEW.line_no IS DISTINCT FROM OLD.line_no THEN
            RAISE EXCEPTION 'LEDGER_IMMUTABLE';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER trg_stock_measurements_supersede_once BEFORE UPDATE ON stock_measurements FOR EACH ROW EXECUTE FUNCTION hak_measurement_supersede_once();
        """;

    public static IEnumerable<string> Statements()
    {
        yield return Up;
        foreach (var t in AppendOnly)
            yield return $"CREATE TRIGGER trg_{t}_append_only BEFORE UPDATE OR DELETE ON {t} FOR EACH ROW EXECUTE FUNCTION hak_ledger_immutable();";
        foreach (var t in NoDelete)
            yield return $"CREATE TRIGGER trg_{t}_no_delete BEFORE DELETE ON {t} FOR EACH ROW EXECUTE FUNCTION hak_ledger_immutable();";
    }

    /// <summary>Phase 4 (fixed assets / tools). Kept separate so the initial migration never refers to tables created later.</summary>
    public static IEnumerable<string> AssetStatements()
    {
        yield return """
            CREATE OR REPLACE FUNCTION hak_long_term_assets_frozen() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              IF OLD.status <> 1 AND (NEW.company_id IS DISTINCT FROM OLD.company_id OR NEW.code IS DISTINCT FROM OLD.code
                   OR NEW.class IS DISTINCT FROM OLD.class OR NEW.asset_account_id IS DISTINCT FROM OLD.asset_account_id
                   OR NEW.accumulated_account_id IS DISTINCT FROM OLD.accumulated_account_id OR NEW.expense_account_id IS DISTINCT FROM OLD.expense_account_id
                   OR NEW.method IS DISTINCT FROM OLD.method OR NEW.proration IS DISTINCT FROM OLD.proration
                   OR NEW.original_cost IS DISTINCT FROM OLD.original_cost OR NEW.residual_value IS DISTINCT FROM OLD.residual_value
                   OR NEW.useful_life_months IS DISTINCT FROM OLD.useful_life_months OR NEW.in_service_date IS DISTINCT FROM OLD.in_service_date
                   OR NEW.opening_accumulated IS DISTINCT FROM OLD.opening_accumulated OR NEW.remaining_life_months IS DISTINCT FROM OLD.remaining_life_months
                   OR NEW.schedule_start_date IS DISTINCT FROM OLD.schedule_start_date) THEN
                RAISE EXCEPTION 'LEDGER_IMMUTABLE';
              END IF;
              RETURN NEW;
            END $$;
            CREATE TRIGGER trg_long_term_assets_frozen BEFORE UPDATE ON long_term_assets FOR EACH ROW EXECUTE FUNCTION hak_long_term_assets_frozen();

            CREATE OR REPLACE FUNCTION hak_depreciation_entries_void_once() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              IF OLD.voided_at IS NOT NULL OR NEW.voided_at IS NULL OR NEW.amount IS DISTINCT FROM OLD.amount OR NEW.asset_id IS DISTINCT FROM OLD.asset_id
                 OR NEW.run_id IS DISTINCT FROM OLD.run_id OR NEW.is_disposal IS DISTINCT FROM OLD.is_disposal OR NEW.period_end IS DISTINCT FROM OLD.period_end THEN
                RAISE EXCEPTION 'LEDGER_IMMUTABLE';
              END IF;
              RETURN NEW;
            END $$;
            CREATE TRIGGER trg_depreciation_entries_void_once BEFORE UPDATE ON depreciation_entries FOR EACH ROW EXECUTE FUNCTION hak_depreciation_entries_void_once();

            CREATE TRIGGER trg_asset_cost_sources_append_only BEFORE UPDATE OR DELETE ON asset_cost_sources FOR EACH ROW EXECUTE FUNCTION hak_ledger_immutable();
            """;
        foreach (var t in AssetNoDelete)
            yield return $"CREATE TRIGGER trg_{t}_no_delete BEFORE DELETE ON {t} FOR EACH ROW EXECUTE FUNCTION hak_ledger_immutable();";
    }

    private static readonly string[] AssetNoDelete = ["long_term_assets", "depreciation_runs", "depreciation_entries"];

    private const string AssetFrozenColumns = """
        NEW.company_id IS DISTINCT FROM OLD.company_id OR NEW.code IS DISTINCT FROM OLD.code
               OR NEW.class IS DISTINCT FROM OLD.class OR NEW.asset_account_id IS DISTINCT FROM OLD.asset_account_id
               OR NEW.accumulated_account_id IS DISTINCT FROM OLD.accumulated_account_id OR NEW.expense_account_id IS DISTINCT FROM OLD.expense_account_id
               OR NEW.method IS DISTINCT FROM OLD.method OR NEW.proration IS DISTINCT FROM OLD.proration
               OR NEW.original_cost IS DISTINCT FROM OLD.original_cost OR NEW.residual_value IS DISTINCT FROM OLD.residual_value
               OR NEW.useful_life_months IS DISTINCT FROM OLD.useful_life_months OR NEW.in_service_date IS DISTINCT FROM OLD.in_service_date
               OR NEW.opening_accumulated IS DISTINCT FROM OLD.opening_accumulated OR NEW.remaining_life_months IS DISTINCT FROM OLD.remaining_life_months
               OR NEW.schedule_start_date IS DISTINCT FROM OLD.schedule_start_date
        """;

    private const string EntryFrozenColumns = """
        OLD.voided_at IS NOT NULL OR NEW.voided_at IS NULL OR NEW.amount IS DISTINCT FROM OLD.amount OR NEW.asset_id IS DISTINCT FROM OLD.asset_id
             OR NEW.run_id IS DISTINCT FROM OLD.run_id OR NEW.is_disposal IS DISTINCT FROM OLD.is_disposal OR NEW.period_end IS DISTINCT FROM OLD.period_end
        """;

    private static string AssetFrozenFunction(string extra) => $"""
        CREATE OR REPLACE FUNCTION hak_long_term_assets_frozen() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF OLD.status <> 1 AND ({AssetFrozenColumns}{extra}) THEN
            RAISE EXCEPTION 'LEDGER_IMMUTABLE';
          END IF;
          RETURN NEW;
        END $$;
        """;

    private static string EntryVoidOnceFunction(string extra) => $"""
        CREATE OR REPLACE FUNCTION hak_depreciation_entries_void_once() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF {EntryFrozenColumns}{extra} THEN
            RAISE EXCEPTION 'LEDGER_IMMUTABLE';
          END IF;
          RETURN NEW;
        END $$;
        """;

    /// <summary>
    /// Phase 15 (DB-08): thẻ tài sản đã ghi tăng khóa thêm các cột khấu hao thêm sau (declining_factor, schedule_end_date, total_units,
    /// unit_name; allocation_frequency vẫn đổi được), số khấu hao khóa thêm company_id / period_start / created_at — khớp interceptor và SQLite.
    /// Chỉ thay thân hàm; trigger giữ nguyên tên. Tách riêng vì Phase4Assets chạy trước khi các cột này tồn tại.
    /// </summary>
    public static IEnumerable<string> ParityStatements()
    {
        yield return AssetFrozenFunction("""

                   OR NEW.declining_factor IS DISTINCT FROM OLD.declining_factor OR NEW.schedule_end_date IS DISTINCT FROM OLD.schedule_end_date
                   OR NEW.total_units IS DISTINCT FROM OLD.total_units OR NEW.unit_name IS DISTINCT FROM OLD.unit_name
            """);
        yield return EntryVoidOnceFunction("""

                 OR NEW.company_id IS DISTINCT FROM OLD.company_id OR NEW.period_start IS DISTINCT FROM OLD.period_start OR NEW.created_at IS DISTINCT FROM OLD.created_at
            """);
    }

    public static IEnumerable<string> ParityDownStatements()
    {
        yield return AssetFrozenFunction("");
        yield return EntryVoidOnceFunction("");
    }

    public static IEnumerable<string> AssetDownStatements()
    {
        foreach (var t in AssetNoDelete) yield return $"DROP TRIGGER IF EXISTS trg_{t}_no_delete ON {t};";
        yield return "DROP TRIGGER IF EXISTS trg_long_term_assets_frozen ON long_term_assets;";
        yield return "DROP TRIGGER IF EXISTS trg_depreciation_entries_void_once ON depreciation_entries;";
        yield return "DROP TRIGGER IF EXISTS trg_asset_cost_sources_append_only ON asset_cost_sources;";
        yield return "DROP FUNCTION IF EXISTS hak_long_term_assets_frozen();";
        yield return "DROP FUNCTION IF EXISTS hak_depreciation_entries_void_once();";
    }

    /// <summary>Phase 4 (loans). Separate for the same reason as <see cref="AssetStatements"/>.</summary>
    public static IEnumerable<string> LoanStatements()
    {
        yield return """
            CREATE OR REPLACE FUNCTION hak_loan_contracts_frozen() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              IF OLD.status <> 1 AND (NEW.company_id IS DISTINCT FROM OLD.company_id OR NEW.code IS DISTINCT FROM OLD.code
                   OR NEW.lender_id IS DISTINCT FROM OLD.lender_id OR NEW.principal_account_id IS DISTINCT FROM OLD.principal_account_id
                   OR NEW.interest_expense_account_id IS DISTINCT FROM OLD.interest_expense_account_id
                   OR NEW.accrued_interest_account_id IS DISTINCT FROM OLD.accrued_interest_account_id OR NEW.currency IS DISTINCT FROM OLD.currency
                   OR NEW.contract_amount IS DISTINCT FROM OLD.contract_amount OR NEW.start_date IS DISTINCT FROM OLD.start_date
                   OR NEW.maturity_date IS DISTINCT FROM OLD.maturity_date OR NEW.interest_from IS DISTINCT FROM OLD.interest_from
                   OR NEW.day_count IS DISTINCT FROM OLD.day_count OR NEW.description IS DISTINCT FROM OLD.description) THEN
                RAISE EXCEPTION 'LEDGER_IMMUTABLE';
              END IF;
              RETURN NEW;
            END $$;
            CREATE TRIGGER trg_loan_contracts_frozen BEFORE UPDATE ON loan_contracts FOR EACH ROW EXECUTE FUNCTION hak_loan_contracts_frozen();

            CREATE OR REPLACE FUNCTION hak_loan_interest_entries_void_once() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              IF OLD.voided_at IS NOT NULL OR NEW.voided_at IS NULL OR NEW.amount IS DISTINCT FROM OLD.amount
                 OR NEW.foreign_amount IS DISTINCT FROM OLD.foreign_amount OR NEW.loan_id IS DISTINCT FROM OLD.loan_id
                 OR NEW.run_id IS DISTINCT FROM OLD.run_id OR NEW.period_end IS DISTINCT FROM OLD.period_end THEN
                RAISE EXCEPTION 'LEDGER_IMMUTABLE';
              END IF;
              RETURN NEW;
            END $$;
            CREATE TRIGGER trg_loan_interest_entries_void_once BEFORE UPDATE ON loan_interest_entries FOR EACH ROW EXECUTE FUNCTION hak_loan_interest_entries_void_once();

            CREATE TRIGGER trg_loan_rates_append_only BEFORE UPDATE OR DELETE ON loan_rates FOR EACH ROW EXECUTE FUNCTION hak_ledger_immutable();
            CREATE TRIGGER trg_loan_links_append_only BEFORE UPDATE OR DELETE ON loan_links FOR EACH ROW EXECUTE FUNCTION hak_ledger_immutable();
            """;
        foreach (var t in LoanNoDelete)
            yield return $"CREATE TRIGGER trg_{t}_no_delete BEFORE DELETE ON {t} FOR EACH ROW EXECUTE FUNCTION hak_ledger_immutable();";
    }

    private static readonly string[] LoanNoDelete = ["loan_contracts", "loan_interest_runs", "loan_interest_entries"];

    public static IEnumerable<string> LoanDownStatements()
    {
        foreach (var t in LoanNoDelete) yield return $"DROP TRIGGER IF EXISTS trg_{t}_no_delete ON {t};";
        yield return "DROP TRIGGER IF EXISTS trg_loan_contracts_frozen ON loan_contracts;";
        yield return "DROP TRIGGER IF EXISTS trg_loan_interest_entries_void_once ON loan_interest_entries;";
        yield return "DROP TRIGGER IF EXISTS trg_loan_rates_append_only ON loan_rates;";
        yield return "DROP TRIGGER IF EXISTS trg_loan_links_append_only ON loan_links;";
        yield return "DROP FUNCTION IF EXISTS hak_loan_contracts_frozen();";
        yield return "DROP FUNCTION IF EXISTS hak_loan_interest_entries_void_once();";
    }

    /// <summary>
    /// Phase 6 (report engine): snapshots are append-only; a Verified template (status 2) and its lines/checks are frozen —
    /// the only change allowed on the definition row is none (new versions are new rows).
    /// </summary>
    public static IEnumerable<string> ReportStatements()
    {
        yield return """
            CREATE OR REPLACE FUNCTION hak_report_definition_frozen() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              IF OLD.status = 2 THEN RAISE EXCEPTION 'LEDGER_IMMUTABLE'; END IF;
              IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
              RETURN NEW;
            END $$;
            CREATE TRIGGER trg_report_definitions_frozen BEFORE UPDATE OR DELETE ON report_definitions FOR EACH ROW EXECUTE FUNCTION hak_report_definition_frozen();

            CREATE OR REPLACE FUNCTION hak_report_parts_frozen() RETURNS trigger LANGUAGE plpgsql AS $$
            DECLARE v_status integer;
            BEGIN
              SELECT status INTO v_status FROM report_definitions WHERE id = CASE WHEN TG_OP = 'DELETE' THEN OLD.definition_id ELSE NEW.definition_id END;
              IF v_status = 2 THEN RAISE EXCEPTION 'LEDGER_IMMUTABLE'; END IF;
              IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
              RETURN NEW;
            END $$;
            CREATE TRIGGER trg_report_lines_frozen BEFORE INSERT OR UPDATE OR DELETE ON report_lines FOR EACH ROW EXECUTE FUNCTION hak_report_parts_frozen();
            CREATE TRIGGER trg_report_checks_frozen BEFORE INSERT OR UPDATE OR DELETE ON report_checks FOR EACH ROW EXECUTE FUNCTION hak_report_parts_frozen();
            CREATE TRIGGER trg_report_snapshots_append_only BEFORE UPDATE OR DELETE ON report_snapshots FOR EACH ROW EXECUTE FUNCTION hak_ledger_immutable();
            """;
    }

    public static IEnumerable<string> ReportDownStatements()
    {
        yield return "DROP TRIGGER IF EXISTS trg_report_definitions_frozen ON report_definitions;";
        yield return "DROP TRIGGER IF EXISTS trg_report_lines_frozen ON report_lines;";
        yield return "DROP TRIGGER IF EXISTS trg_report_checks_frozen ON report_checks;";
        yield return "DROP TRIGGER IF EXISTS trg_report_snapshots_append_only ON report_snapshots;";
        yield return "DROP FUNCTION IF EXISTS hak_report_definition_frozen();";
        yield return "DROP FUNCTION IF EXISTS hak_report_parts_frozen();";
    }

    public static IEnumerable<string> DownStatements()
    {
        foreach (var t in AppendOnly) yield return $"DROP TRIGGER IF EXISTS trg_{t}_append_only ON {t};";
        foreach (var t in NoDelete) yield return $"DROP TRIGGER IF EXISTS trg_{t}_no_delete ON {t};";
        foreach (var (trg, tbl) in new[] { ("trg_vouchers_posted_frozen", "vouchers"), ("trg_voucher_lines_draft_only", "voucher_lines"),
                     ("trg_settlement_allocations_void_once", "settlement_allocations"), ("trg_bank_statement_lines_frozen", "bank_statement_lines"),
                     ("trg_reconciliation_matches_void_once", "reconciliation_matches"), ("trg_stock_valuations_void_once", "stock_valuations"),
                     ("trg_stock_measurements_supersede_once", "stock_measurements") })
            yield return $"DROP TRIGGER IF EXISTS {trg} ON {tbl};";
    }
}
