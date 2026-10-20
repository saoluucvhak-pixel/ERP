# HAK Accounting Offline ERP — hướng dẫn cho phiên làm việc

Người dùng nói tiếng Việt, **xưng "chị"** (16/10/2026 tự xưng "chị"; trước đó ghi nhầm là "anh"), gọi trợ lý là "em"; trả lời bằng tiếng Việt.

## Cách làm việc người dùng yêu cầu
- **Làm tiếp liên tục** theo danh sách "Bước tiếp theo" bên dưới; không chờ được giao lại việc.
- Câu hỏi chưa được trả lời → ghi vào mục "Câu hỏi đang chờ", **chuyển sang phần khác**, quay lại khi người dùng trả lời.
- Sau khi bị giới hạn chat / mở phiên mới: đọc file này + `STATUS.md`, làm tiếp từ "Đang làm dở".
- Mỗi đợt xong: chạy toàn bộ test, cập nhật `STATUS.md` và file này.

## Lệnh thường dùng (PowerShell)
- .NET SDK 10 ở `%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe` (chưa có trong PATH).
- Test: `& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test` · Frontend: `npm --prefix web test; npm --prefix web run build`
- Migration: `$env:DOTNET_ROOT="$env:LOCALAPPDATA\Microsoft\dotnet"; $env:PATH="$env:DOTNET_ROOT;$env:PATH"; dotnet ef migrations add <Tên> --project src/Hak.Data --output-dir Migrations` — trigger bất biến viết tay bằng `migrationBuilder.Sql` cuối hàm Up.
- API dev: `dotnet run --project src/Hak.Api --urls http://127.0.0.1:5080 --Hak:Environment=Development --Hak:DatabasePath=<file db dev mới>`; web: `npm --prefix web run dev`; dữ liệu thử: `node web/scripts/dev-seed.mjs` (mật khẩu ghi vào `web/.dev-credentials.json`, không commit).
- Tiến trình `Hak.Api.exe` đang chạy sẽ khóa DLL → dừng nó trước khi build.
- Git: `& "$env:LOCALAPPDATA\Programs\PortableGit\cmd\git.exe"` (repo đã khởi tạo, nhánh main; commit sau mỗi đợt). Rust: `%USERPROFILE%\.cargo\bin`. PostgreSQL portable 18.6 (bản dùng chính): `%LOCALAPPDATA%\HAK\postgres18\pgsql\bin`, dữ liệu `...\postgres18\data`, cổng 5433, user `hak_admin`, mật khẩu trong `...\postgres18\superuser.pw` (không commit); khởi động: `pg_ctl -D <data> -o "-p 5433 -c listen_addresses=127.0.0.1" -l <base>\server.log -w start`. (Có thêm cụm cũ `%LOCALAPPDATA%\HAK\postgres` cùng cổng 5433 — đã dừng, không khởi động song song.)
- Test trên PostgreSQL: `$env:HAK_TEST_PG="Host=127.0.0.1;Port=5433;Username=hak_admin;Password=<postgres18\superuser.pw>"` rồi `dotnet test tests\Hak.Integration.Tests` (~12 phút; mỗi test tạo/xóa DB `hak_test_<guid>`).

## Nguyên tắc không thương lượng
Ghi sổ ACID; sổ cái/sổ kho/phân bổ/kết quả đo bất biến (interceptor + trigger); decimal, không float (UI dùng chuỗi/BigInt); quyền kiểm tra ở backend; không seed tài khoản/biểu mẫu pháp lý chưa xác minh (mã thử: CASH, AR, INV…); test không chạm DB thật; không đánh dấu DONE khi chưa đối chiếu dữ liệu thực.

