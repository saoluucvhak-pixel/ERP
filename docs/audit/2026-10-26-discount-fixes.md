# CKTM, đối chiếu thuế và hóa đơn thay thế — 26/10/2026

Cơ sở: `main` tại `d8f097cd35a0c3f341c39fc0e62825f4b5c49eb7`. Chỉ thử trên dữ liệu giả và nhà cung cấp Simulated. Không phát hành hóa đơn thật, không sửa DB thật.

## Các lỗi đã sửa

1. **Kỳ thuế phụ thuộc lý do nghiệp vụ.** `InvoiceAdjustmentReason` phân biệt sửa sai hóa đơn, CKTM, trả hàng và thay đổi giá. Chỉ loại sửa sai có VAT và kỳ gốc đã nộp mới gắn cảnh báo kiểm tra khai bổ sung theo policy hiện có. CKTM theo thỏa thuận không còn bị gắn cờ này. Dữ liệu cũ chưa biết lý do có cảnh báo cần xác minh, không tự đoán là sửa sai. Màn lập điều chỉnh cho chọn lý do sửa sai/thay đổi giá.
2. **Dấu hóa đơn điều chỉnh.** Nhập mới mặc định `amountConvention=Signed`, giữ nguyên số âm/dương. Adapter dùng số dương phải khai rõ `MagnitudeWithNature` và `adjustmentNature=Decrease|Increase`. Đối chiếu không dùng `Abs` trên sổ để đoán dấu từ chứng từ đang đối chiếu. Chứng cứ nhập và XML gốc không bị sửa. Hóa đơn HAK cũ có thể dùng bản phát hành bất biến để xác minh bản chất; số dương cũ không có bằng chứng thì báo cần kiểm tra, không coi là đã khớp.
3. **Bảng kê và Xem tính sau quyết toán dùng snapshot.** Lưu số từng hóa đơn/dòng, thuế suất, tỷ giá, số lẻ tiền tệ và chính sách/bậc tại lúc quyết toán cho cả hai chế độ. Nhập thêm hóa đơn kỳ cũ không đổi số đã quyết toán. Snapshot, tổng quyết toán và phân bổ hóa đơn được bảo vệ bằng interceptor/trigger. Chỉ cập nhật tham chiếu số hóa đơn pháp lý khi nhà cung cấp cấp số; không thay số tiền lịch sử.
4. **Hóa đơn NCC giảm âm** tạo được nháp giảm, không thể đổi thành tăng chỉ bằng lựa chọn trên form. Chứng từ có nhiều thuế suất cần phân bổ theo dòng chứng cứ, không tự chia tổng trên các thuế suất khác nhau.
5. **Kiểm tra trước phát hành thay thế.** Kiểm kỳ mở, quyền đảo, trạng thái gốc, điều chỉnh phụ thuộc, lệnh sản xuất đã chốt, khớp sao kê và khả năng chuyển phân bổ. Không gửi yêu cầu phát hành đầu tiên khi biết chắc bị chặn. Sau yêu cầu đầu tiên, nếu có race thì vẫn giữ cùng khóa và trạng thái `Issued/PostError` để xử lý tiếp; không giả rollback hóa đơn ngoài ERP.
6. **Thay thế đã thu tiền VND.** Chụp các phân bổ đang có, đảo gốc và ghi mới rồi chuyển phần phân bổ phù hợp trong cùng transaction. Ví dụ gốc 1.100.000 đã thu đủ, thay bằng 990.000: hóa đơn mới hết nợ, tiền thừa 110.000 còn chưa phân bổ; không tạo lại phiếu thu, không tăng thêm biến động kho. Thay đổi đối tượng/tiền tệ/tỷ giá khi có phân bổ bị chặn để xử lý hồ sơ trước. Ngoại tệ có phân bổ cần xử lý phân bổ và tỷ giá trước, chưa hỗ trợ chuyển tự động.
7. **Windows test runner:** dùng `fileURLToPath` khi quét tên module, tránh đường dẫn `/C:/...` sai.

## Dữ liệu cũ và triển khai

