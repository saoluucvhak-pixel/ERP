using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase20EInvoiceTriggers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Tầng thứ hai của bất biến hóa đơn điện tử (tầng một: LedgerImmutabilityInterceptor): chặn cả ghi bỏ qua EF.
            foreach (var sql in Triggers) migrationBuilder.Sql(sql);
        }

        private static readonly string[] Triggers =
        [
            "CREATE TRIGGER trg_einvoice_messages_no_update BEFORE UPDATE ON e_invoice_messages BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;",
            "CREATE TRIGGER trg_einvoice_messages_no_delete BEFORE DELETE ON e_invoice_messages BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;",
            "CREATE TRIGGER trg_einvoice_outbox_no_delete BEFORE DELETE ON e_invoice_outbox BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;",
            "CREATE TRIGGER trg_einvoice_documents_no_delete BEFORE DELETE ON e_invoice_documents BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;",
            // Đã phát hành (trạng thái khác Đang chờ=1 / Bị từ chối=3): số, ký hiệu, ngày ký, mã cơ quan thuế, XML, băm, hóa đơn ERP và ngày không đổi; chỉ trạng thái và ghi chú lỗi đổi được.
            "CREATE TRIGGER trg_einvoice_documents_issued_frozen BEFORE UPDATE ON e_invoice_documents WHEN OLD.status NOT IN (1, 3) AND (OLD.series IS NOT NEW.series OR OLD.number IS NOT NEW.number OR OLD.signed_at IS NOT NEW.signed_at OR OLD.tax_authority_code IS NOT NEW.tax_authority_code OR OLD.xml_content IS NOT NEW.xml_content OR OLD.xml_sha256 IS NOT NEW.xml_sha256 OR OLD.invoice_id IS NOT NEW.invoice_id OR OLD.issue_date IS NOT NEW.issue_date) BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;",
        ];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var name in new[] { "trg_einvoice_messages_no_update", "trg_einvoice_messages_no_delete", "trg_einvoice_outbox_no_delete", "trg_einvoice_documents_no_delete", "trg_einvoice_documents_issued_frozen" })
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS {name};");
        }
    }
}