## Quyết định nghiệp vụ đã được người dùng xác nhận
- Chênh lệch giá HĐ mua so với phiếu nhập đã ghi sổ: phần còn tồn → tồn kho, phần đã xuất → giá vốn (vai trò TK COGS), có cảnh báo + xác nhận (đã làm 2026-10-15).
- Giá xuất kho theo từng kho: FIFO / bình quân tức thời / bình quân cuối tháng.
- Dăm gỗ: BDMT = MT × độ khô % ÷ 100; đo khi nhập và đo ở tàu khi xuất (chứng từ là chứng thư); làm tròn độ khô 2, BDMT 3 chữ số; giá vốn theo MT, **giá mua/bán theo BDMT**; sổ kho giữ MT phiếu, MT chứng thư chỉ để tính BDMT.

## Câu hỏi đang chờ người dùng (không chặn việc khác)
1. Dữ liệu thực: ĐÃ CÓ thư mục dữ liệu FAST (đường dẫn ghi trong bộ nhớ riêng, không ghi ở đây; FAST, VFP, TCVN3; chỉ đọc, KHÔNG đọc SYS\dmuser). Công cụ tools/migrate-fast; 2026 khớp BCĐPS 7/7 tháng và khớp BCĐPS in từ FAST T6/2026. Chị QUYẾT ĐỊNH ÁP DỤNG TT99 từ 01/01/2026 (16/10; chuyển cả 2025 làm số so sánh, đã chạy thử khớp 19/19 tháng + B01/B02/B03 2025).

