# Sổ lỗi — kiểm toán ERP HAK (21/10/2026)

Nguồn: 6 nhóm kiểm toán đọc mã song song (lõi kế toán, bảo mật, CSDL/hiệu suất, nghiệp vụ kho–mua bán–công nợ–vay, giao diện, CI/test/tài liệu) + kiểm chứng lại bằng đọc mã của người thực hiện.
**Giới hạn:** môi trường không có .NET SDK nên chưa chạy được test cục bộ; mọi phát hiện đến từ đọc mã và lần theo đường gọi. Mỗi lỗi sửa đều có test hồi quy và chỉ coi là xong khi CI đạt.
Trạng thái: **ĐÃ SỬA** (có test, chờ CI) · **CHỜ QUYẾT** (đổi quy tắc hạch toán / phân quyền / cần thiết kế — không tự ý sửa) · **CHƯA LÀM** (đã rõ cách sửa).

## Đã sửa trong đợt này
| Mã | Mức | Nội dung | Test |
|---|---|---|---|
| OPS-01 | P1 | Hai màn "Hồ sơ chu trình" và "Việc cần làm theo đơn" gọi sai đường dẫn (404): `/api/orders/{id}/flow` thay vì `/api/cycles/orders/{id}/flow`; route worklist bị lặp `cycles/cycles`. | `ApiTests.EveryWebApiCall_HasAServerRoute`, `ApiTests.FullFlow_OverHttp` |
| OPS-03 | P2 | `POST /periods/{id}/lock` khóa kỳ thẳng, qua mặt bảng kiểm đóng kỳ → nay đi qua `LockCheckedAsync`. | `TradeTests` (đóng kỳ) |
| OPS-04/05 | P1 | CI: bước quét lỗ hổng NuGet luôn qua, `npm audit` bị che; không giới hạn quyền token, thời gian, trùng lần chạy. | — (workflow) |
| SEC-01 | P1 | Middleware idempotency lưu băm nội dung (mật khẩu, passphrase sao lưu) và nguyên văn phản hồi (khóa ký HMAC) vào DB/bản sao lưu 7 ngày; danh sách loại trừ sai (`/api/backups` thay vì `/api/admin/backups`), thiếu `/api/me/`, `/api/admin/users`, `/api/exchange/keys`. | `AuditFixTests.Idempotency_*` |
| SEC-01b | P1 | Dữ liệu cũ của SEC-01: bản ghi chống gửi trùng đã lưu trước bản sửa vẫn chứa khóa ký HMAC / băm mật khẩu. Nay tự xóa khi khởi động (`IdempotencySecretPurge`), khóa có bí mật trong đó ghi `exchange_key.exposed` và màn Lương → Khóa trao đổi báo "đã lộ, cần cấp lại"; thêm thao tác **Cấp lại** (ngừng khóa cũ + cấp khóa mới trong một giao dịch). Quy trình: docs/08 §7a. | `PayrollTests.SecretPurge_*`, `PayrollTests.ExchangeKeys_Rotate_*` |
| DB-08 | P2 | SQLite thiếu trigger bất biến cho `file_attachments`, `depreciation_entry_splits` (chỉ có interceptor); cả hai bên chưa khóa các cột khấu hao thêm sau của thẻ đã ghi tăng (`declining_factor`, `schedule_end_date`, `total_units`, `unit_name`) và `company_id`/`period_start`/`created_at` của số khấu hao. Nay migration `Phase15ImmutabilityParity` (SQLite + PostgreSQL) khớp với interceptor; `allocation_frequency` vẫn đổi được. | `ImmutabilityParityTests` (so phạm vi trigger hai bên từ script migration, so với database thật), `AssetTests.UpgradeFromPreviousVersion_*` (nâng cấp database có dữ liệu) |
| SEC-07 | P2 | Lộ tài khoản qua thông báo/thời gian đăng nhập (đã sửa trước) và **khóa hộ tài khoản người khác**: sai 5 lần từ bất kỳ đâu khóa cả tài khoản. Nay khóa theo nguồn (máy chạy API + địa chỉ kết nối, server xác định) trong bảng `login_throttles`; khóa cả tài khoản chỉ khi 20 lần sai từ mọi nguồn (dò phân tán); quản trị đặt lại mật khẩu gỡ mọi khóa. | `SecurityHardeningTests.WrongPasswordsFromOneSource_*`, `DistributedGuessing_*` |
| SEC-09 | P2 | Chuỗi băm nhật ký kiểm toán không có khóa → sửa được tệp CSDL thì tính lại được chuỗi. Nay HMAC-SHA256 với khóa con sinh từ khóa chủ (`h1:`); mỗi chuỗi có mốc chuyển đổi ký bằng khóa (`audit_chain_anchors`, chỉ thêm) nên không hạ được đuôi chuỗi về băm thường hay xóa mốc mà không bị phát hiện; bản ghi cũ vẫn kiểm được; kiểm bản sao lưu dùng cùng khóa. | `SecurityHardeningTests.KeyedAuditChain_*`, `ChainAnchors_AreAppendOnly` |
| SEC-10 | P2 | Kiểm chữ ký gói JSON giải mã chữ ký người gửi bằng `Convert.FromBase64String` (chỉ dựa vào schema để không ra 500); đổi mật khẩu không giới hạn số lần thử mật khẩu hiện tại. Nay `PackageCrypto.Verify` không bao giờ ném lỗi với chữ ký sai định dạng; nhập sai mật khẩu hiện tại tính vào bộ đếm khóa theo nguồn, đến ngưỡng đóng mọi phiên. | `SecurityHardeningTests.PackageSignature_*`, `ChangeOwnPassword_*` |
| SEC-04 | P3 | Nhật ký đăng nhập sai / phiên lưu nguyên văn tên đăng nhập, thiết bị dài tùy ý; mật khẩu dài tùy ý được băm PBKDF2. Nay cắt 100 ký tự trước khi ghi, nguồn tối đa 200, mật khẩu > 1024 ký tự bị từ chối không băm. | `SecurityHardeningTests.OversizedLoginInput_*` |
| SEC-06 | P3 | Cấp được khóa trao đổi cho nguồn `HAK.*` (nguồn dành cho chứng từ do phân hệ sinh). Nay từ chối `HAK` / `HAK.*`. | `SecurityHardeningTests.ExchangeKeys_RefuseTheReservedInternalSource` |
| ACC-07 / DB-10 | P2 | Ghi nhật ký kiểm toán ngoài giao dịch: `StatementVoucherService.CreateDraftAsync` lưu nháp rồi mới lưu nhật ký ở lần lưu riêng (lỗi giữa chừng để lại chứng từ không có nhật ký; kiểm trùng ngoài giao dịch nên hai người bấm cùng lúc tạo được hai nháp); `SetupService.EnsureReferenceDataAsync` (khởi động, chế độ LAN nhiều máy) cũng vậy. Nay cả hai chạy trong một giao dịch (một người ghi). | `BankingTests.DraftFromStatementLine_AndItsAuditRecord_AreOneTransaction` |
| ACC-08 | P3 | Nguyên tệ không kiểm số chữ số lẻ theo loại tiền và không kiểm mã loại tiền có trong danh mục (JPY 1,5 hay mã lạ vẫn qua). Nay kiểm ở `VoucherValidator` cho chứng từ và gói JSON; chứng từ đảo không kiểm lại (chép nguyên dòng đã ghi sổ). | `VoucherRulesTests.ForeignAmount_*`, `ForeignCurrency_UnknownCode` |
| ACC-09 | P2 | Đối tượng ở đầu phiếu thu/chi (`BusinessDocument.PartnerId`) không kiểm thuộc công ty — chỉ dòng chi tiết được kiểm; quy tắc không đưa đối tượng xuống dòng thì phiếu mang được đối tượng của công ty khác. Nay từ chối `PARTNER_WRONG_COMPANY`. | `RulesCashClosingTests.InternalTransfer_NeverTouchesRevenueOrExpense` |
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

