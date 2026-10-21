# Sổ lỗi — kiểm toán ERP HAK (21/10/2026)

Nguồn: 6 nhóm kiểm toán đọc mã song song (lõi kế toán, bảo mật, CSDL/hiệu suất, nghiệp vụ kho–mua bán–công nợ–vay, giao diện, CI/test/tài liệu) + kiểm chứng lại bằng đọc mã của người thực hiện.
**Giới hạn:** môi trường không có .NET SDK nên chưa chạy được test cục bộ; mọi phát hiện đến từ đọc mã và lần theo đường gọi. Mỗi lỗi sửa đều có test hồi quy và chỉ coi là xong khi CI đạt.
Trạng thái: **ĐÃ SỬA** (có test, chờ CI) · **CHỜ QUYẾT** (đổi quy tắc hạch toán / phân quyền / cần thiết kế — không tự ý sửa) · **CHƯA LÀM** (đã rõ cách sửa).

## Đã sửa trong đợt này
| Mã | Mức | Nội dung | Test |
|---|---|---|---|
| OPS-01 | P1 | Hai màn "Hồ sơ chu trình" và "Việc cần làm theo đơn" gọi sai đường dẫn (404): `/api/orders/{id}/flow` thay vì `/api/cycles/orders/{id}/flow`; route worklist bị lặp `cycles/cycles`. | `RouteContractTests` |
| OPS-03 | P2 | `POST /periods/{id}/lock` khóa kỳ thẳng, qua mặt bảng kiểm đóng kỳ → nay đi qua `LockCheckedAsync`. | `TradeTests` (đóng kỳ) |
| OPS-04/05 | P1 | CI: bước quét lỗ hổng NuGet luôn qua, `npm audit` bị che; không giới hạn quyền token, thời gian, trùng lần chạy. | — (workflow) |
| SEC-01 | P1 | Middleware idempotency lưu băm nội dung (mật khẩu, passphrase sao lưu) và nguyên văn phản hồi (khóa ký HMAC) vào DB/bản sao lưu 7 ngày; danh sách loại trừ sai (`/api/backups` thay vì `/api/admin/backups`), thiếu `/api/me/`, `/api/admin/users`, `/api/exchange/keys`. | `AuditFixTests.Idempotency_*` |
| SEC-03 | P2 | Người có quyền sửa nháp sửa nháp của người khác rồi tự gửi, tự duyệt (so sánh người duyệt ≠ người lập vẫn qua). Nay người sửa nội dung thành người lập. | `AuditFixTests.EditingSomeoneElsesDraft_*` |
| ACC-01 | P1 | Đảo được một bút toán đảo (kể cả bút toán đảo do phân hệ sinh) → sổ cái lệch với phân hệ/công nợ; lệnh chi có thể chi hai lần. Nay chặn. | `AuditFixTests.AReversalVoucher_*` |
| UI-01 | P1 | `GET /stock-documents` không lọc ngày cắt 1000 phiếu CŨ nhất → sau 1000 phiếu, phiếu mới biến mất khỏi màn hóa đơn mua/điều chỉnh/tổng quan. Nay giữ 1000 phiếu MỚI nhất. | (đọc mã; chưa có test HTTP) |
| UI-02 | P1 | Thuế suất nhập sai ("10%") bị coi như ô trống → hóa đơn/đơn hàng lưu thuế 0 mà báo thành công. Nay báo lỗi. | `money.test.ts` |
| UI-03 | P1 | Ctrl+S bỏ qua trạng thái bận (gửi 2 lần → 2 chứng từ nháp); Esc trong ô nhập/danh sách thả xuống đóng form, mất dữ liệu. | (hàm thuần: xem UPGRADE_PLAN) |
| UI-05 | P2 | Xóa trống ô ngày ở Thu/Chi tiền → `toISOString` ném lỗi → trắng cả màn hình. | `money.test.ts` (`daysBefore`) |
| UI-10 | P2 | Xuất CSV lệnh chi không vô hiệu hóa ô bắt đầu bằng `= + - @` (công thức Excel). | `money.test.ts` (`csvCell`) |
| BIZ-01 | P1 | Đóng khế ước vay trước khi trích lãi → mất lãi các ngày cuối. Nay bắt buộc trích lãi đến kỳ chứa ngày trả gốc cuối (chị chốt 21/10). | `LoanTests` (USD, đóng khế ước) |
| BIZ-02 | P1 | Đảo hóa đơn mua sau khi hàng đã xuất làm kẹt FIFO. Nay tính lại đơn giá khi đảo: phần còn trong kho gỡ khỏi tồn kho, phần đã xuất chuyển từ tồn kho sang giá vốn (chị chốt 21/10). | `TradeTests.ReversingAPurchaseInvoice_*` (2 test) |
| ACC-02 | P2 | Nháp do phân hệ sinh ra sửa tay được → nay không sửa tay, chỉ hủy nháp và tính lại ở phân hệ (chị chốt 21/10). `HAK.IMPORT` và chứng từ tạo tay vẫn sửa được. | `AuditFixTests.ModuleGeneratedDraft_*`, `voucherguards.test.ts` |
| ACC-03 | P2 | Kết chuyển cuối kỳ không tạo lại được sau khi đảo → nay tạo lại được (khóa nguồn hậu tố `#n`, không đổi CSDL). | `RulesCashClosingTests.Closing_CanBeGeneratedAgain*` |
| OPS-10/12 | P3 | Hai tham số không dùng (CS9113), `ExecuteSqlRawAsync` nội suy (EF1002), tệp rỗng `src/Admin.tsx`. | build |

