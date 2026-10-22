using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <summary>
    /// DB-08: trigger bất biến của SQLite khớp với PostgreSQL và với LedgerImmutabilityInterceptor.
    /// - file_attachments, depreciation_entry_splits: chỉ thêm, không sửa/xóa (PostgreSQL đã có từ Phase9/Phase10, SQLite chỉ có interceptor).
    /// - long_term_assets đã ghi tăng: khóa thêm các cột khấu hao thêm sau (declining_factor, schedule_end_date, total_units, unit_name);
    ///   allocation_frequency vẫn đổi được (AssetService.SetFrequencyAsync).
    /// - depreciation_entries: khóa thêm company_id, period_start, created_at (interceptor chỉ cho đổi voided_at).
    /// ImmutabilityParityTests so phạm vi trigger hai bên từ script migration để lệch kiểu này không quay lại.
    /// </summary>
    public partial class Phase15ImmutabilityParity : Migration
    {
        private const string Abort = "BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;";

        private const string AssetFrozenColumns = """
            NEW.company_id IS NOT OLD.company_id OR NEW.code IS NOT OLD.code OR NEW.class IS NOT OLD.class
              OR NEW.asset_account_id IS NOT OLD.asset_account_id OR NEW.accumulated_account_id IS NOT OLD.accumulated_account_id
              OR NEW.expense_account_id IS NOT OLD.expense_account_id OR NEW.method IS NOT OLD.method OR NEW.proration IS NOT OLD.proration
              OR NEW.original_cost IS NOT OLD.original_cost OR NEW.residual_value IS NOT OLD.residual_value
              OR NEW.useful_life_months IS NOT OLD.useful_life_months OR NEW.in_service_date IS NOT OLD.in_service_date
              OR NEW.opening_accumulated IS NOT OLD.opening_accumulated OR NEW.remaining_life_months IS NOT OLD.remaining_life_months
              OR NEW.schedule_start_date IS NOT OLD.schedule_start_date
            """;

        private const string EntryFrozenColumns = """
            OLD.voided_at IS NOT NULL OR NEW.voided_at IS NULL OR NEW.amount IS NOT OLD.amount OR NEW.asset_id IS NOT OLD.asset_id
              OR NEW.run_id IS NOT OLD.run_id OR NEW.is_disposal IS NOT OLD.is_disposal OR NEW.period_end IS NOT OLD.period_end
            """;

        private const string AssetNewColumns = """

              OR NEW.declining_factor IS NOT OLD.declining_factor OR NEW.schedule_end_date IS NOT OLD.schedule_end_date
              OR NEW.total_units IS NOT OLD.total_units OR NEW.unit_name IS NOT OLD.unit_name
            """;

        private const string EntryNewColumns = """

              OR NEW.company_id IS NOT OLD.company_id OR NEW.period_start IS NOT OLD.period_start OR NEW.created_at IS NOT OLD.created_at
            """;

        private static void AssetFrozen(MigrationBuilder b, string extraColumns)
        {
            b.Sql("DROP TRIGGER IF EXISTS trg_long_term_assets_frozen;");
            b.Sql($"""
                CREATE TRIGGER trg_long_term_assets_frozen BEFORE UPDATE ON long_term_assets
                WHEN OLD.status <> 1 AND ({AssetFrozenColumns}{extraColumns})
                {Abort}
                """);
        }

        private static void EntryVoidOnce(MigrationBuilder b, string extraColumns)
        {
            b.Sql("DROP TRIGGER IF EXISTS trg_depreciation_entries_void_once;");
            b.Sql($"""
                CREATE TRIGGER trg_depreciation_entries_void_once BEFORE UPDATE ON depreciation_entries
                WHEN {EntryFrozenColumns}{extraColumns}
                {Abort}
                """);
        }

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var t in new[] { "file_attachments", "depreciation_entry_splits" })
            {
                migrationBuilder.Sql($"CREATE TRIGGER trg_{t}_no_update BEFORE UPDATE ON {t} {Abort}");
                migrationBuilder.Sql($"CREATE TRIGGER trg_{t}_no_delete BEFORE DELETE ON {t} {Abort}");
            }
            AssetFrozen(migrationBuilder, AssetNewColumns);
            EntryVoidOnce(migrationBuilder, EntryNewColumns);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var t in new[] { "file_attachments", "depreciation_entry_splits" })
            {
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS trg_{t}_no_update;");
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS trg_{t}_no_delete;");
            }
            AssetFrozen(migrationBuilder, "");
            EntryVoidOnce(migrationBuilder, "");
        }
    }
}
