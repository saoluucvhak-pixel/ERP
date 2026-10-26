# Prompt triển khai các phần nghiệp vụ tiếp theo

Phạm vi lõi của hai mục đã được triển khai ở đợt Phase28; xem `2026-10-26-rebate-amendments-fx-replacement.md` để biết hành vi thực tế, kiểm thử và các trường hợp nâng cao còn chặn. Nội dung bên dưới được giữ làm tiêu chí thiết kế/nghiệm thu, không có nghĩa mọi trường hợp nâng cao đã hỗ trợ. Giữ nguyên snapshot/lịch sử đã ghi sổ; không gỡ chặn nghiệp vụ chỉ để làm thao tác chạy qua.

## 1. Quyết toán CKTM bổ sung / thu hồi

> Đọc main mới nhất, CLAUDE.md, STATUS.md và các phần DiscountAgreementService, lifecycle, InvoiceService.Adjustments, VatService, AccountingEngine, snapshot và trigger bất biến. Triển khai quyết toán bổ sung/thu hồi theo chuỗi lần quyết toán có tham chiếu lần gốc, có phiên bản và phê duyệt. Không cập nhật số tiền/snapshot của lần đã quyết toán, không bắt người dùng đảo một hóa đơn pháp lý đã phát hành chỉ để tính doanh số mới.
>
> Trước khi ghi dữ liệu, bổ sung preview chỉ đọc: nguồn doanh số hiện tại, nguồn đã chốt, doanh số tăng/giảm, bậc trước/sau, quyền lợi CKTM lũy kế theo điều khoản đã lưu, số đã quyết toán, phần chênh lệch theo từng hóa đơn/dòng/thuế suất/tiền tệ. Phân biệt doanh số, giới hạn còn được điều chỉnh và số CKTM đã ghi nhận; không trừ CKTM của chính chuỗi quyết toán vào doanh số lần nữa. Không cộng trùng khi một nguồn có cả hóa đơn ERP và chứng cứ HĐĐT.
>
> Khóa chuỗi theo công ty/thỏa thuận và số phiên bản. Bấm đúp, retry, hai người cùng chạy hoặc thay đổi dữ liệu giữa preview và xác nhận không được tạo hai lần quyết toán. Đối chiếu lại hash/phiên bản nguồn trong transaction trước khi tạo. Thêm migration SQLite/PostgreSQL, chuyển đổi decimal SQLite và trigger bảo vệ các trường nguồn mới; kiểm thử dữ liệu cũ và hạ/nâng phiên bản. Không chỉ bỏ unique index đang giới hạn một run hiệu lực: phải sửa mọi truy vấn Single/ToDictionary, Statement, Preview, Lifecycle, void/reverse và quota để hiểu chuỗi nhiều run.
>
> Phần chênh lệch bằng 0 không tạo chứng từ. Phần dương tạo điều chỉnh giảm CKTM bổ sung. Phần âm cần luồng thu hồi có tham chiếu chứng từ/quyền lợi CKTM đã cấp, chứng minh số thu hồi chưa vượt số còn hiệu lực; không mặc định hạch toán thành doanh thu bán hàng thông thường. Kiểm tra luồng Increase đang dùng tài khoản nào và bổ sung quy tắc thu hồi tương ứng thay vì dùng sai tài khoản. Giữ lý do CommercialRebate, không đổi sang InvoiceError để ép đi vào khai bổ sung.
>
> Chốt riêng quy tắc chứng cứ pháp lý và ngày nhận trước khi đưa một loại thu hồi vào trạng thái được kê khai. Không tự suy số âm/dương từ bút toán nếu dữ liệu hóa đơn chưa xác định được quy ước dấu. Chưa đủ chứng cứ thì hiện chờ đối chiếu, không hiện đã hoàn tất thuế. Không sửa kỳ VAT đã lưu bằng cập nhật trực tiếp chứng cứ cũ.
>
> Hạch toán số tăng/giảm trong kỳ mở; phần trích trước và phần đã ghi nhận phải đối trừ đúng một lần. Bên mua phân bổ tồn kho/giá vốn theo nguồn; nguồn liên quan sản xuất dở dang, giá thành đã chốt hay tài sản chưa hỗ trợ phải có trạng thái/chặn rõ, không tự dồn vào giá vốn. VND VAT và nguyên tệ phải tách đúng như hóa đơn nguồn.
>
> UI hiển thị dòng thời gian các run, số lũy kế, số chênh lệch, hướng bổ sung/thu hồi, lý do và trạng thái kế toán/hóa đơn/thuế/phân bổ. Có preview và duyệt trước ghi sổ. Hủy nháp chỉ tác động phần chưa phát hành; đảo chứng từ không xóa snapshot hoặc chứng cứ gốc.

Ca nghiệm thu tối thiểu:

1. Doanh số 100 triệu × 2% đã quyết toán 2 triệu, thêm 20 triệu cùng bậc: chỉ tạo thêm 400 nghìn.
2. Thêm doanh số vượt bậc hồi tố: chênh lệch tính trên toàn doanh số theo điều khoản; bậc lũy tiến tính từng phần, không dùng cùng một thuật toán cho hai loại.
3. Trả hàng làm hạ bậc: tính lại quyền lợi và phần phải thu hồi; tổng thu hồi không vượt CKTM còn hiệu lực. Không tạo doanh thu bán hàng giả.
4. Chênh lệch tổng bằng 0 nhưng phân bố giữa hóa đơn/thuế suất khác nhau: không được bỏ qua chênh lệch thuế theo nguồn; hiển thị và xử lý đối chiếu rõ.
5. Nhiều thuế suất, doanh số ngoài khoảng, nguồn nháp/hủy/đảo, hóa đơn thay thế và ngoại tệ làm tròn: không chồng nguồn, không sai dấu.
6. Hai yêu cầu cùng phiên bản, retry mất phản hồi, đảo một phần, run cũ thiếu snapshot, kỳ khóa, thiếu quyền, sai công ty: không ghi trùng/không sửa sổ cũ.
7. Ghi nhận CKTM trước khi có hóa đơn rồi nhận hóa đơn bổ sung: tổng chi phí/giảm doanh thu chỉ ghi một lần, VAT theo chứng cứ/ngày nhận, lifecycle không tự báo đã nộp tờ khai.

## 2. Chuyển phân bổ ngoại tệ khi thay thế hóa đơn

> Đọc InvoiceService.Replacement, FxPaymentService, FxSettlementMath, SettlementService.OpenBothAsync/AllocateCoreAsync và cơ chế đảo chứng từ. Hiện CaptureReplacementAllocationsAsync chủ động chặn mọi allocation có ForeignAmount. Không chỉ xóa điều kiện chặn hoặc thêm ForeignAmount vào record rồi sao chép máy móc.
>
> Trước tiên xây dựng kế hoạch chuyển phân bổ chỉ đọc: phân bổ cũ, khoản đối ứng, nguyên tệ và giá trị ghi sổ thực tế của từng phía, chênh lệch đã thực hiện, kỳ phân bổ, khoản VAT VND riêng, khoản đã đánh giá lại và ràng buộc đối soát ngân hàng. Phải preflight trước khi gửi phát hành hóa đơn thay thế; cùng một preflight phải được kiểm tra lại trong transaction lúc ghi sổ.
>
> Đợt đầu chỉ hỗ trợ trường hợp chứng minh được cùng đối tượng, tài khoản, chiều, loại tiền, tỷ giá/giá trị ghi sổ và nguồn phân bổ hợp lệ. Dùng OpenBothAsync và FxSettlementMath để giới hạn đồng thời nguyên tệ và tiền hạch toán; không dùng riêng min(OpenAmount). Tách phần công nợ nguyên tệ khỏi VAT VND. Ghi tham chiếu allocation cũ/mới để audit; giữ allocation cũ ở trạng thái đã hủy theo cơ chế hiện có.
>
> Không tạo lại dòng tiền. Không ghi lại lãi/lỗ tỷ giá đã được ghi nhận trong phiếu thu/chi gốc. Khi hóa đơn thay thế giảm giá trị, phần thừa phải thành khoản chưa phân bổ với nguyên tệ và giá trị ghi sổ đúng. Nếu số dư thừa mang tỷ giá không còn tương thích, phải có kế hoạch điều chỉnh chênh lệch theo nghiệp vụ được duyệt hoặc tiếp tục chặn rõ trước phát hành, không âm thầm lấy số VND nhỏ hơn rồi coi đã đối soát.
>
> Hóa đơn đã đánh giá lại, chiết khấu thanh toán, bù trừ nhiều nguồn, tỷ giá thay đổi, số lẻ không thể biểu diễn bằng ExactRate, kỳ khóa và chuỗi thay thế nhiều lần phải được phân loại và kiểm thử. Trường hợp chưa hỗ trợ giữ chặn với lý do cụ thể. Toàn bộ đảo gốc → ghi thay thế → tái phân bổ phải ACID; lỗi ở bước cuối không được để lại một nửa trạng thái trong DB. Kết quả NCC phát hành bên ngoài không rollback được nên cần đối soát/idempotency riêng.

Ca nghiệm thu tối thiểu:

1. Thu/chi một phần USD, tỷ giá thanh toán khác tỷ giá hóa đơn, thay thế cùng trị giá: số dư nguyên tệ và VND như trước, lãi/lỗ tỷ giá không tăng lần hai.
2. Thay thế lớn hơn/nhỏ hơn phần đã thanh toán: phần còn nợ/ứng trước đúng cả hai đơn vị; không tạo phiếu tiền mới.
3. Thu nhiều lần, trả nhiều nguồn, VAT VND và công nợ ngoại tệ: không gom nhầm dòng hoặc vượt số mở.
4. Tỷ giá/đối tượng/tài khoản đổi, kỳ phân bổ đã khóa, đã đánh giá lại/đối soát ngân hàng, thiếu quyền đảo: bị chặn trước gọi NCC nếu chưa có quy trình hỗ trợ.
5. Làm tròn một đơn vị tiền, ExactRate không biểu diễn được, thay thế lặp lại, lỗi ở lần phân bổ cuối và retry sau mất phản hồi: không mất phân bổ, không sai sổ, không nhân đôi chứng từ.

Chỉ báo hoàn tất từng mục sau khi có code, UI, test nghiệp vụ và bằng chứng chạy toàn bộ suite. PostgreSQL thực, nhà cung cấp HĐĐT thật và đối chiếu dữ liệu doanh nghiệp phải được ghi riêng nếu môi trường chưa có.