## Đợt rà soát 24/10/2026 (mã R01–R06 + Prompt 2/3) — ĐÃ SỬA, có test hồi quy
Bộ mã **R** là của báo cáo `BAO-CAO-GITHUB.md` (24/10/2026, commit rà soát `deb7c5c`); không nhầm với bộ mã UI-/SEC-/ACC-/BIZ- ở trên. Bằng chứng: test tự động trên SQLite **và PostgreSQL 16** (348 tích hợp), kiểm tay bằng trình duyệt (Playwright) cho giao diện. PR #39–#46.

| Mã | Mức | Nội dung | Cách sửa | Test |
|---|---|---|---|---|
| R01 | P1 | Retry dùng nhầm công ty/phiên (PendingWrite không mang ngữ cảnh) | Snapshot phiên + công ty khi tạo lệnh; thao tác chờ chỉ hiện/gửi lại trong ngữ cảnh gốc; 401 của phiên cũ không đăng xuất phiên mới (`web/src/api.ts`) | `api-context.test.ts` |
| R02 | P1 | Mất quyền vẫn nhận phản hồi idempotency đã lưu | Lưu quyền yêu cầu gốc đã đòi (`required_permissions`), kiểm lại quyền HIỆN TẠI trước khi trả cache (kể cả 422) | `IdempotencyReplayAuthTests` |
| R03 | P2 | Nút Lưu đối tượng gọi hai PUT độc lập; `30abc` thành 30 | `PUT /partners/{id}/full` một giao dịch + kiểm phiên bản; `parseWholeNumber` | `PartnerFullUpdateTests`, `money.test.ts` |
| R04 | P2 | Kết quả Kiểm tra chứng từ bị thông báo chung ghi đè | `act()` đặt thông báo một lần sau khi xong; lỗi hiện màu lỗi | tay + e2e |
| R05 | P2 | Thêm nhanh không phân biệt Thu/Chi, mua/bán, Nhập/Xuất/Kiểm kê | `NavIntent` qua `navStore.go`, ý định gắn màn đích; hỏi xác nhận khi dựng lại màn đang mở | `quickadd.test.ts`, e2e |
| R06 | P2 | Cảnh báo hợp đồng bỏ sót vì `Take(500)` | Lọc công ty/trạng thái trước khi giới hạn; phân trang `GET /contracts/page` (total/hasMore) | `ContractAlertsScaleTests` |
| ACC-05 | P2 | Duyệt/ghi sổ không bắt buộc `rowVersion` | Bắt buộc ở chứng từ (submit/approve/reject/cancel/post), hóa đơn (post/reverse/cancel), phiếu kho (post/reverse/cancel), lệnh thanh toán (approve/cancel), hợp đồng (submit/decide/complete/cancel — `version` nội dung), phiếu sửa chữa tài sản. **Không áp** cho tạm ứng, cổ tức, điều chỉnh/xóa nợ bán hàng, nghiệm thu dịch vụ, đề nghị tài sản (không có đường sửa sau khi tạo) | `ApiTests`, `VersionRequiredEndpointTests`, `TradeTests`, `ContractTests` |
| ACC-04 | P2 | CLS nháp có thể cũ khi ghi sổ | `ClosingService.CheckVoucherAsync` tính lại, `CLOSING_OUT_OF_DATE` ở gửi duyệt/duyệt/ghi sổ | `RulesCashClosingTests` |
| ACC-06 | P2 | Đảo hủy phân bổ làm đổi tuổi nợ kỳ đã khóa | `VoidEffectiveDate` = ngày đảo; báo cáo tại ngày trước đó giữ nguyên | `SettlementTests.ReversingPaymentInALaterPeriod_...` |
| BIZ-04 | P2 | Hóa đơn nhận phiếu kho của đối tượng khác | `PARTNER_MISMATCH` lúc lập và lúc ghi sổ; ngoại lệ có quyền `invoice.partner_override` + lý do + nhật ký | `TradeTests.Invoice_FromStandaloneIssue_...` |
| BIZ-05 | P2 | Đảo hóa đơn gốc khi còn điều chỉnh đã ghi sổ | Từ chối, không để lại bút toán dở dang | `TradeTests.ReverseOriginal_...` |
| SEC-02 | P2 | `report.view` xem lương từng người | Lương từng người cần `sensitive.view` (tổng hợp theo phiếu vẫn `report.view`). **`data.export` vẫn chưa kiểm ở máy chủ**; hàng chờ trả lương vẫn lộ thực lĩnh cho `report.view` | `PayrollTests` |
| BIZ-06 | P2 | Đảo phiếu xuất NVL / phiếu nhập thành phẩm sau khi lệnh sản xuất hoàn thành → đếm giá trị hai lần | Chặn đảo phiếu kho và đảo hóa đơn gắn phiếu của lệnh `Completed`/`Closed` (chị chốt 24/10); đảo hóa đơn MUA bị chặn khi vật tư đã xuất cho lệnh đã hoàn thành từ ngày nhập (quy tắc thận trọng, có thể chặn dư — muốn đảo phải mở lại lệnh) | `TradeTests.ReverseInvoice_IsBlockedWhen...`, `ReversePurchaseInvoice_...` |
| SEC-02b | P2 | `data.export` và hàng chờ trả lương | Hàng chờ trả lương cần `sensitive.view` hoặc `bank.document`; nút Xuất Excel ẩn khi thiếu `data.export` (`GET /me/permissions`) — xuất file chạy ở trình duyệt nên máy chủ không chặn được, chỉ chặn dữ liệu gốc | `BankingTests`, e2e |
| UI-14 | P2 | Danh sách phiếu kho cắt 1000 dòng, lệnh thanh toán cắt 500, không có tổng số | `GET /stock-documents/page`, `/payment-orders/page` (total, hasMore) + Tải thêm | e2e phân trang |
| UI-15 | P2 | Công nợ / Chiết khấu thanh toán dùng chung hai phân hệ | Bán hàng chỉ phải thu, Mua hàng chỉ phải trả; bỏ nút chuyển chiều chiết khấu | e2e |
| OPS-16 | P2 | Bảng kiểm đóng kỳ thiếu khấu hao, lãi vay, khấu trừ GTGT, kết chuyển | Thêm 4 mục "cần xác nhận" (không chặn khóa) dẫn tới màn xử lý | `TradeTests.CloseChecklist_...` |
| UI-16 | P3 | Worklist thiếu người phụ trách, lệnh sản xuất, đề nghị tài sản | Cột "Người có thể làm" (người có quyền làm bước kế tiếp, bỏ người lập khi cần tách người); thêm lệnh sản xuất và đề nghị mua sắm tài sản | `TradeTests.HomeTasks_...`, e2e |
| UI-13 | P2 | Hai chu trình mua/bán lẫn trong một màn (đơn, hóa đơn, hợp đồng, nghiệm thu dịch vụ), số việc chờ cộng gộp, cảnh báo/báo cáo hợp đồng trộn hai chiều | Bỏ nút chuyển chiều, chiều do phân hệ quyết định, đếm việc theo chiều, báo cáo hợp đồng lọc theo chiều, worklist chia nhóm theo chu trình | `TradeTests.HomeWorkQueue_...`, `ContractTests`, e2e |
| PG-01 | **P1** | Migration chỉ sinh cho SQLite → PostgreSQL báo `PendingModelChangesWarning`, hỏng mọi test tích hợp (PR #39–#44) | Sinh thêm bộ `src/Hak.Data.Postgres/Migrations` — **luôn sinh CẢ HAI bộ** khi đổi model | CI job `backend-postgres` |
| TOOL-01 | P2 | `web/scripts/dev-seed.mjs` và `tools/migrate-fast/hak_import.mjs` gọi submit/approve/post không kèm `rowVersion` | Chuyền phiên bản vừa nhận sang bước sau. `hak_import.mjs` **chưa chạy với dữ liệu thật** | seed chạy được |

Còn mở từ đợt này: chạy `tools/migrate-fast/hak_import.mjs` trên dữ liệu thật (chưa có dữ liệu để thử); job CI `e2e` chưa có kết quả trên GitHub Actions; diễn tập nâng cấp DB cũ nhiều đời và khôi phục DB+tệp+khóa trên máy thật; chuẩn hóa thao tác chung (busy / quyền / phiên bản / ngữ cảnh / kết quả chưa xác định) thành một cơ chế duy nhất; xử lý 54 cảnh báo lint theo mức ảnh hưởng.

## Chờ chị quyết (đổi quy tắc hạch toán hoặc phân quyền)
| Mã | Mức | Vấn đề | Đề xuất |
|---|---|---|---|
| BIZ-03 | P2 | Đảo phiếu xuất bình quân cuối tháng sang tháng sau khi chưa tính giá → kẹt tính giá tháng sau. | Chặn đảo khác kỳ khi chưa tính giá. |
| BIZ-07 | P2 | BDMT dăm trả NCC: sổ BDMT trừ theo bình quân kho, hóa đơn theo tỷ lệ dòng gốc (lệch 1,2 BDMT trong ví dụ). Đã chốt 16/10: theo tỷ lệ dòng gốc. | Ghi BDMT sổ theo tỷ lệ dòng gốc. |
| SEC-08 | P3 | Quản trị hệ thống tự cấp cho mình vai trò kế toán trưởng và tắt kiểm soát hai người. | Không cho tự cấp / cần admin thứ hai. |

## Chưa làm (rõ cách sửa; cần di chuyển dữ liệu hoặc nhiều thay đổi)
- **Hiệu suất:** DB-01 (giá xuất kho nạp toàn bộ lịch sử cho mỗi dòng xuất) — **GIẢM MỘT PHẦN 21/10**: thay chuỗi truy vấn theo lô 500 mã bằng một truy vấn nối; độ dốc theo lịch sử giảm ~3×, đo ở `docs/09-HIEU-NANG.md`; muốn phẳng hẳn cần bảng số dư lũy kế (migration), DB-02 (công nợ mở nạp toàn bộ dòng đã tất toán) và DB-03 (BCĐPS vòng lặp bậc hai) — **đã đo ở 12.000 chứng từ: dưới 0,3 s, chưa cần sửa**; xem lại khi dữ liệu lớn hơn, DB-04/05 (thiếu index phủ và `journal_lines(company_id, posting_date)` — cần migration), DB-06/09 (báo cáo kho và Tổng quan quét toàn lịch sử, không cache), DB-07 (nhập danh mục hàng loạt bậc hai), DB-11, DB-12. **Chưa đo benchmark nào**, nên mọi mức ảnh hưởng là ước tính từ đọc mã.
- **Giao diện (UI-04, 06–09, 11, 12):** nút Sửa làm mất nguyên tệ; tiêu đề kỳ báo cáo lấy từ ô ngày hiện tại thay vì kỳ đã tải; phiếu điều chỉnh kiểm kê tạo 2 POST không nguyên tử; thiếu `disabled={busy}` ở vài nút ghi sổ; danh sách cắt 500 dòng và lọc phía client; phản hồi cũ ghi đè phản hồi mới; không có ErrorBoundary.
- **CI/test (OPS-02, 06–09, 11, 13–15):** thiếu quét SQL injection/XSS bằng payload; test nâng cấp DB cũ mới phủ bản liền trước → bản mới (`AssetTests.UpgradeFromPreviousVersion_*`), chưa phủ các bản cũ hơn; thiếu test tương đương SQLite↔PostgreSQL; thiếu lint/coverage; `gen-migration.yml` quyền ghi; số liệu test trong STATUS.md/CLAUDE.md mâu thuẫn; các route chưa có màn hình (lịch sử chứng từ, điều chỉnh chứng từ, Trung tâm JSON Exchange, phương pháp đo độ khô, cấu hình quy tắc).


## Chuẩn hóa thao tác chung + dọn lint (24/10/2026)
- `web/src/actions.ts`: một nơi cho quyền (`needs`), chống bấm đúp (`exclusive`), `withRowVersion`; `useAction().run(fn, opts)` dùng chung; kiểm thử `actions.test.ts`. Bối cảnh công ty/phiên, dedupe ghi trùng, kết quả chưa xác định vẫn nằm ở `api.ts` + `ErrorBox`.
- Lint 54 → 25: còn lại toàn `react/only-export-components` (chỉ ảnh hưởng Fast Refresh khi dev). Giờ in dùng `<PrintedAt />` (cập nhật ở sự kiện beforeprint); các effect tải dữ liệu có chú thích `oxlint-disable-next-line … -- lý do` từng dòng, không tắt luật hàng loạt.


## Kiểm tra 25/10/2026 (commit 8fab99d) — xử lý
Nguồn: `BAO-CAO-LUONG-VA-HIEU-SUAT.md`, `PROMPT-NANG-CAP-ERP.md` của chị. Mỗi phát hiện có test hồi quy.

| Mã | Mức | Xử lý | Test |
|---|---|---|---|
| F01 | P1 | `quickadd.ts` → `navIntents.ts` (không còn trùng `QuickAdd.tsx` trên Windows); test chặn trùng tên module không phân biệt hoa/thường; job CI `frontend-windows` build thật | `modulenames.test.ts` |
| F02 | P1 | Bản ghi idempotency trước Phase17 (`RequiredPermissions = NULL`) KHÔNG phát lại payload, KHÔNG xóa rồi chạy lại: giữ làm dấu chống ghi trùng, trả 409 `IDEMPOTENCY_LEGACY_RECORD` yêu cầu đối soát. Bản ghi mới luôn có chuỗi quyền (rỗng nếu không đòi quyền) nên phát lại bình thường | `IdempotencyReplayAuthTests.LegacyRecordWithoutPermissionMetadata_…` |
| F03 | P2 | Nội dung JSON chụp thành chuỗi một lần khi tạo thao tác; dedupe, lần gửi đầu, tự gửi lại, "thử lại an toàn" cùng một chuỗi + khóa | `api-retry.test.ts` |
| F04 | P2 | Thay slot đơn bằng tập thao tác chưa xác định theo `opId` (`InFlight/Uncertain/Confirmed/Rejected`), lọc theo phiên + công ty; nút "Thử lại an toàn" gửi đúng thao tác của lỗi hiển thị. Chưa lưu qua tải lại trang (cần thiết kế lưu an toàn không chứa token — xem "Còn lại") | `api-retry.test.ts` |
| F05 | P2 | `e2e/run.mjs` chạy Vite bằng Node + CLI (không `npx`), `taskkill /T` trên Windows, giữ log API/web, thoát sớm thì fail ngay, readiness kiểm HTML + proxy; `E2E_BROWSER_CHANNEL`/`E2E_BROWSER_PATH`; bỏ toàn bộ `waitForTimeout`; test đỏ giữ ảnh + log console ở `web/e2e/artifacts` | `npm run e2e` 10/10 |
| F06 | P2 | `OrderFlowService.WorklistAsync` xếp ưu tiên trên MỌI đơn đang mở (duyệt theo lô 100) rồi mới phân trang; trả `items/total/hasMore`, tham số `skip/take`; giao diện có "Xem thêm" | `TradeTests.OrderWorklist_SortsBeforePaging_…` |

**Bất biến chéo chu trình** (`tests/Hak.Integration.Tests/LedgerInvariants.cs`): sau luồng nghiệp vụ, mọi bút toán cân Nợ = Có, mỗi chứng từ đúng một bút toán, chứng từ đã ghi sổ ⇔ có bút toán, phân bổ còn hiệu lực không vượt dòng, tồn kho không âm, mỗi biến động tối đa một định giá còn hiệu lực. Đã gắn vào 7 test luồng của `TradeTests` (đơn bán → giao → hóa đơn → thu; cung ứng → thanh toán; chu trình doanh thu; đảo bị chặn; sản xuất hoàn thành; 4 trục trạng thái).

**Hiệu suất** (SQLite, máy 4 CPU của phiên này, Release; số tham khảo, không so với máy Windows của chị): 10.000 chứng từ + 2.000 nhập/2.000 xuất mỗi kho: ghi sổ 75 chứng từ/giây (p95 18,7 ms); BCĐPS 138 ms; sổ chi tiết 6.667 dòng 98 ms; tuổi nợ 49 ms; phiếu xuất sau 1.000 → 4.000 biến động: 16–20 ms → 36–39 ms (tăng tuyến tính nhẹ, chưa nghẽn). Câu tốn nhất trong kho là `MAX(movement_date)` theo kho–vật tư (0,54 ms × 8.000). Thử thêm chỉ mục `(company, warehouse, item, movement_date)`: tổng thời gian 2.000 nhập + 2.000 xuất không đổi (77,8 s → 77,9 s) → **không áp dụng** (tránh tốn chi phí ghi, thêm migration). Không cần checkpoint giá bình quân/FIFO ở quy mô này; đo lại ở 50k–100k biến động trước khi quyết.

**Còn lại / chưa làm (cần quyết định hoặc dữ liệu):**
- F04 bền qua tải lại trang: cần API tra cứu theo `operationId` phía máy chủ rồi mới lưu hàng chờ (chỉ id + khóa, không token).
- Benchmark 50k/100k chứng từ, nhiều người dùng, PostgreSQL; N+1 của `OrderFlowService.WorklistAsync` (mỗi đơn gọi `FulfilmentAsync`) — chưa đo ở >500 đơn mở.
- Luồng chưa có test xuyên chu trình đủ 8 tình huống mỗi luồng (lỗi giữa transaction, hai người đồng thời, mất quyền…): mới có bất biến chung + các test sẵn có; BDMT theo tỷ lệ dòng gốc khi trả hàng và đảo phiếu bình quân cuối tháng sang kỳ khác vẫn là mục mở.


## Đợt xử lý tồn đọng 25/10/2026 (sau PR #50–#53)
- **F04 bền qua tải lại:** thao tác chưa xác định lưu ở sessionStorage của tab (id, khóa chống trùng, phương thức, đường dẫn, nội dung đã chụp, công ty — KHÔNG lưu token), khôi phục gắn với phiên hiện tại, đăng xuất xóa hết; dải nhắc `PendingBanner` ở khung chính có nút "Thử lại an toàn". Không cần API tra cứu phía máy chủ vì thử lại dùng chính khóa chống trùng cũ. Test `api-retry.test.ts`.
- **useAction `needs` / `exclusive`:** áp cho Gửi duyệt / Duyệt / Ghi sổ / Đảo ở Chứng từ và Ghi sổ / Đảo ở Hóa đơn.
- **Hóa đơn điện tử:** trigger bất biến SQLite + PostgreSQL, dịch vụ nền, đối soát hóa đơn điều chỉnh, hóa đơn mua từ nhà cung cấp (xem docs/12).
- **Chiết khấu:** thỏa thuận theo doanh số tính cả hóa đơn ngoại tệ.
- **Test luồng:** hóa đơn thay thế khi hóa đơn gốc chưa đảo được (còn điều chỉnh đã ghi sổ) ⇒ phát hành rồi nhưng chưa ghi sổ, không có nửa bút toán, đối soát nhắc đảo, sửa xong thì ghi sổ lại.
- **PostgreSQL:** chạy trọn bộ test tích hợp trên `main` sau #53: 365/365.
- **Hiệu suất 50.000 chứng từ** (docs/09 mục "Đo bổ sung 25/10/2026"): ghi sổ 73/giây ổn định, báo cáo dưới 1 giây, phiếu xuất tăng ~7 ms mỗi 1.000 biến động cùng kho–vật tư ⇒ checkpoint kho chỉ đáng làm khi một cặp kho–vật tư vượt ~20.000 biến động/kỳ. Chưa đo nhiều người dùng và PostgreSQL ở quy mô này.

### Bổ sung 25/10/2026 (làm tiếp)
- Test xuyên chu trình thêm (`TradeTests.CrossCycle.cs`, 6 tình huống, đạt trên SQLite và PostgreSQL): hai người ghi sổ cùng hóa đơn mua → một bút toán; phiên bản cũ bị từ chối và không ghi gì; mất quyền (lập / kiểm toán không ghi sổ, không đảo); lỗi giữa giao dịch ghi sổ (lệch giá chưa xác nhận) → hoàn tác toàn bộ; hủy tranh với ghi sổ → một trạng thái nhất quán; đảo hai lần bị chặn, số dư về 0. Sau mỗi tình huống chạy `LedgerInvariants`.
- Đo nhiều người dùng đồng thời trên PostgreSQL: xem docs/09.

