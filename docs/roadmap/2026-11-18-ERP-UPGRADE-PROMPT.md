# Prompt nâng cấp HAK ERP theo 8 nhóm yêu cầu
Ngày rà soát 18/11/2026. Dùng tài liệu này làm đầu bài triển khai, nghiệm thu và theo dõi tiến độ.

## Bối cảnh và đánh giá hiện trạng
Dự án C#/.NET, React, SQLite một máy và PostgreSQL cho LAN. Bản tích hợp xuất phát từ main df1ede0, hợp nhất PR #62, #63, #64, #78, #82–#87 và #89–#92. Danh sách commit gộp và kết quả cuối nằm trong báo cáo tích hợp của đợt 18/11.

| Nhóm | Đã thấy trong code | Phần cần hoàn thiện |
|---|---|---|
| 1. Quy trình ERP | Hợp đồng, đơn hàng, kho, hóa đơn, thu/chi, duyệt, ghi sổ, worklist và liên kết chứng từ | Chuẩn hóa một luồng dẫn thao tác, quy định quyền sở hữu dữ liệu và xử lý ngoại lệ giữa các phân hệ |
| 2. Dữ liệu và hạch toán | Giao dịch DB, row version, idempotency, sổ bất biến, liên kết nguồn và đối chiếu công nợ | Rà mọi đường nhập API/import/UI, theo dõi tác vụ chưa xác định, chống trùng theo khóa nghiệp vụ và đối chiếu xuyên chu trình |
| 3. Design | Sidebar, module tabs, bảng, form, modal, CSS sáng/tối | Chuẩn hóa thành phần dùng chung, giảm tab và nội dung hướng dẫn thường trực, cải thiện khả năng đọc |
| 4. Một người và phân quyền | Quyền backend theo vai trò/công ty, permission catalog, RequireDistinctApprover | Thiết lập Solo/Team dễ hiểu, màn quyền hiệu lực theo người, giới hạn dữ liệu và đồng bộ nút theo quyền |
| 5. Báo cáo | ReportDefinition có phiên bản, trạng thái xác minh, công thức, snapshot, xuất Excel; có bộ mẫu TT99 | Hồ sơ đối chiếu từng mẫu pháp lý, in PDF thực tế, chuyển phiên bản theo hiệu lực, tùy biến có kiểm soát |
| 6. Bố cục | Đã hỗ trợ desktop/mobile và vùng bảng cuộn | Pilot CKTM, Hóa đơn, Chứng từ; phân tách danh sách/chi tiết/form, luôn nhận diện công ty và kỳ |
| 7. Lưu trữ/khôi phục | Sao lưu mã hóa DB và attachments, kiểm toàn vẹn/audit, worker lịch SQLite, DataRoot và công cụ đổi nơi lưu | Diễn tập máy mới có khóa giải mã, bản chép ngoài máy được kiểm hash, lịch PostgreSQL, xử lý đổi ổ khi app đang chạy |
| 8. Vận hành và mở rộng | CI đa nền tảng, test SQLite/PG/E2E, log/audit, khóa kỳ | Quan sát tác vụ, SLO hiệu suất, phát hành có rollback, migration dữ liệu thật, kiểm toán dependency liên tục |

Ảnh kiểm tra CKTM ở 1440 px và 390 px trên dữ liệu giả cho thấy sidebar và tab đã rõ nhóm, nhưng có nhiều tab ngang, hướng dẫn chiếm phần trên màn hình và khoảng trống lớn khi ít dòng. Ở 390 px thanh phân hệ cuộn ngang và tên công ty bị ẩn. Độ rộng trang đo được 390/390 px. Đây là mẫu đã xem trực tiếp; các màn còn lại cần nghiệm thu theo ma trận bên dưới.

Các thành phần đối chiếu chính gồm App.tsx, Modules.tsx, common.tsx, index.css, RebateAmendments.tsx, Orders.tsx, ReportService.cs, AccountingEngine.cs, SettlementService.cs, AutoBackupWorker.cs, BackupService.cs và tools/package.

