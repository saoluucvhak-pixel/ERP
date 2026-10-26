using Hak.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <summary>Chỉ thêm trigger bất biến (không đổi mô hình): bằng chứng trích trước chiết khấu và thông báo hóa đơn điện tử, khóa trạng thái / khóa chống trùng của hóa đơn điện tử.</summary>
    [DbContext(typeof(HakDbContext))]
    [Migration("20261026120000_Phase29EvidenceTriggers")]
    public partial class Phase29EvidenceTriggers : Migration
    {
        private static readonly string[] AppendOnly = ["discount_accruals", "e_invoice_error_notice_lines", "e_invoice_risk_notice_lines"];
        private static readonly string[] NoDelete = ["e_invoice_error_notices", "e_invoice_risk_notices"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var t in AppendOnly)
            {
                migrationBuilder.Sql($"CREATE TRIGGER trg_{t}_no_update BEFORE UPDATE ON {t} BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
                migrationBuilder.Sql($"CREATE TRIGGER trg_{t}_no_delete BEFORE DELETE ON {t} BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
            }
            foreach (var t in NoDelete)
                migrationBuilder.Sql($"CREATE TRIGGER trg_{t}_no_delete BEFORE DELETE ON {t} BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
            // Hóa đơn điện tử đã phát hành / thay thế không được mở lại thành Đang chờ / Bị từ chối (sau đó sửa số, XML).
            migrationBuilder.Sql("CREATE TRIGGER trg_einvoice_documents_no_reopen BEFORE UPDATE ON e_invoice_documents WHEN OLD.status NOT IN (1, 3) AND NEW.status IN (1, 3) BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
            // Lệnh gửi: khóa chống trùng, hóa đơn điện tử và công ty không đổi.
            migrationBuilder.Sql("CREATE TRIGGER trg_einvoice_outbox_key_frozen BEFORE UPDATE ON e_invoice_outbox WHEN OLD.idempotency_key IS NOT NEW.idempotency_key OR OLD.document_id IS NOT NEW.document_id OR OLD.company_id IS NOT NEW.company_id BEGIN SELECT RAISE(ABORT, 'LEDGER_IMMUTABLE'); END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var t in AppendOnly)
            {
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS trg_{t}_no_update;");
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS trg_{t}_no_delete;");
            }
            foreach (var t in NoDelete) migrationBuilder.Sql($"DROP TRIGGER IF EXISTS trg_{t}_no_delete;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_einvoice_documents_no_reopen;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_einvoice_outbox_key_frozen;");
        }
    }
}