## Chờ chị quyết (đổi quy tắc hạch toán hoặc phân quyền)
| Mã | Mức | Vấn đề | Đề xuất |
|---|---|---|---|
| SEC-02 | P2 | `sensitive.view` và `data.export` khai báo nhưng không nơi nào kiểm → mọi người có `report.view` xem lương/thuế TNCN từng nhân viên. | Kiểm `sensitive.view` ở báo cáo lương (sẽ ẩn màn Lương với kế toán thường). |
| ACC-04 | P2 | CLS nháp có thể cũ khi ghi sổ (có chứng từ chi phí ghi thêm sau khi tạo). | Tính lại khi duyệt/ghi sổ, lệch thì từ chối. |
| ACC-05 | P2 | Duyệt/ghi sổ không bắt buộc `rowVersion` → duyệt nội dung chưa xem. | Bắt buộc `rowVersion` cho duyệt/ghi sổ (đổi API, ảnh hưởng giao diện). |
| ACC-06 | P2 | Đảo chứng từ hủy phân bổ trong kỳ đã khóa → tuổi nợ kỳ đã khóa đổi. | Chặn hoặc tính theo ngày hủy. |
| BIZ-03 | P2 | Đảo phiếu xuất bình quân cuối tháng sang tháng sau khi chưa tính giá → kẹt tính giá tháng sau. | Chặn đảo khác kỳ khi chưa tính giá. |
| BIZ-04 | P2 | Hóa đơn bán nhận phiếu xuất của đối tượng khác / phiếu trả hàng NCC; hóa đơn mua không kiểm NCC khớp phiếu nhập. | Kiểm đối tượng khớp. |
| BIZ-05 | P2 | Đảo hóa đơn gốc khi còn hóa đơn điều chỉnh đã ghi sổ → giảm giá/công nợ mồ côi. | Từ chối khi còn điều chỉnh. |
| BIZ-06 | P2 | Đảo phiếu xuất NVL sau khi lệnh sản xuất hoàn thành → đếm giá trị hai lần. | Từ chối đảo phiếu thuộc lệnh đã hoàn thành. |
| BIZ-07 | P2 | BDMT dăm trả NCC: sổ BDMT trừ theo bình quân kho, hóa đơn theo tỷ lệ dòng gốc (lệch 1,2 BDMT trong ví dụ). Đã chốt 16/10: theo tỷ lệ dòng gốc. | Ghi BDMT sổ theo tỷ lệ dòng gốc. |
| SEC-08 | P3 | Quản trị hệ thống tự cấp cho mình vai trò kế toán trưởng và tắt kiểm soát hai người. | Không cho tự cấp / cần admin thứ hai. |
| SEC-07, SEC-09, SEC-10, SEC-04/05/06 | P2–P3 | Lộ tồn tại tài khoản qua thông báo/thời gian đăng nhập; chuỗi băm audit không có khóa; chữ ký sai định dạng ra 500; audit đăng nhập không giới hạn độ dài; idempotency chạy trước xác thực; không chặn nguồn `HAK.*` khi cấp khóa. | Xem BÁO CÁO chi tiết trong lịch sử phiên; làm theo ưu tiên. |

## Chưa làm (rõ cách sửa; cần di chuyển dữ liệu hoặc nhiều thay đổi)
- **Hiệu suất:** DB-01 (giá xuất kho nạp toàn bộ lịch sử cho mỗi dòng xuất; cần bảng số dư lũy kế), DB-02 (công nợ mở nạp toàn bộ dòng đã tất toán), DB-03 (BCĐPS vòng lặp bậc hai), DB-04/05 (thiếu index phủ và `journal_lines(company_id, posting_date)` — cần migration), DB-06/09 (báo cáo kho và Tổng quan quét toàn lịch sử, không cache), DB-07 (nhập danh mục hàng loạt bậc hai), DB-11, DB-12. **Chưa đo benchmark nào**, nên mọi mức ảnh hưởng là ước tính từ đọc mã.
- **Lệch trigger SQLite/PostgreSQL (DB-08):** SQLite thiếu trigger bất biến cho `file_attachments`, `depreciation_entry_splits` và cột khấu hao mới (chỉ có interceptor). Cần migration + test so tên trigger hai bên.
- **Atomicity (ACC-07, DB-10):** `StatementVoucherService` ghi audit ngoài giao dịch.
- **Giao diện (UI-04, 06–09, 11, 12):** nút Sửa làm mất nguyên tệ; tiêu đề kỳ báo cáo lấy từ ô ngày hiện tại thay vì kỳ đã tải; phiếu điều chỉnh kiểm kê tạo 2 POST không nguyên tử; thiếu `disabled={busy}` ở vài nút ghi sổ; danh sách cắt 500 dòng và lọc phía client; phản hồi cũ ghi đè phản hồi mới; không có ErrorBoundary.
- **CI/test (OPS-02, 06–09, 11, 13–15):** thiếu quét SQL injection/XSS bằng payload; thiếu test nâng cấp DB cũ; thiếu test tương đương SQLite↔PostgreSQL; thiếu lint/coverage; `gen-migration.yml` quyền ghi; số liệu test trong STATUS.md/CLAUDE.md mâu thuẫn; các route chưa có màn hình (lịch sử chứng từ, điều chỉnh chứng từ, Trung tâm JSON Exchange, phương pháp đo độ khô, cấu hình quy tắc).