- Có migration `Phase26DiscountEvidence` cho SQLite và PostgreSQL. Các cột bổ sung có giá trị cũ Unspecified/Unknown hoặc null; không viết lại số tiền, chứng cứ hay sổ đã ghi.
- Quyết toán cũ thiếu snapshot: giữ tổng đã lưu, trả bảng kê chi tiết rỗng kèm cảnh báo, vô hiệu xuất bảng kê đó. Không dùng doanh số hiện tại để dựng lịch sử. Cần đối chiếu hồ sơ gốc trước khi bổ sung dữ liệu có audit.
- Hóa đơn điều chỉnh nhập mới bằng số dương được hiểu là tăng khi dùng `Signed`. Nguồn cũ từng gửi số dương cho giảm phải khai quy ước và bản chất rõ ràng theo schema đã cập nhật. Không được suy dấu theo hóa đơn ERP vì sẽ che giấu ghép nhầm tăng/giảm.
- Sao lưu trước khi triển khai migration vào dữ liệu sử dụng. Đợt này không chạy migration vào DB sử dụng của doanh nghiệp.

## Giới hạn còn mở — không coi đã hoàn thiện

- Quyết toán bằng chứng từ vẫn chỉ là xử lý kế toán, chưa tự hoàn tất hồ sơ hóa đơn/VAT. UI đã nêu rõ. Chưa có chu trình nhận hóa đơn sau voucher và tự bù trừ khoản đã ghi nhận, kèm trạng thái kế toán/pháp lý/thuế độc lập.
- CKTM mua bằng voucher/trích trước hiện vẫn ghi vai trò giảm giá vốn; chưa phân bổ xuyên kho–giá vốn–sản xuất–tài sản như đường hóa đơn. Chưa đổi quyết định này một cách ngầm định; cần đối chiếu chính sách tài khoản và đối tượng chi phí trước khi mở rộng.
- Hoàn nhập trích trước khi quyết toán bằng nhiều hóa đơn vẫn là chứng từ riêng. Cần settlement group/hoàn nhập theo từng phần để kiểm soát ghi sổ một phần; không nên ghi hoàn nhập toàn bộ rồi để các hóa đơn điều chỉnh chưa xử lý.
- Chưa có run bổ sung/thu hồi CKTM; không mở lại snapshot cũ để đưa doanh số mới vào. Khi run đã ghi sổ cần nghiệp vụ bổ sung có lý do/audit riêng.
- Kỳ thuế chưa tách đầy đủ ngày nhận hóa đơn đầu vào khỏi ngày kế toán. Cảnh báo đối soát không phải thao tác tự lập/nộp tờ khai.
- Chưa kiểm định adapter hóa đơn thật, chữ ký số, sandbox nhà cung cấp hoặc mẫu bảng kê pháp lý chính thức.
- Không sửa lỗi hàng chờ retry sau logout trong đợt này; thuộc phạm vi bảo vệ phiên đăng nhập riêng.

## Căn cứ và kiểm chứng

Đã đọc Điều 10 của bản gốc TT91/2026 tải từ [Công báo Chính phủ](https://congbao.chinhphu.vn/van-ban/thong-tu-so-91-2026-tt-btc-470171.htm), đặc biệt khoản 5(b), 6(c), 6(d). CKTM theo doanh số và nghiệp vụ sửa sai hóa đơn không được gộp thành một quy tắc khai bổ sung kỳ gốc. Việc nộp tờ khai vẫn do người có trách nhiệm xác minh theo hồ sơ cụ thể.

Kiểm thử và giới hạn môi trường được ghi trong báo cáo bàn giao kèm kết quả TRX. Kiểm tra model/migration PostgreSQL không thay thế chạy test trên PostgreSQL thực.

Kiểm thử bản sửa: **531/531 backend** (126 domain + 405 tích hợp SQLite), **64/64 frontend**, **12/12 e2e**, build thành công, lint không lỗi (25 cảnh báo có sẵn). Model SQLite/PostgreSQL khớp migration; SQL PostgreSQL sinh thành công. Chưa chạy PostgreSQL thực hoặc provider hóa đơn thật.