## Tài liệu tham khảo
- Odoo dùng các mốc khóa sổ để kiểm soát chứng từ đã ghi. Áp dụng vào HAK bằng chính sách khóa/mở kỳ hiện có và lịch sử phê duyệt theo công ty. [Odoo — Year-end closing](https://www.odoo.com/documentation/19.0/applications/finance/accounting/reporting/year_end.html)
- ERPNext tổ chức ledger từ chứng từ nghiệp vụ và giữ dấu vết ghi/đảo, phù hợp với nền sổ bất biến của HAK. [ERPNext — Immutable Ledger](https://docs.frappe.io/erpnext/immutable-ledger-in-erpnext)
- ERPNext phân quyền theo vai trò, loại chứng từ, hành động và giới hạn tài liệu. [ERPNext — Role Based Permissions](https://docs.frappe.io/erpnext/permissions)
- Business Central có panel thông tin liên quan theo dòng được chọn. HAK có thể dùng cách bố trí tương tự cho nguồn, công nợ, tệp đính kèm và lịch sử. [Microsoft — FactBoxes](https://learn.microsoft.com/en-us/dynamics365/business-central/dev-itpro/developer/devenv-adding-a-factbox-to-page)
- Quyền truy cập Business Central được tổ chức bằng permission sets. [Microsoft — Manage Access](https://learn.microsoft.com/en-us/dynamics365/business-central/admin-access-overview)
- Tài liệu Frappe thể hiện backup/restore gồm database và files; cấu hình, khóa mã hóa cần được quản lý khi phục hồi site. [Backup](https://docs.frappe.io/framework/user/en/bench/reference/backup), [Restore](https://docs.frappe.io/framework/user/en/bench/reference/restore), [Site configuration](https://docs.frappe.io/framework/user/en/basics/site_config)

Các nguồn trên là tham khảo thiết kế. Quy tắc thuế, thời điểm ghi nhận và biểu mẫu của HAK phải được đối chiếu theo chế độ áp dụng tại Việt Nam.

## Prompt triển khai
Bạn là kỹ sư ERP phụ trách nghiệp vụ, dữ liệu, UI/UX và chất lượng phát hành của HAK. Đọc main mới nhất, CLAUDE.md, STATUS.md và các source liên quan. Kiểm tra chức năng đã có trước khi viết mới. Lập bảng yêu cầu → nguồn code → lỗi tái hiện hoặc khoảng trống → thay đổi → test → trạng thái nghiệm thu.

Triển khai theo các đợt nhỏ có thể review. Với thay đổi tiền, VAT, kho, công nợ hoặc số liệu báo cáo, đưa ví dụ trước/sau và chứng từ đối chiếu vào PR. Dùng dữ liệu giả hoặc bản sao đã được phép. Tiến độ phân biệt code đã có, test tự động đã đạt, mẫu pháp lý đã đối chiếu và dữ liệu doanh nghiệp đã nghiệm thu.

### 1. Chuẩn hóa quy trình ERP
1. Lập bản đồ chu trình với chủ thể phụ trách, trạng thái, ngày nghiệp vụ, ngày hạch toán, dữ liệu đầu vào/đầu ra, quyền và điểm kiểm tra.
2. Bán hàng đi từ hợp đồng/đơn → giao hàng hoặc nghiệm thu dịch vụ → hóa đơn → công nợ → thu tiền/phân bổ → đối chiếu → đóng kỳ. Hỗ trợ giao nhiều lần, hóa đơn nhiều lần, đặt cọc, trả hàng, giảm giá, CKTM và CK thanh toán.
3. Mua hàng đi từ đề nghị/đơn → nhận hàng/nghiệm thu → nhận hóa đơn → đối chiếu đơn–nhập–hóa đơn → phải trả → chi/phân bổ. Hỗ trợ nhận trước hóa đơn, GRNI và chênh lệch giá phân về kho/giá vốn theo chính sách đã xác nhận.
4. Các luồng kho, sản xuất, tài sản/CCDC, lương và vay phải chuyển dữ liệu sang kế toán qua nguồn định danh. Kiểm tra các nhánh phân bổ đã đi vào WIP, thành phẩm, tài sản và kỳ giá thành đã chốt.
5. Worklist chỉ ra việc tiếp theo, người phụ trách, lý do chặn và chứng từ cần mở. Người dùng đi tiếp từ hồ sơ hiện tại, dữ liệu liên quan được lấy từ nguồn.
6. Chứng từ nháp được sửa theo quyền. Chứng từ đã ghi đi qua điều chỉnh/đảo với quan hệ nguồn, lý do và kỳ mở. Mở lại kỳ ghi người thực hiện, phạm vi và thời gian hiệu lực.
7. Trạng thái hóa đơn điện tử, kế toán và hồ sơ thuế phải được hiển thị độc lập. Mỗi trạng thái hoàn tất phải có chứng cứ tương ứng.

Nghiệm thu ít nhất chu trình bán hàng, mua hàng, hàng về chưa có hóa đơn, trả hàng sau thanh toán, điều chỉnh sau khóa kỳ, CKTM hồi tố/lũy tiến, dịch vụ và ngoại tệ. Với từng luồng, từ báo cáo mở được đến sổ, chứng từ và nguồn nghiệp vụ.

### 2. Dữ liệu liền mạch, chống trùng và nguyên tắc hạch toán
1. Xác định nguồn chính thức cho khách hàng/NCC, vật tư, kho, tài khoản, hợp đồng, đơn, phiếu kho, hóa đơn và thanh toán. Một hồ sơ có ID chuẩn, các phân hệ lưu tham chiếu và snapshot cần thiết.
2. Tạo từ nguồn phải kế thừa dữ liệu và lưu mapping cấp dòng. Tách rõ thao tác mở hồ sơ hiện có với sao chép thành hồ sơ mới. Chặn vượt số lượng/giá trị ở backend kể cả khi hai người thao tác đồng thời.
3. Kiểm soát trùng import theo hệ thống nguồn + external ID + phiên bản và khóa nghiệp vụ phù hợp công ty/đối tượng/ký hiệu/số. Có preview bản trùng, khác dữ liệu và cần xác nhận. Hai giao dịch cùng ngày/cùng số tiền vẫn có thể hợp lệ.
4. Giữ khóa idempotency cho cùng yêu cầu và chủ thể/công ty. Nếu mất phản hồi, truy hồi kết quả bằng cùng khóa. Thay đổi payload phải được coi là quyết định mới. Kiểm tra quyền tại thời điểm gửi lại.
5. Thiết lập ràng buộc duy nhất phù hợp trên nguồn ghi sổ để một nghiệp vụ chỉ sinh một hiệu lực kế toán cho từng bước được thiết kế. Cặp ghi/đảo và lịch sử thay thế phải truy vết được.
6. Ghi chứng từ, journal, stock effects, settlement allocation và audit trong ranh giới giao dịch phù hợp. Provider hóa đơn bên ngoài cần trạng thái gửi/tra cứu/kết quả chưa rõ, khóa gửi và phục hồi sau lỗi DB.
7. Áp dụng decimal, số lẻ theo loại tiền, quy tắc làm tròn đã duyệt. Ngoại tệ theo dõi đồng thời nguyên tệ và giá trị ghi sổ, VAT theo mô hình hiện hành. Sửa allocation phải đối chiếu cả hai phía.
8. CKTM, giảm giá, trả hàng và CK thanh toán có cơ sở tính/tài khoản/trạng thái riêng. Bổ sung/thu hồi nối chuỗi snapshot, tránh dùng lại trích trước hoặc đưa chính khoản CKTM vào cơ sở tự sinh quyền lợi.
9. Tạo báo cáo kiểm soát định kỳ về nguồn thiếu journal, journal thiếu nguồn, phân bổ vượt số mở, nguồn đã đảo còn con hiệu lực và dữ liệu khác công ty.
10. Test đồng thời, retry sau timeout, crash sau provider đã nhận, import lại cùng lô, nguồn cũ thiếu metadata và dữ liệu biên làm tròn.

Điều kiện nghiệm thu gồm tổng Nợ = Có theo voucher, công nợ chi tiết khớp sổ cái, tồn kho số lượng/giá trị khớp sổ, thanh toán phân bổ + chưa phân bổ khớp số phát sinh, đầu kỳ + phát sinh = cuối kỳ. Mỗi sai lệch phải chỉ ra nguồn và cách đối chiếu.

### 3. Nâng cấp design chuyên nghiệp
1. Tạo token cho màu, font, khoảng cách, kích thước control, viền, focus và trạng thái. Dùng hệ thống icon thống nhất, font hỗ trợ tiếng Việt và chạy offline.
2. Một trang có tiêu đề rõ, hành động chính và trạng thái cần xử lý. Gom thao tác phụ theo ngữ cảnh hàng/chứng từ. Nội dung hướng dẫn dài đặt trong trợ giúp có thể mở khi cần.
3. Thành phần dùng chung gồm PageHeader, FilterBar, DataTable, StatusBadge, MoneyInput, FormSection, DetailPanel, ConfirmDialog và EmptyState, hoặc tên phù hợp cấu trúc hiện có.
4. Chuẩn hóa bảng với cột tiền canh phải, số dạng tabular, đơn vị, ngày dd/MM/yyyy, số dòng, tổng và phân trang. Cho chọn cột, độ rộng và bộ lọc theo người dùng/công ty.
5. Modal có focus ban đầu, chỉ lớp trên cùng xử lý bàn phím và trả focus về nút mở. Form nhiều dòng chuyển sang workspace có đủ chiều rộng.
6. Trạng thái tải/rỗng/lỗi/thành công thể hiện gần thao tác và giữ nội dung đang nhập khi lỗi có thể sửa.
7. Lấy CKTM, Hóa đơn và Chứng từ làm pilot. So sánh ảnh trước/sau và đo số thao tác để hoàn thành một hồ sơ.

### 4. Chế độ một người sử dụng và phân quyền
1. Dùng chính sách công ty hiện có để hỗ trợ Solo và Team. Solo cho phép một người thực hiện lập/duyệt/ghi theo quyền được cấp và cấu hình RequireDistinctApprover. Team áp dụng phân nhiệm khi được bật.
2. Vai trò quản trị hệ thống, quản trị công ty, kế toán trưởng, kế toán, thủ quỹ, kho, bán hàng và chỉ xem phải có quyền hiệu lực dễ kiểm tra. Một người có thể có nhiều vai trò theo từng công ty.
3. Xây dựng ma trận xem, tạo, sửa nháp, gửi duyệt, duyệt, ghi sổ, đảo, hủy, in, export, import, xem giá vốn và quản trị. Phạm vi dữ liệu có thể theo công ty, chi nhánh, kho, bộ phận hoặc hồ sơ phụ trách.
4. Thực thi quyền ở API/service và kiểm tra công ty ở truy vấn. Các đường file tải xuống, báo cáo, tìm kiếm và export phải cùng phạm vi.
5. UI hiện đúng hành động, trạng thái bị chặn có lý do. Khi quyền chưa tải, dùng trạng thái chờ phù hợp, tránh nút khả dụng tạm thời.
6. Màn quản trị hiển thị quyền thực tế theo một người, nguồn vai trò và phạm vi. Thay đổi quyền có audit, thời điểm hiệu lực và cơ chế thu hồi phiên phù hợp.
7. Đổi công ty, đăng xuất, hết phiên và chuyển phân hệ xử lý dữ liệu nhập dở thống nhất. Nếu người dùng chọn ở lại, giữ form và ngữ cảnh.
8. Test ít nhất tài khoản Solo, người lập Team, người duyệt Team, chỉ xem và người thuộc công ty khác. Test quyền trực tiếp bằng HTTP và từ giao diện.

Nghiệm thu Solo thực hiện được chu trình hoàn chỉnh theo chính sách đã chọn; Team kiểm được các bước phân nhiệm và giới hạn dữ liệu.

### 5. Mẫu biểu báo cáo, pháp lý và tùy biến
1. Lập registry mẫu với mã, loại báo cáo, chế độ áp dụng, phiên bản, ngày hiệu lực, nguồn gốc văn bản/phụ lục, mapping tài khoản, người xác minh và lịch sử thay đổi.
2. Đã xác nhận metadata TT99/2025/TT-BTC có hiệu lực 01/01/2026. Metadata NĐ254/2026/NĐ-CP và TT91/2026/TT-BTC ghi hiệu lực 01/07/2026. Khi triển khai phải đọc bản gốc, phụ lục, điều khoản chuyển tiếp và văn bản sửa đổi áp dụng tại kỳ báo cáo.
3. Đối chiếu từng mẫu B01, B02, B03, B09, sổ kế toán, chứng từ, bảng kê VAT và hồ sơ hóa đơn. Lưu điều khoản/phụ lục hỗ trợ từng yêu cầu. Chức năng được triển khai không tự chứng minh mẫu đã đủ pháp lý.
4. Mẫu pháp lý có phiên bản và bước xác minh. Tùy biến cột, logo, bộ lọc, chiều phân tích và mẫu quản trị phải lưu phiên bản, người sửa và phạm vi. Nếu sửa công thức/chỉ tiêu pháp lý thì cần xác minh lại phần bị tác động.
5. Báo cáo hỗ trợ drill-down đến journal/chứng từ, cột so sánh, số đầu/cuối kỳ, đơn vị, dấu âm, số tiền ngoại tệ và tổng theo cấp.
6. Nghiệm thu B01 cân, B02 khớp kết quả kinh doanh, B03 khớp biến động tiền theo phương pháp áp dụng, B09 đối chiếu chỉ tiêu liên quan và sổ chi tiết. Kiểm cả kỳ có số âm, năm chuyển đổi và dữ liệu điều chỉnh.
7. Kiểm bản in A4/A3, ngang/dọc, lặp tiêu đề, đánh số trang, ký tên/chức danh và phân trang bảng dài. Excel phải giữ đúng giá trị tiền/ngày, chống công thức không mong muốn từ dữ liệu nhập.
8. Với báo cáo đã ký/nộp, lưu snapshot, phiên bản mẫu, dữ liệu nền và dấu vết điều chỉnh sau đó.

Nguồn cần đọc và đối chiếu theo kỳ
- [TT99/2025/TT-BTC — Công báo và các phần phụ lục](https://congbao.chinhphu.vn/van-ban/thong-tu-so-99-2025-tt-btc-46529/59658.htm)
- [NĐ254/2026/NĐ-CP — Cổng Chính phủ](https://chinhphu.vn/?docid=218689&pageid=27160)
- [TT91/2026/TT-BTC — Cổng Chính phủ](https://vanban.chinhphu.vn/?classid=1&docid=219006&orggroupid=4&pageid=27160)

### 6. Bố cục hài hòa và dễ sử dụng
1. Khung chung gồm điều hướng phân hệ, thanh công ty/kỳ/người dùng, breadcrumb, tiêu đề và workspace. Ở desktop danh sách đi cùng panel chi tiết theo dòng chọn.
2. Tách màn danh sách, hồ sơ xem chi tiết và form sửa theo trạng thái. Một form nghiệp vụ chính có thể mở từ nhiều điểm điều hướng. Khi đổi sang điểm mở khác, dùng cùng ID và cùng quy tắc.
3. Chia form thành thông tin chung, dòng hàng/dịch vụ, tài khoản/thuế, tổng và kiểm tra trước ghi sổ. Tổng quan trọng nằm gần hành động xác nhận.
4. Tránh lặp màn nhập cùng loại nghiệp vụ ở Quy trình, Tổng hợp, Thu/Chi và Hóa đơn. Với bút toán thủ công vẫn có màn riêng và lý do sử dụng, đồng thời phát hiện hồ sơ đã sinh từ nguồn.
5. Tối ưu 1920, 1440, 1024, 768 và 390 px. Trên mobile dùng drawer cho phân hệ, luôn nhận biết công ty; vùng bảng cuộn riêng. Giữ tương tác bàn phím, zoom 200%, tương phản và nhãn form.
6. Nhớ bộ lọc/dòng chọn khi trở về từ hồ sơ. Thay đổi công ty phải tải trạng thái phù hợp công ty mới.
7. Kiểm toàn bộ nhóm màn Login/Setup, Dashboard/Worklist, Bán/Mua, Tiền/Công nợ, Kho/Sản xuất, Tài sản/Lương, Thuế, Chứng từ, Báo cáo và Admin.

Mỗi màn bàn giao ảnh đầy dữ liệu, rỗng, tải, lỗi và bị hạn chế quyền. Kiểm ít nhất hai bộ dữ liệu ngắn/dài để đánh giá cắt chữ và cuộn.

### 7. Lưu trữ, sao lưu và khôi phục
1. Tạo inventory nơi lưu DB, attachments, secret.key, bản sao lưu, cấu hình và dữ liệu khôi phục. Xác nhận đường dẫn hiệu lực trong SQLite và PostgreSQL.
2. Kiểm DataRoot và tùy chọn ghi đè từng đường dẫn. Đổi thư mục phải kiểm tra app đang chạy, ổ đích ghi được/đủ dung lượng, thư mục lồng nhau, reparse point và dữ liệu đích đã tồn tại.
3. Chép dữ liệu sang thư mục mới có kiểm chứng hash/manifest, chỉ cập nhật con trỏ nơi lưu khi hoàn tất, có thể quay lại nơi cũ. Khởi động lại đối chiếu DB, attachments, khóa và báo cáo.
4. Sao lưu gồm snapshot DB nhất quán, attachments và metadata phiên bản. Lưu khóa khôi phục theo phương án được bảo vệ và có kiểm soát truy cập, hỗ trợ máy mới giải mã những bí mật cần sử dụng lại.
5. Worker SQLite cần theo dõi lần tạo, lần verify, lần chép ngoài máy và lần diễn tập khôi phục. Bản chép ngoài máy kiểm hash và khả năng đọc. Giữ đủ bản tốt trước khi dọn.
6. PostgreSQL có lịch pg_dump/backup phù hợp, kèm files/config cần thiết và diễn tập pg_restore vào DB tách biệt. Kiểm khả năng khôi phục tại thời điểm theo nhu cầu thực tế.
7. RPO/RTO là tham số nghiệm thu do doanh nghiệp lựa chọn. Đề xuất ban đầu RPO 24 giờ, RTO 4 giờ cho triển khai nhỏ; đo khả năng đạt bằng dữ liệu gần quy mô vận hành.
8. Diễn tập khôi phục trên máy/thư mục mới, đối chiếu số dư, công nợ, tồn kho, allocation, audit, attachments và khóa mã hóa. Ghi thời gian, kết quả và người xác nhận.
9. Kiểm gỡ/cài lại/nâng cấp, ổ đầy, mất mạng/USB, sai mật khẩu, bản sao bị sửa cùng kích thước và dừng máy giữa lúc chép.
10. Chế độ Solo có wizard dễ dùng để chọn lịch/nơi chép và hướng dẫn giữ khóa. Quyền khôi phục toàn hệ thống vẫn gắn với người quản trị thích hợp.

### 8. Các hạng mục ERP bổ sung
- Hiệu suất và quy mô. Lập benchmark 10k/100k chứng từ và nhiều người dùng với phần cứng nêu rõ. Đo p50/p95, số SQL, bộ nhớ, phân trang và xuất báo cáo. Ưu tiên N+1, tổng hợp client quá lớn, giao dịch dài và thiếu index có số đo.
- Quan sát vận hành. Gắn correlation ID cho nghiệp vụ xuyên API/provider/background job. Có hàng chờ tác vụ, lỗi có thể thử lại, lịch sử và cách phục hồi theo quyền.
- Đóng kỳ. Worklist đối chiếu kho–sổ cái, công nợ, ngân hàng, VAT, khấu hao, giá thành, khoản chờ và sai lệch trước khi khóa.
- Import/migration. Preview, mapping, ID nguồn, cân số đầu/cuối, kiểm trường bắt buộc, undo lô nháp và đối chiếu sau ghi.
- Danh mục dùng chung. Quy tắc mã, trùng MST/đơn vị/kho, UoM, đối tượng vừa khách vừa NCC, hiệu lực ngừng sử dụng và hợp nhất danh mục có lịch sử.
- Bảo mật. Quản lý khóa, HTTPS LAN, cập nhật thư viện, kiểm file upload/export, log bí mật, phân quyền theo dữ liệu, thời hạn phiên và quy trình phục hồi quản trị.
- Phát hành. Gói cài có phiên bản và checksum, bản sao trước migration, compatibility với DB, phương án rollback phù hợp lịch sử đã phát sinh, hướng dẫn cho quản trị doanh nghiệp.
- Đặc thù hàng dăm. MT, BDMT, độ khô, chứng thư và giá vốn theo chính sách đã xác nhận. Test sai lệch chứng thư nhập/xuất, hoàn trả và làm tròn.

Tại đợt 18/11, npm audit báo GHSA-fc5q-39xc-vjpf/CVE-2026-112366 ở picomatch trong chuỗi Vite. Advisory đang ghi chưa có bản vá phát hành; npm báo 5 cảnh báo liên quan cùng chuỗi. Theo dõi nguồn gốc, kiểm nơi tiếp nhận glob và thay bản vá đã xác minh khi có. Giữ kết quả audit trong báo cáo phát hành cùng phạm vi ảnh hưởng dev tooling. [GitHub Advisory](https://github.com/advisories/GHSA-fc5q-39xc-vjpf)

## Thứ tự thực hiện và đầu ra
| Đợt | Công việc | Đầu ra nghiệm thu |
|---|---|---|
| P0 | Tính đúng số liệu, chống trùng, phiên/quyền, khôi phục dữ liệu, dependency security | Ca lỗi tái hiện, sửa nguồn, test SQLite/PG, biên bản restore, danh sách rủi ro còn mở |
| P1 | Chuẩn hóa chu trình và Solo/Team; pilot 3 màn | Bản đồ nguồn, ma trận quyền, ảnh trước/sau, E2E theo vai trò |
| P1 | Registry mẫu và đối chiếu pháp lý | Mẫu có phiên bản, mapping điều khoản, test công thức và bản in đã duyệt |
| P2 | Nhân thiết kế sang các phân hệ; đo hiệu suất | Bộ thành phần dùng chung, checklist từng màn, benchmark trước/sau |
| P2 | Giám sát, import, phát hành, tích hợp nâng cao | Runbook, migration drill, cảnh báo/tác vụ có thể phục hồi, kế hoạch tương thích |

Mỗi PR báo rõ yêu cầu đã xử lý, giới hạn, test đã chạy, dữ liệu sử dụng, ảnh/PDF liên quan, ảnh hưởng migration và bước triển khai. Nghiệm thu tổng thể bằng một bộ dữ liệu xuyên chu trình với số liệu đầu/cuối đã biết, bao gồm Solo và Team, hai provider, lỗi mạng và khôi phục sau sự cố.