## Đã trả lời 16/10/2026 (hỏi gộp một lần)
- Sao kê không mã giao dịch, trùng tiền + ngày với đúng một bút toán: **chỉ gợi ý, bắt xác nhận tay** (không tự khớp).
- Khấu hao/phân bổ tháng đầu–cuối: mặc định **tròn tháng** (từng thẻ vẫn đổi được).
- Lãi vay: **365 ngày**, tính từ ngày giải ngân đến ngày trước ngày trả nợ.
- Trả hàng mua: chênh lệch giá HĐ – giá vốn xuất **vào giá vốn** (giữ như đang làm); BDMT hàng dăm trả lại **theo tỷ lệ dòng gốc** (không đo lại).
- Chi tiền từ TK ngoại tệ: tỷ giá ghi sổ **bình quân gia quyền di động**.
- Hàng về chưa có hóa đơn (GRNI): **TK chi tiết của 331 "Hàng về chưa có hóa đơn"** theo dõi theo NCC; có HĐ thì chuyển sang 331 chính.
- Người xác minh chế độ và mẫu BCTC: **chị (kế toán trưởng)**.
## Đã trả lời 2026-10-15
- Chênh lệch giá hóa đơn mua so với phiếu nhập đã ghi sổ: **phần còn trong kho điều chỉnh giá trị tồn kho, phần đã xuất điều chỉnh giá vốn, có cảnh báo** (người ghi sổ phải xác nhận).
- Đồng ý cài Rust / Git / PostgreSQL (cài mức người dùng; PostgreSQL portable 18.6 ở %LOCALAPPDATA%\HAK\postgres, cổng 5433, chỉ 127.0.0.1; Rust MSVC cần thêm bộ C++ của VS Build Tools — cần quyền admin).
## Đang làm dở
- GĐ5 bước 1 — PostgreSQL: XONG 2026-10-15 (105/105 test tích hợp đạt trên PostgreSQL 18.6).
- GĐ4 TSCĐ & CCDC: XONG 2026-10-15 (src/Hak.Application/Assets, màn hình "Tài sản / CCDC", migration Phase4Assets cho cả SQLite và PostgreSQL — trigger PG của tài sản nằm trong `ImmutabilityTriggers.AssetStatements()`, KHÔNG thêm bảng mới vào `Statements()` vì migration đầu gọi hàm đó). 173/173 test SQLite.
- GĐ4 Ngoại tệ (bước 1): XONG 2026-10-15 (src/Hak.Application/Fx, màn hình "Ngoại tệ", migration Phase4Fx; dòng FXR chỉ mang mã loại tiền, không nguyên tệ). 180/180 test SQLite.
- GĐ4 Công nợ ngoại tệ: XONG 2026-10-15 (SettlementAllocation.ForeignAmount, FxPaymentService "Thu/chi ngoại tệ theo hóa đơn", đánh giá lại theo khoản mở trong FxService, InvoiceService.Fx.cs dòng công nợ nguyên tệ; vai trò FX_REALIZED_GAIN/LOSS). 190/190 test SQLite, 123/123 tích hợp trên PostgreSQL.
- GĐ4 Vay: XONG 2026-10-15 (src/Hak.Application/Loans, màn hình "Vay", migration Phase4Loans; trigger PG trong ImmutabilityTriggers.LoanStatements()). 198/198 test SQLite.
- GĐ4 Hàng mua trả lại / giảm giá hàng mua: XONG 2026-10-15 (InvoiceService.Adjustments.cs: CreatePurchaseReturnIssueAsync, PurchaseAdjustmentLinesAsync; StockService.ReverseValueAdjustmentsAsync). 200/200 test.
- GĐ4 Lương (import JSON) + quản trị khóa trao đổi: XONG 2026-10-15 (src/Hak.Application/Payroll, Exchange/ExchangeKeyService.cs, schemas/payroll.schema.json, màn hình "Lương"). 205/205 test.
- GĐ5 bước 2 — API chế độ PostgreSQL (Hak:Provider=PostgreSQL, ConnectionStrings:Hak) + ghi tuần tự bằng pg_advisory_xact_lock: XONG 2026-10-15.
- GĐ4 Giá thành sản xuất: XONG 2026-10-15 (src/Hak.Application/Production, màn hình "Giá thành"). 209/209 test.
- Sao lưu/khôi phục mã hóa (src/Hak.Application/Backup), Audit Viewer, quản trị người dùng + khóa đăng nhập sai + đồng bộ quyền mặc định, màn hình "Quản trị": XONG 2026-10-15. 215/215 test.
- Báo cáo: BCĐPS cộng dồn TK cha, Sổ Nhật ký chung, xuất Excel .xlsx (web/src/xlsx.ts, nút ExportButton trong common.tsx): XONG 2026-10-15. 216/216 test.
- Màn hình Thiết lập (TK, vai trò TK, kho/vật tư, năm TC) + tạo công ty trong Quản trị + xuất Excel các sổ: XONG 2026-10-15. 217/217 test.
- Nhập danh mục TK / đối tượng / số dư đầu kỳ từ Excel/CSV: XONG 2026-10-15 (src/Hak.Application/Setup/CatalogImportService.cs, web/src/sheetread.ts + ImportData.tsx). 232 test.
- Lần chạy đầu (tạo quản trị đầu tiên trên màn hình đăng nhập, /api/setup/status), khai báo + xác minh chế độ kế toán trên giao diện, lệnh khôi phục mật khẩu quản trị tại máy (`Hak.Api.exe --reset-admin-password <user>`), docs/08 hướng dẫn vận hành: XONG 2026-10-15. 235 test.
- Đo hiệu năng (docs/09, tools/Hak.Bench; ghi log vào %TEMP%\hak-bench vì scratchpad có thể không ghi được) + bật pooling SQLite + sửa sổ chi tiết O(n²): XONG 2026-10-15. 235 test SQLite, 163/163 tích hợp PostgreSQL.
- Report Engine (src/Hak.Domain/Reports, Application/Reports/ReportService.cs, màn hình "Báo cáo theo mẫu", migration Phase6Reports — trigger PG trong ImmutabilityTriggers.ReportStatements()): XONG 2026-10-15. 248 test. Lưu ý EF: thêm entity con có khóa GUID gán sẵn qua `db.AddRange`, không qua navigation của cha đang tracked (bị hiểu là UPDATE → lỗi xung đột).
- Đợt 15/10 (sau Report Engine): phân trang chứng từ, in (CSS + phiếu kế toán có số tiền bằng chữ), nhập vật tư / tồn kho đầu kỳ / TSCĐ-CCDC đầu kỳ, đổi TK cấp trên, tạo chứng từ từ dòng sao kê (gợi ý phí/lãi). 252 test; 170/170 PG. Lưu ý: server chạy InvariantGlobalization → `string.Normalize` không tách dấu; bỏ dấu tiếng Việt bằng bảng (StatementVoucherService.Plain).
- 16/10: TT99 bản gốc → `legal/TT99-2025` (Phụ lục II-A 184 TK, tệp nhập đề xuất, đề xuất vai trò). Đã nhận đủ Công báo (1579+1580 ngày 16/10). Đã dựng mẫu B01-DN, B02-DN (legal/TT99-2025/mau-*.json; nhãn TK DH/TDT/HCSD/HTM/BCC/SXDD/NO/BDSDT/DGL/LV/TLTS; NET_D/NET_C bỏ chứng từ CLS; MAX0).
- Mã luồng tiền + B03-DN (trực tiếp): XONG 16/10 (Domain/Reports/CashFlow.cs, cột cash_flow_code ở voucher_lines/journal_lines, hàm CF(), cột Mã LCTT trong form chứng từ).
- B03 gián tiếp + B09 (8 mẫu số liệu + khung văn bản) + mẫu nhiều cột (ReportDefinition.Columns, migration Phase6ReportColumns) + hàm PS_D/PS_C, FXR, VT_D/VT_C: XONG 16/10. 260 test SQLite. Sinh mẫu: `python legal/TT99-2025/tools/templates.py %TEMP%\hak-tt99\tables legal/TT99-2025` và `python legal/TT99-2025/tools/b09.py legal/TT99-2025`. Còn: thử tay mẫu nhiều cột trên giao diện; phần văn bản B09 nhập trong phần mềm (hiện là tệp .md).
- Cây TK cha–con dạng modal (Setup.tsx Accounts + Modal trong common.tsx; SetupService.AccountTree.cs: AddChildAccountAsync, UpdateAccountAttributesAsync; API /accounts/{id}/children, /attributes) + áp dụng các quyết định 16/10 (sao kê không mã → chờ xác nhận; chi ngoại tệ theo bình quân di động; GRNI theo NCC): XONG 16/10. 262 test.
- Tiếp theo: nhập mẫu BCTC khi có TT99 Phụ lục IV đã xác minh (chỉ là dữ liệu mẫu, công cụ đã sẵn) → bộ cài Tauri/MSI (GĐ8; gói chạy độc lập tools/package/package.ps1 đã XONG 2026-10-15; Tauri cần VS C++ — cần quyền admin; tệp .ps1 phải lưu UTF-8 CÓ BOM cho PowerShell 5.1) → GĐ7 đối chiếu dữ liệu thật (chờ thư mục).
- 18/10: Khấu trừ thuế GTGT cuối kỳ: `Tax/VatOffsetService.cs` (PreviewAsync/GenerateAsync; Nợ VAT_OUTPUT / Có VAT_INPUT = min(số dư đầu vào, số dư đầu ra) lũy kế đến hết kỳ, gồm TK con; chứng từ NHÁP nguồn `HAK.VAT.OFFSET`, mỗi kỳ một chứng từ; API GET/POST /api/vat/offset; tab "Khấu trừ thuế cuối kỳ" ở màn Thuế GTGT). Test VatTests.PeriodEndOffset…. 86 + 182 test đạt. Cách hạch toán khấu trừ GTGT giống nhau ở TT99 và TT200 (chị xác nhận 20/10) — không cần đối chiếu riêng theo TT99; TK 133/3331 cụ thể do công ty ánh xạ qua vai trò VAT_INPUT/VAT_OUTPUT. Ngoại tệ (đánh giá lại + chênh lệch đã thực hiện) đã có từ 15/10; CÒN: kết chuyển FX_REVALUATION sang 515/635 (đang qua bộ quy tắc kết chuyển).
- 19/10: Khấu trừ GTGT cho CHỌN tài khoản: VatOffsetService.Preview/Generate nhận danh sách TK đầu ra (Nợ) và đầu vào (Có) tùy chọn (rỗng = theo vai trò VAT_OUTPUT/VAT_INPUT + TK con); không cho trùng TK hai phía; API GET /vat/offset?inputAccounts=&outputAccounts= (CSV id), POST body InputAccountIds/OutputAccountIds; màn Thuế GTGT → Khấu trừ có ô thêm/bỏ TK. Test VatTests.PeriodEndOffset_UsesTheAccountsTheUserPicks…. Chị đã xác nhận 19/10: 1331 và 1332 KHÔNG tách (khấu trừ gộp). Đầu vào dư: để nguyên số dư TK 133, năm sau làm số đầu kỳ của tờ khai (không kết chuyển/hoàn thuế; Preview cộng lũy kế nên tự tính cả số dư năm trước). Kỳ kê khai CHỌN được tháng/quý (quý dương lịch; chứng từ ghi ngày cuối quý, khóa chống trùng theo kỳ kế toán cuối quý; API tham số quarterly; lựa chọn lưu localStorage hak.vat.frequency; test PeriodEndOffset_Quarterly…).
- 20/10 (từ PR #1): công tắc lập/duyệt theo công ty (Company.RequireDistinctApprover) nay áp cả lệnh chi ngân hàng (PaymentOrder.Approve) và xác minh mẫu báo cáo; API GET/PUT /settings/approval; Thiết lập → tab "Quy trình duyệt" (ghi nhật ký company.approval_policy). Tắt công tắc = một người được lập và duyệt (doanh nghiệp nhỏ); mặc định bật.
- 20/10 (từ PR #1): admin hệ thống TỰ TẠO / SỬA vai trò riêng chọn theo phân hệ và chức năng (Domain/Security/PermissionCatalog.cs = danh mục quyền theo phân hệ, nguồn kiểm tra duy nhất — thêm quyền mới ở Permissions thì thêm cả vào danh mục này; SetupService.PermissionCatalogAsync/CreateRoleAsync/UpdateRoleAsync; API /api/admin/permission-catalog, POST /admin/roles, PUT /admin/roles/{code}; màn Quản trị → Vai trò (web/src/Roles.tsx)). Vai trò mặc định chỉ xem, không sửa; thêm vai trò mặc định ACCOUNTANT "Kế toán" (lập chứng từ, tiền, kho, hóa đơn, thanh toán; KHÔNG duyệt/khóa kỳ). Giao diện chưa ẩn phân hệ theo quyền ở khung mới.
- 20/10 (từ PR #1): Thiết lập → tab "Hạch toán tự động" (web/src/AutoPosting.tsx): bảng tình trạng thiết lập các bút toán tự động (vai trò TK hóa đơn / khấu trừ GTGT / chênh lệch tỷ giá, kết chuyển, giá xuất kho, TK mặc định thẻ CCDC, duyệt) + soạn và kích hoạt bộ quy tắc kết chuyển cuối kỳ (mẫu gợi ý TT200 chỉ điền sẵn form — CHỜ KTT đối chiếu TT99). API mới GET /api/closing/rule-sets (ClosingService.ListRuleSetsAsync). Năm vai trò TK mặc định tùy chọn ASSET_COST_DEFAULT / ASSET_ACCUMULATED_DEFAULT / ASSET_EXPENSE_DEFAULT / TOOL_COST_DEFAULT / TOOL_EXPENSE_DEFAULT: form thẻ TSCĐ/CCDC mới điền sẵn tài khoản theo loại thẻ (vẫn đổi được). KHÔNG đưa sang: cách tính khấu hao mặc định cấp công ty (cần migration; main đã có phương pháp khấu hao từng thẻ ở đợt 17) và chia chi phí nhiều TK của PR #1 (trùng AssetExpenseSplit của đợt 16).
- Lưu ý khi thêm entity có decimal: phải thêm `HasConversion(money)` trong `ApplySqliteMoneyStorage` (SQLite), PostgreSQL tự áp numeric(19,4).

## Định hướng kiến trúc mới (19/10/2026, chị chốt) — xem docs/10-CHU-TRINH-NHAT-KY.md
**Bám tệp "Phân tích thiết kế hệ thống ERP" chị gửi** (không tự thêm khái niệm ngoài tệp). Xây theo **chu trình** (doanh thu, cung ứng, chuyển đổi; nhân sự–lương và tài chính chỉ được giới thiệu trong tệp). Quy trình tệp: **ghi nhận** (chứng từ nghiệp vụ = dữ liệu phát sinh) → **xử lý** (ghi **Nhật ký + Chi tiết Nhật ký**, sổ cái, sổ chi tiết = dữ liệu chuyển đổi) → **báo cáo** (tập tin tồn/tổng hợp, BCĐPS, BCTC = dữ liệu kết quả). Khâu **phát sinh nghiệp vụ kế toán = có ghi Nhật ký** (xuất/nhập kho, hóa đơn, thu/chi); đơn đặt hàng, yêu cầu mua hàng, lệnh bán hàng, hợp đồng: không. Cuối chu trình này thì chu trình sau tự nhận (ví dụ cung ứng "chấp nhận thanh toán" → hàng chờ thanh toán theo ngày đáo hạn → thanh toán tự tạo lệnh Nháp). Chị đã giao em quyết các điểm mở theo đề xuất (mục 8 tài liệu): lệnh thanh toán tự tạo ở trạng thái Nháp, người khác duyệt; hạn mức tín dụng = trình người có thẩm quyền; chiều phân tích và chu trình chuyển đổi hoãn. **Chị muốn trọng tâm là thiết kế luồng dữ liệu, không hỏi dồn các câu về ai duyệt/khâu nào.** Đã xong: lát cắt Cung ứng → Thanh toán (mục 9), đợt 2 (mục 9b: danh mục theo tệp, kiểm tra tín dụng + tồn kho khi xác nhận đơn bán, danh mục cần đặt hàng) đợt 3 (mục 9c: chu trình doanh thu — lệnh bán hàng, phiếu giao hàng, hóa đơn từ phiếu giao hàng, hàng chờ thu → phiếu thu, báo cáo bán hàng/thu tiền) đợt 4 (mục 9d: biên bản kiểm nghiệm, đối chiếu nhiều bên chặn chấp nhận thanh toán, menu theo chu trình + bảng xếp phân hệ vào khâu) đợt 5 (mục 9e: kế hoạch tiền theo ngày đáo hạn, thông báo nợ quá hạn) và đợt 6 (mục 9f: duyệt + xếp hạng nhà cung cấp, đơn mua nháp từ danh mục cần đặt) và đợt 7 (mục 9g: giá vốn/lãi gộp trong phân tích tiêu thụ, hàng chờ trả lương → lệnh thanh toán lương Nháp) và đợt 8 (mục 9h: chu trình quản lý TSCĐ/CCDC — đề nghị mua sắm, bàn giao, kiểm kê; menu chuyển sang nhóm Tài chính) và đợt 9 (mục 9j: giao diện ô ứng dụng + thanh trạng thái, chu trình sản xuất theo tài liệu thiết kế chị gửi, lịch trả nợ vay vào kế hoạch tiền). **Giao diện (mục 9k): thanh bên theo phân hệ + tab Quy trình theo ảnh mẫu chị gửi; chị dặn GIỮ NGUYÊN các màn phiếu, lệnh, nhập liệu — chỉ đổi khung ngoài; "Tổng quan" làm như MISA, còn "Việc cần làm" chuyển sang mục "Quản lý chu trình" (nội dung giữ nguyên).** **Chị đã gửi thêm 3 tài liệu .md (thiết kế chu trình tài chính, chu trình sản xuất, kiến trúc CSDL tổng thể) — mục 9j đối chiếu từng bảng.** PR #3 mở cho đợt 1–4 (đã theo dõi). **Sổ việc chưa làm của các chu trình đã lưu ở mục 10 của docs/10 — đọc mục đó khi mở phiên mới.** Tiếp theo: chiết khấu thanh toán, hàng trả lại/xóa nợ có phê duyệt ở chu trình doanh thu, màn dòng chảy cho các chu trình còn lại; chiều phân tích khi luồng ổn.

## Bước tiếp theo
1. ~~Đơn hàng~~ (xong 2026-10-15, 156 test).
2. GĐ4: ~~Thuế GTGT~~ (xong 2026-10-15, 160 test); ~~TSCĐ & khấu hao; CCDC phân bổ~~ (xong 2026-10-15, 173 test); ~~ngoại tệ: đánh giá lại tiền/vay~~ (xong 2026-10-15, 180 test); ~~công nợ ngoại tệ + chênh lệch tỷ giá đã thực hiện~~ (xong 2026-10-15, 190 test); ~~vay~~ (xong 2026-10-15, 198 test); ~~hàng mua trả lại / giảm giá hàng mua~~ (xong 2026-10-15, 200 test); ~~lương (import JSON)~~ (xong 2026-10-15, 205 test); ~~giá thành~~ (xong 2026-10-15, 209 test).
3. GĐ5 đa công ty/LAN (PostgreSQL), GĐ6 Report Engine + BCTC (cần TT99 đã xác minh), GĐ7 đối chiếu dữ liệu thực, GĐ8 bộ cài.

- 19/10 đợt 11: 2 đơn vị tính (Item.AltUnit/AltFactor cố định; BDMT ước tính bình quân thời điểm cho xuất chưa đo) — docs/10 mục 9l.

- 19/10 đợt 12: bảo trì & sửa chữa tài sản theo tệp CMMS (AssetRepairService: YCSC, phiếu thi công, lịch bảo trì, KPI MTBF/MTTR) + Nhật ký tài sản (AssetJournalService, nút "Nhật ký") — docs/10 mục 9m.

- 19/10 đợt 13: quản lý hợp đồng mua bán (ContractService, ContractControl chặn đơn kế thừa, màn "Hợp đồng") — docs/10 mục 9n. Kiểm tra kiểu web: `npm --prefix web run build` (không dùng `tsc -p .`).

- 20/10 đợt 14: SmartFilter dùng chung (web/src/SmartFilter.tsx, filter.ts) cho tài sản, hợp đồng, đơn hàng, YCSC, phiếu thi công; sửa tràn ngang trên điện thoại — docs/10 mục 9o. Quy tắc CSS khung đặt cuối index.css để không bị đè.

- 20/10 đợt 15: số hiển thị kiểu 1.234.567,5 (server: VnNumber.Vn(), web: formatMoney); mọi bộ lọc ngày mặc định đầu tháng → hôm nay (SmartFilter dateLabel, monthStart() trong money.ts) — docs/10 mục 9p. Ngày trong bảng còn dạng ISO.

- 20/10 (chị chốt): ngày hiển thị dd/MM/yyyy (web formatDate, server DateOnly.Vn()); Báo cáo theo mẫu mặc định từ đầu năm, các bộ lọc ngày khác đầu tháng → hôm nay.

- 20/10 đợt 16: chia chi phí khấu hao/phân bổ cho nhiều bộ phận (AssetExpenseSplit, ExpenseSplitter, báo cáo theo bộ phận) — docs/10 mục 9q.

- 20/10 đợt 17: phương pháp khấu hao/phân bổ tùy chọn từng thẻ (đường thẳng, theo thời gian số ngày, theo sản lượng, số dư giảm dần) + kỳ ghi sổ tháng/quý/năm đổi được sau ghi tăng (DepreciationCalculator.Target/IsBookingPeriod, AssetUsage) — docs/10 mục 9r.

- 20/10 đợt 18: đối chiếu khấu hao (legal/KHAU-HAO; thẻ TSCĐ mới mặc định tính theo ngày), độ khô theo ngày làm đơn vị phụ BDMT như tỷ giá (DrynessDailyRate; ưu tiên đo > độ khô ngày > bình quân thời điểm), Excel ngày dd/MM/yyyy — docs/10 mục 9s.

- 20/10 đợt 19–20: tạm ứng/hoàn ứng (vai trò ADVANCE), kiểm kê quỹ (CASH_SHORTAGE/CASH_SURPLUS), số TK ngân hàng đối tượng/công ty + in UNC + xuất CSV/Excel, vốn góp & cổ tức (CAPITAL_CONTRIBUTED, DIVIDEND_SOURCE/PAYABLE/PIT_PAYABLE) — docs/10 mục 9t, 9u. Mẫu: chứng từ nháp qua AccountingEngine, trạng thái lấy từ trạng thái chứng từ (không ghi riêng). Test PG cục bộ: cụm 16 cổng 5432, `HAK_TEST_PG="Host=127.0.0.1;Port=5432;Username=hak_admin;Password=x"` (tạo role bằng su postgres).

- 20/10 đợt 21: phê duyệt trả hàng/giảm giá bán (cờ công ty RequireSalesAdjustmentApproval, StockDocumentLine.SourceInvoiceId), xóa nợ khó đòi có duyệt (BAD_DEBT_PROVISION/EXPENSE), Item.RequiresQc chặn ghi sổ phiếu nhập từ NCC, in biên bản kiểm kê/bàn giao tài sản — docs/10 mục 9v.

- 20/10 đợt 22: bảo trì di động — nhãn QR (qr.ts, AssetQr.tsx), trang `#/m` (MobileMaintenance.tsx), PWA (public/manifest.webmanifest, sw.js), ảnh sự cố FileAttachment (AttachmentService; thư mục attachments cạnh DB — cần sao lưu) — docs/10 mục 9w.

- 20/10 đợt 23: bộ mã chứng từ (docs/11-QUY-TAC-MA.md; loại mới khai báo ở DocumentKinds và cấp số bằng Numbering.NextAsync, không tự viết hàm cấp số), chiết khấu thanh toán (PAYMENT_DISCOUNT_INCOME/EXPENSE), dải dòng chảy các màn còn lại — docs/10 mục 9x.

- 20/10 đợt 24: nhóm danh mục nhiều cấp, mỗi mục nhiều nhóm (CatalogGroup/Member, GroupService, màn "Nhóm danh mục", groupLib.tsx; báo cáo theo nhóm không cộng được) — docs/10 mục 9y. Chưa: mặc định theo nhóm, tiền tố mã theo nhóm, nhập Excel.

- 20/10 đợt 25: mặc định theo nhóm cho đối tượng (hạn mức, hạn thanh toán, thuế suất vật tư; theo ERP khác: mục khai > nhóm chính > kế thừa nhóm cha > nhóm sâu nhất/giá trị chặt nhất; GroupDefaults) — docs/10 mục 9z. Mã đối tượng giữ như hiện tại (chị: tạm thời vậy).

- 20/10 đợt 26: dịch vụ (Item.IsService không kho; đơn → biên bản nghiệm thu dịch vụ NTDV/NTDM (ServiceAcceptanceService) → hóa đơn trong phạm vi nghiệm thu; TK doanh thu/chi phí trên mặt hàng) — docs/10 mục 9aa. InventoryAccountId của vật tư có thể null (dịch vụ).

- 20/10 đợt 27: báo cáo dịch vụ (ServiceReportService), lịch nghiệm thu theo hợp đồng (ContractServicePlanService), mốc + chấm giờ (ServiceProgressService) — docs/10 mục 9ab.

- 20/10 đợt 28: sổ nhật ký đặc biệt + xử lý bút toán trùng (rút tiền, hoàn ứng mua vật tư: mỗi bút toán một sổ chính — chị chốt ưu tiên tiền mặt: thu tiền → chi tiền → bán hàng → mua hàng, sổ khác đánh dấu trùng không cộng; SpecialJournalService, Domain/Ledger/SpecialJournals.cs) — docs/10 mục 9ac.
