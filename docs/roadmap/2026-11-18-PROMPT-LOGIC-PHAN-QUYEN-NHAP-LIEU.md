# Prompt sửa lỗi và nâng cấp: phân quyền, chế độ một người dùng, quy trình nhập, luồng số liệu giữa các màn

Ngày rà soát 18/11/2026, trên `main` commit `4b60e19` (PR #93: tích hợp 14 PR). Tài liệu này bổ sung cho `2026-11-18-ERP-UPGRADE-PROMPT.md` (8 nhóm tổng quát) và tập trung vào năm việc người dùng yêu cầu: **sửa lỗi · nâng cấp · kiểm tra lại phân quyền và chế độ một người · quy trình nhập · xử lý số liệu giữa các màn sao cho đúng logic tính toán, ghi sổ đầy đủ, không trùng lặp.**

## 1. Hiện trạng đã kiểm tra trên main

Phân biệt rõ: **[ĐÃ XÁC NHẬN]** = đã đọc code/chạy được, **[CẦN TÁI HIỆN]** = nghi ngờ, phải dựng ca thử trước khi sửa.

Kết quả chạy nền trên main: web 77/77 test, build đạt, lint 0 lỗi (cảnh báo `only-export-components` ở `SmartFilter.tsx`); Backend SQLite: 126 domain + 475 tích hợp = 601/601 đạt (10 phút). Web 77/77. PostgreSQL và dữ liệu doanh nghiệp thật **chưa chạy** trong đợt này.

| # | Hiện trạng | Nguồn | Mức |
|---|---|---|---|
| H1 | Có 127 ô `<input type="date">` rải trong 40 tệp `web/src/*.tsx`. Ô này hiển thị theo ngôn ngữ trình duyệt, không đảm bảo dd/MM/yyyy dù đã chốt 20/10 là ngày hiển thị dd/MM/yyyy. `formatDate` có nhưng chưa có ô nhập dùng chung. | `grep 'type="date"'` | ĐÃ XÁC NHẬN |
| H2 | Giao diện **không ẩn màn/tab theo quyền**: `perms.ts` chỉ dùng cho `data.export` và `refuseReason`. Người chỉ có quyền thủ quỹ vẫn thấy mọi phân hệ rồi mới bị 403 khi bấm. Backend vẫn là nơi chặn (đã rà: các service chính đều gọi `ctx.Require`). | `perms.ts`, `common.tsx:45`, `App.tsx:222` | ĐÃ XÁC NHẬN |
| H3 | Chế độ một người: cờ `Company.RequireDistinctApprover` được tôn trọng ở **mọi** điểm phân nhiệm tìm thấy (chứng từ, phiếu kho, hóa đơn, lệnh chi, vay, tài sản, tạm ứng, kiểm kê quỹ, hợp đồng, nghiệm thu, vốn góp, mẫu báo cáo, nhà cung cấp, lệnh sản xuất, HĐĐT). Nhưng **không có thao tác "ghi sổ ngay"**: một người vẫn phải bấm Gửi duyệt → Duyệt → Ghi sổ (3 lần, mỗi lần một `rowVersion`), và nhiều màn chỉ sinh chứng từ **nháp** rồi bảo "duyệt và ghi sổ ở màn Chứng từ". | `Vouchers.tsx:132-137`, `Endpoints.cs:191-213`, `PaymentDiscount.tsx:83` | ĐÃ XÁC NHẬN |
| H4 | Số dư ban đầu có **hai lối vào cùng một chức năng**: Thiết lập → tab "Nhập" và phân hệ "Số dư ban đầu" đều mở `ImportData`. | `Setup.tsx:31`, `OpeningBalances.tsx` | ĐÃ XÁC NHẬN |
| H5 | Tám màn (`contracts`, `orders`, `services`, `trade`, `arap`, `einvoice`, `discounts`, `paydiscount`) nằm ở **cả** phân hệ Mua lẫn Bán; chiều mua/bán do phân hệ đang mở quyết định. Màn `reports` nằm ở cả Tổng hợp và Phân tích tài chính. Cần chắc chắn lọc/chiều không lẫn và bộ đếm "việc chờ" tách đúng chiều. | `Modules.tsx` | ĐÃ XÁC NHẬN cấu trúc; CẦN TÁI HIỆN lẫn chiều |
| H6 | Bộ bất biến sổ (`LedgerInvariants.AssertAsync`: bút toán cân, mỗi chứng từ một bút toán, chứng từ đã ghi ⇔ có bút toán, phân bổ không vượt dòng, tồn kho không âm, mỗi biến động một định giá) **chỉ nằm trong test**, không chạy được trên dữ liệu thật như một báo cáo kiểm soát. | `tests/.../LedgerInvariants` | ĐÃ XÁC NHẬN |
| H7 | Bảng kiểm đóng kỳ (`PeriodCloseChecklistService`) đã có 19 mục (chứng từ chờ, kho↔sổ cái, công nợ↔sổ cái, TSCĐ↔sổ cái, vay↔sổ cái, sao kê, GRNI, khấu hao, lãi vay, VAT, HĐĐT, kết chuyển) nhưng **không có** mục cho: lương (bảng lương ↔ sổ cái), sản xuất/WIP/giá thành chốt, tạm ứng (sổ tạm ứng ↔ TK tạm ứng), trích trước chiết khấu (`REBATE_PAYABLE/RECEIVABLE` ↔ thỏa thuận), vốn góp/cổ tức, sổ VAT ↔ bảng kê, phương trình kế toán Tài sản = Nợ + Vốn, kiểm kê quỹ chưa xử lý. | `PeriodCloseChecklistService.cs` | ĐÃ XÁC NHẬN thiếu mục |
| H7b | Chạm `ctx.Require` ở mọi hàm công khai chưa chứng minh được bằng tĩnh: 23 hàm nhận `SessionContext` không tự gọi `Require` trong thân (đa số ủy quyền cho hàm lõi đã kiểm tra; `ComputeAsync` của `SalesOrderCheckService`, `DispatchOneAsync`/`PostIssuedAsync` của HĐĐT, `CreateFromScheduleAsync` của bảo trì là hàm nội bộ gọi từ nơi đã kiểm tra). Phải có test HTTP chứng minh từng endpoint. | script rà ngày 18/11 | CẦN TÁI HIỆN bằng test |
| H8 | Khóa duy nhất nguồn ghi sổ có ở `vouchers(company, source_system, source_id)` (bỏ qua nháp đã hủy) và `(…, source_version)` ở bảng nguồn khác; chưa có kiểm soát tương đương cho mọi loại nghiệp vụ sinh nhiều hiệu lực kế toán (ví dụ hóa đơn ↔ phiếu kho ↔ bút toán giá vốn). | `HakDbContext.cs:248,1015` | CẦN TÁI HIỆN |

Các thay đổi giao diện làm ở nhánh `feature/phan-quyen` (ô ngày `DateInput` dd/MM/yyyy, `ThemePanel` sáng/tối + 10 kiểu màu, icon Phosphor, `DataGrid` lọc không dấu, ẩn màn theo quyền `SCREEN_PERMS`, khấu trừ GTGT chọn tài khoản/kỳ quý) **chưa nằm trong main**. Được dùng làm tham chiếu mã; không cherry-pick nguyên khối vì main đã đổi khung (Modules/FlowTab, SmartFilter).

## 2. Prompt triển khai (đưa nguyên văn cho người/AI thực hiện)

> Bạn là kỹ sư ERP phụ trách tính đúng của số liệu kế toán. Làm trên `main` mới nhất (nhánh mới cắt từ main, một chủ đề một PR nhỏ). Đọc `CLAUDE.md`, `STATUS.md`, `docs/10-CHU-TRINH-NHAT-KY.md`, `docs/03-ACCOUNTING-ENGINE.md`, `docs/audit/BUG_REGISTER.md`. **Không viết lại thứ đã có**: tìm trước, mở rộng sau. Với mỗi việc lập bảng: yêu cầu → nguồn code → ca tái hiện → sửa → test → trạng thái (code có / test tự động đạt / đối chiếu pháp lý / dữ liệu thật). Không đánh dấu "xong" khi chưa có test chạy được; không sửa sổ cũ/snapshot cũ; không tự suy tài khoản kế toán. Số tiền luôn `decimal` (UI: chuỗi/BigInt), ghi sổ trong một giao dịch, đổi model EF phải có migration cho **cả SQLite và PostgreSQL**.

### A. Sửa lỗi nền (P0 — làm trước, nhỏ, an toàn)
1. **Ngày dd/MM/yyyy**: tạo `DateInput` dùng chung (hiển thị và nhập dd/MM/yyyy, giá trị trao đổi `yyyy-MM-dd`, không phụ thuộc ngôn ngữ trình duyệt, có chọn lịch) và thay 127 `<input type="date">`. Thêm test đơn vị phân tích/hiển thị ngày và một quy tắc lint hoặc test chặn `type="date"` mới. Mọi thông báo lỗi phía máy chủ có ngày phải định dạng `{ngày:dd/MM/yyyy}` (server `InvariantGlobalization` mặc định in MM/dd/yyyy). Kỳ kế toán hiển thị `MM/yyyy`.
2. Ô tiền/số lượng: một `MoneyInput` dùng chung (nhập `1.234.567,5`, lưu chuỗi decimal, không `parseFloat`). Quét `Number(`, `parseFloat`, `toFixed` trên số tiền.
3. `SmartFilter.tsx`: tách hằng/hàm ra tệp riêng để hết cảnh báo lint; giữ nguyên hành vi.
4. Với mỗi lỗi tìm thêm được: ghi vào `docs/audit/BUG_REGISTER.md` (mã, tái hiện, nguyên nhân gốc, sửa, test).

### B. Kiểm tra lại phân quyền (P0/P1)
1. Lập **ma trận quyền** tự sinh từ code (không viết tay): hàng = endpoint (`/api/...`, phương thức), cột = quyền yêu cầu (`Permissions.*`), nguồn = service gọi `ctx.Require`. Xuất ra `docs/audit/PERMISSION_MATRIX.md` và có test so khớp với `PermissionCatalog` (quyền nào có trong danh mục mà không endpoint nào dùng, endpoint nào không yêu cầu quyền mà không nằm trong danh sách ngoại lệ `auth/setup/health/me`).
2. **Test HTTP theo vai trò** (không chỉ test service): với từng vai trò mặc định (`ADMIN`, `CHIEF_ACCOUNTANT`, `GL_ACCOUNTANT`, `ACCOUNTANT`, `CASHIER`, `APPROVER`, người chỉ xem) và một người thuộc **công ty khác**: gọi mọi nhóm endpoint (xem/tạo/sửa nháp/gửi/duyệt/ghi sổ/đảo/hủy/in/xuất/nhập/giá vốn/quản trị). Kỳ vọng: 403 đúng mã `FORBIDDEN`, không lộ dữ liệu công ty khác (kể cả qua tìm kiếm, tệp đính kèm, xuất Excel, báo cáo, `ID` đoán được → 404 chứ không 403 để không lộ sự tồn tại).
3. Chứng minh H7b: với mỗi hàm công khai không tự `Require`, hoặc thêm `Require` ở lõi, hoặc ghi rõ lý do nội bộ và test gọi qua endpoint.
4. **Giao diện ẩn/khóa theo quyền hiệu lực** (`/me/permissions`): màn/tab/nút thiếu quyền bị ẩn hoặc vô hiệu kèm lý do ("Cần quyền Duyệt chứng từ"); khi quyền chưa tải thì hiển thị trạng thái chờ chứ không bật nút tạm. Một bảng ánh xạ màn → quyền duy nhất (`SCREEN_PERMS`), có test chống lệch với danh mục quyền. Backend vẫn là nơi quyết định.
5. Màn Quản trị: xem **quyền hiệu lực của một người** theo công ty (nguồn vai trò, phạm vi), đổi quyền có audit + hiệu lực ngay (token cũ nhận quyền mới ở lần gọi kế); admin không tự cấp cho mình quyền duyệt/ghi sổ khi chế độ Team (đã có — giữ và test).
6. Phạm vi dữ liệu theo công ty ở **mọi** truy vấn: thêm test tự động duyệt qua mọi `DbSet` có `CompanyId` để chắc không có truy vấn thiếu bộ lọc công ty (đặc biệt `JournalLines`, `SettlementAllocations`, `StockMovements`, `FileAttachment`, `Audit`).

### C. Chế độ một người dùng (Solo) và Team (P1)
Cờ `RequireDistinctApprover` đã được tôn trọng ở mọi điểm phân nhiệm — **giữ nguyên, không thêm điểm kiểm tra cứng không theo cờ**. Việc cần làm là làm cho một người dùng được thuận tiện **mà vẫn đủ dấu vết**:
1. Thiết lập → "Quy trình duyệt": hai lựa chọn dễ hiểu **Một người (Solo)** / **Nhiều người (Team)**, nói rõ hệ quả từng bước; chỉ người có `config.manage`; ghi audit `company.approval_policy` (đã có API `/settings/approval`).
2. Thêm thao tác **"Ghi sổ ngay"** cho chứng từ và các chứng từ nghiệp vụ: một lệnh phía máy chủ chạy `lập (nếu chưa) → gửi → duyệt → ghi sổ` **trong một giao dịch**, cùng người dùng, sinh đủ bản ghi audit từng bước (`submit`, `approve`, `post` với đúng `userId`), kiểm tra `rowVersion` một lần. Chỉ khả dụng khi `RequireDistinctApprover = false` **và** người dùng có đủ cả ba quyền; ở Team trả `SOD_VIOLATION`/`FORBIDDEN` như cũ. Không mở đường vòng cho chứng từ đã khóa kỳ.
3. Các màn đang dừng ở chứng từ nháp ("duyệt và ghi sổ ở màn Chứng từ") — chiết khấu thanh toán độc lập, tạm ứng, kiểm kê quỹ, vốn góp/cổ tức, sao kê, lương, khấu trừ GTGT, trích lãi/khấu hao… — thêm nút "Ghi sổ ngay" khi Solo, hoặc liên kết mở đúng chứng từ (một cú bấm) khi Team. Không sao chép logic ghi sổ vào màn.
4. Chế độ Solo vẫn chặn: đảo/hủy cần lý do, đóng/mở kỳ cần quyền riêng và ghi nhật ký, nhập khẩu hàng loạt có xem trước.
5. Test: tài khoản Solo chạy trọn chu trình bán hàng và mua hàng (đơn → giao/nhận → hóa đơn → thu/chi → đối chiếu → khóa kỳ) chỉ bằng một tài khoản; cùng chu trình ở Team với hai tài khoản; thử đổi cờ giữa chừng (chứng từ đang `Submitted` khi chuyển Team→Solo và ngược lại phải xử lý đúng, không kẹt).

### D. Quy trình nhập liệu (P1)
1. Lập **bản đồ nhập liệu** `docs/audit/ENTRY_MAP.md`: với từng loại dữ liệu (tài khoản, đối tượng, vật tư, kho, số dư đầu kỳ, tồn kho đầu kỳ, TSCĐ/CCDC đầu kỳ, chứng từ chuyển đổi, hợp đồng, đơn, phiếu kho, hóa đơn, thu/chi, lương, vay, tạm ứng…): **một nguồn nhập chính duy nhất**, các màn khác chỉ đọc hoặc mở nguồn đó theo ID. Mỗi hàng có: màn nhập, API, ai có quyền, sinh bút toán gì, trạng thái, nhập lại thì sao.
2. Gộp hai lối vào số dư ban đầu (H4): giữ một nơi (khuyến nghị phân hệ "Số dư ban đầu" có thứ tự bước: tài khoản → đối tượng → vật tư → số dư → tồn kho → TSCĐ/CCDC → chứng từ), tab "Nhập" trong Thiết lập chỉ còn liên kết sang đó. Mỗi bước hiển thị đã nhập/chưa, số dòng, tổng Nợ/Có, **chênh lệch với bước trước**.
3. **Chặn nhập trùng** bằng khóa nghiệp vụ, không chỉ bằng mã: nhập lại cùng lô (SHA-256 tệp), cùng `SourceSystem+SourceId+SourceVersion`, cùng (đối tượng, ký hiệu, số hóa đơn, ngày), cùng số dư đầu kỳ theo (tài khoản, đối tượng, kỳ). Có **xem trước** ba nhóm: mới / trùng y hệt (bỏ qua) / trùng khác dữ liệu (hỏi xác nhận). Hai giao dịch cùng ngày cùng số tiền vẫn có thể hợp lệ — không tự gộp.
4. Số dư đầu kỳ: chặn ghi lần thứ hai cho cùng kỳ/tài khoản/đối tượng trừ khi hủy bản cũ có lý do; kiểm tra tổng Nợ = tổng Có; công nợ chi tiết theo đối tượng cộng khớp tài khoản tổng; tồn kho đầu kỳ cộng khớp tài khoản hàng tồn kho; thẻ TSCĐ đầu kỳ khớp TK nguyên giá/hao mòn.
5. Nhập từ tệp: hoàn tác theo lô khi còn nháp; báo lỗi theo dòng/cột bằng tiếng Việt; không chạy công thức Excel từ dữ liệu nhập (chống `=`/`+`/`@` đầu ô khi xuất).
6. Nhập từ màn hình: ghi nháp tự động hoặc cảnh báo khi rời màn (đã có `unsaved.ts` — mở rộng cho mọi form dài), giữ nội dung khi lỗi sửa được, `rowVersion` ở mọi thao tác duyệt/ghi/hủy/đảo, idempotency khi mất phản hồi (đã có — kiểm lại các màn mới).
7. Test: nhập lại lô, nhập hai lô chồng nhau, nhập số dư hai lần, hai người cùng nhập một nguồn, mất mạng giữa lúc nhập.

### E. Luồng số liệu giữa các màn và logic tính toán (P0/P1)
Lập `docs/audit/DATA_FLOW_MATRIX.md`: mỗi hàng là một **bước chuyển** (nguồn → đích): hợp đồng→đơn, đơn→giao/nhận, giao/nhận→hóa đơn, hóa đơn→công nợ, thu/chi→phân bổ, phiếu kho→giá vốn, sản xuất→WIP→thành phẩm, mua sắm→tài sản→khấu hao, bảng lương→chứng từ lương→lệnh chi, vay→lãi, CKTM/CKTT→hóa đơn điều chỉnh/hạch toán điều chỉnh, ngoại tệ→chênh lệch tỷ giá, kỳ→kết chuyển→báo cáo. Mỗi hàng ghi: khóa liên kết (ID nguồn lưu ở dòng đích), số lượng/giá trị chuyển theo từng dòng, quy tắc chặn vượt, bút toán sinh ra (Nợ/Có theo vai trò TK), báo cáo nơi số này xuất hiện, test chứng minh. Với từng hàng, **kiểm tra bằng ca thử**:
1. **Kế thừa dữ liệu**: tạo từ nguồn thì lấy giá, thuế, kho, đối tượng, tiền tệ từ nguồn (lưu mapping cấp dòng); sửa ở đích không âm thầm đổi nguồn; "mở bản có sẵn" khác "sao chép thành bản mới".
2. **Chặn vượt ở backend** (số lượng và giá trị còn lại của nguồn) kể cả hai người thao tác đồng thời — thêm test đồng thời cho đơn→giao, giao→hóa đơn, hóa đơn→trả hàng, công nợ→phân bổ, xuất kho→tồn.
3. **Tính toán**: làm tròn theo số lẻ tiền tệ công ty và quy tắc đã duyệt (mỗi dòng, dòng cuối nhận phần dư khi chia), VAT theo dòng và theo hóa đơn có khớp tổng, giá xuất kho theo phương pháp từng kho (FIFO / bình quân tức thời / bình quân cuối tháng), giá vốn dăm theo MT còn giá mua/bán theo BDMT (làm tròn độ khô 2, BDMT 3), chênh lệch giá hóa đơn–phiếu nhập (tồn → kho, đã xuất → giá vốn, có cảnh báo), ngoại tệ (nguyên tệ + giá trị ghi sổ, bình quân di động khi chi), CKTM/CKTT (đúng cơ sở tính, đúng TK, không tính trên khoản CKTM). Với mỗi quy tắc: bảng ví dụ **trước/sau** bằng số cụ thể (cả ca biên: làm tròn nửa, số âm, nhiều dòng, nhiều lần giao, trả hàng sau thanh toán).
4. **Cùng một con số, một nguồn**: số tồn, số dư công nợ, số đã thu/chi, số đã hóa đơn **không** được tính lại bằng công thức riêng ở từng màn; mọi màn gọi cùng một truy vấn/dịch vụ (ví dụ công nợ mở = `SettlementService.OpenItems`, tồn = `StockService`, dư nợ vay = `LoanService`). Tìm các công thức trùng lặp ở `web/src/*.tsx` và các `*Service.cs` (tổng hợp lại ở client, cộng lại ở nhiều nơi) và gom về một chỗ; màn chỉ trình bày.
5. Màn nào hiển thị số tổng hợp phải có **đường mở xuống chứng từ nguồn** (drill-down) và con số xuống đến dòng khớp con số trên màn tổng hợp.

### F. Ghi sổ đầy đủ và không ghi hai lần (P0)
1. Danh mục **mọi sự kiện nghiệp vụ có tác động tiền/kho/công nợ/thuế/tài sản** (xuất/nhập/chuyển/kiểm kê kho, hóa đơn mua/bán/điều chỉnh/trả hàng, thu/chi/lệnh chi/sao kê, tạm ứng/hoàn ứng, kiểm kê quỹ, vay giải ngân/trả/lãi, khấu hao/ghi tăng/ghi giảm tài sản, lương, vốn/cổ tức, đánh giá lại ngoại tệ, khấu trừ GTGT, kết chuyển, CKTM/CKTT, giá thành). Với mỗi sự kiện ghi: bản ghi nghiệp vụ → chứng từ → bút toán → hiệu ứng kho/phân bổ → audit; kiểm tra **không có đường nào sinh hiệu ứng kho/phân bổ/công nợ mà không có bút toán** và **không có hai bút toán cho một sự kiện** (khóa duy nhất theo nguồn + phiên bản cho từng bước thiết kế, cặp ghi/đảo truy vết được).
2. Đưa `LedgerInvariants` thành **dịch vụ kiểm soát chạy được trên dữ liệu thật** (`IntegrityService` + API chỉ đọc + màn "Kiểm soát toàn vẹn" ở Quản trị/đóng kỳ), tính **theo công ty và kỳ**, trả danh sách vi phạm có ID nguồn: bút toán không cân; chứng từ đã ghi thiếu bút toán / bút toán thiếu chứng từ; nguồn đã ghi thiếu bút toán; bút toán thiếu nguồn; phân bổ vượt số mở; nguồn đã đảo còn bản ghi con hiệu lực; dữ liệu khác công ty; tồn kho âm; biến động kho thiếu hoặc thừa định giá; đầu kỳ + phát sinh ≠ cuối kỳ ở BCĐPS; Tài sản ≠ Nợ + Vốn. Chỉ đọc, không tự sửa.
3. Bổ sung vào bảng kiểm đóng kỳ các mục còn thiếu (H7): lương↔sổ cái, sản xuất/WIP/giá thành chốt↔sổ cái, tạm ứng↔TK tạm ứng, trích trước chiết khấu↔thỏa thuận, vốn góp/cổ tức↔sổ cái, sổ VAT↔bảng kê, kiểm kê quỹ chưa xử lý, phương trình kế toán. Mức "Chặn" / "Cần xác nhận" / "Chuyển kỳ" theo quy ước hiện có.
4. Tổng Nợ = Có theo chứng từ; công nợ chi tiết khớp sổ cái; tồn số lượng/giá trị khớp sổ cái; thanh toán phân bổ + chưa phân bổ = phát sinh; đầu kỳ + phát sinh = cuối kỳ — mỗi điều kiện có test chạy trên bộ dữ liệu xuyên chu trình và kiểm bằng `IntegrityService` sau mỗi kịch bản.
5. Báo lỗi bằng tiếng Việt chỉ rõ chứng từ/dòng/số tiền lệch và cách đối chiếu; không nuốt lỗi.

### G. Không trùng lặp giữa các màn (P1)
1. Quy tắc: **một nghiệp vụ — một nơi nhập — một nơi duyệt/ghi sổ — nhiều nơi xem**. Lập danh sách màn có thể tạo cùng loại chứng từ (ví dụ thu/chi ở Tiền, Tạm ứng, Chiết khấu thanh toán độc lập, Ngoại tệ, Sao kê, Chứng từ tổng hợp) và ghi lý do tồn tại từng cái; chứng từ **loại nghiệp vụ không được tạo qua màn Chứng từ tổng hợp** (đã giới hạn bằng `VoucherTypes.UserCreatable` — giữ và test), màn Chứng từ chỉ nhận bút toán thủ công.
2. Tám màn Mua/Bán dùng chung (H5): kiểm tra chiều (`kind`) truyền đúng ở **mọi** lời gọi (danh sách, đếm việc chờ, tìm kiếm, báo cáo, deep-link, nhớ bộ lọc); đổi phân hệ không giữ bộ lọc chiều cũ; một hồ sơ có đúng một chiều và không mở được từ chiều kia (trả 404/chuyển hướng đúng).
3. Màn `reports` ở hai phân hệ và các báo cáo cùng nội dung ở nhiều tab: chỉ một bản cài đặt, nhiều lối vào, cùng bộ lọc.
4. Danh mục: một đối tượng vừa khách vừa NCC là **một hồ sơ nhiều vai trò**; kiểm tra trùng MST/mã/đơn vị tính/kho; hợp nhất danh mục có lịch sử (không đổi tham chiếu cũ).
5. Thông báo/nhãn trạng thái dùng chung (trạng thái chứng từ, hóa đơn, HĐĐT, thanh toán) lấy từ một bảng nhãn, không tự đặt ở từng màn.

### H. Kiểm thử và nghiệm thu (bắt buộc cho mọi PR)
1. Bộ dữ liệu xuyên chu trình dùng chung (đã có `TradeTests.CrossCycle`, `ProductionFlowTests`…): thêm kịch bản **Solo** và **Team**, hai provider (SQLite/PostgreSQL), mất mạng giữa chừng, nhập lại cùng lô, hai người đồng thời. Sau mỗi kịch bản gọi `IntegrityService` và `LedgerInvariants`.
2. Test HTTP theo vai trò (mục B.2) chạy trong CI; E2E Playwright cho Solo (một tài khoản chạy trọn một chu trình) và Team (người lập ≠ người duyệt), thêm kiểm "người chỉ xem không thấy nút ghi".
3. Mỗi PR báo: yêu cầu xử lý, ca tái hiện, ví dụ **trước/sau bằng số**, test đã chạy (số đạt/tổng), tác động migration, phần chưa làm. Phân biệt **code đã có · test tự động đạt · đối chiếu pháp lý · nghiệm thu dữ liệu thật**; không ghi DONE dữ liệu thật khi chưa đối chiếu.
4. Không đưa dữ liệu doanh nghiệp thật vào repository; dữ liệu mẫu đã lược danh tính.

## 3. Thứ tự và đầu ra

| Đợt | Việc | Đầu ra nghiệm thu |
|---|---|---|
| P0-1 | A (ngày/tiền dùng chung), F.2 (`IntegrityService` chỉ đọc) | `DateInput`/`MoneyInput`; báo cáo toàn vẹn chạy trên bộ dữ liệu mẫu, 0 vi phạm; test |
| P0-2 | B.1–B.3, B.6 (ma trận + test HTTP theo vai trò + phạm vi công ty) | `PERMISSION_MATRIX.md`; test HTTP đạt cho 8 vai trò; kết luận H7b |
| P1-1 | C (Solo: "Ghi sổ ngay", chọn Solo/Team) + B.4–B.5 (UI theo quyền, quyền hiệu lực) | Chu trình bán/mua chạy một tài khoản; ảnh trước/sau; E2E |
| P1-2 | D (bản đồ nhập, gộp lối vào số dư, chặn trùng, xem trước) | `ENTRY_MAP.md`; test nhập lại/chồng lô/hai lần số dư |
| P1-3 | E + F.3–F.5 (ma trận luồng dữ liệu, một nguồn số, bảng kiểm đóng kỳ đủ mục) | `DATA_FLOW_MATRIX.md`; bộ ca số học trước/sau; bảng kiểm đủ mục |
| P2 | G (gỡ trùng lặp màn, chiều mua/bán, báo cáo một bản) | Danh sách màn hợp nhất, test chiều, ảnh |

## 4. Điều chưa biết / cần người dùng xác nhận trước khi làm
- Chế độ Solo có được "Ghi sổ ngay" cho **mọi** loại chứng từ không, hay giữ riêng bước ghi sổ với các chứng từ có tiền ra (lệnh chi, chi tiền)? (Đề xuất: cho phép, nhưng vẫn ghi audit từng bước và yêu cầu xác nhận lần hai với chi tiền.)
- Hợp nhất lối vào "Số dư ban đầu": giữ phân hệ riêng hay đưa hẳn vào Thiết lập? (Đề xuất: giữ phân hệ riêng có thứ tự bước.)
- Phạm vi dữ liệu theo chi nhánh/kho/bộ phận (ngoài công ty) có làm đợt này không? (Đề xuất: để đợt sau; đợt này chỉ công ty + vai trò.)
- Mã tài khoản mẫu ở bộ quy tắc kết chuyển vẫn theo TT200 (đã chốt 20/10: giữ làm mẫu, người lập tự chọn).

## 5. Ghi chú về chứng cứ
Tài liệu này không khẳng định lỗi nào ngoài các mục "ĐÃ XÁC NHẬN". Các mục "CẦN TÁI HIỆN" là giả thuyết có căn cứ trong code, phải dựng ca thử trước khi sửa. Kết quả chạy test PostgreSQL và dữ liệu doanh nghiệp thật chưa có ở đợt rà soát này.
