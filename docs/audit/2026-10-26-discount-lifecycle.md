# Hoàn thiện chu trình CKTM — 26/10/2026

Tiếp nối PR #65 trên commit `081678efc463bde6419af8132616aa683919e9c9`. Phạm vi đợt này là quyết toán từng phần, hóa đơn đến sau chứng từ, công nợ nguồn và kỳ nhận hóa đơn CKTM đầu vào. Không thực hiện trên dữ liệu doanh nghiệp hay phát hành hóa đơn thật.

## Quyết toán hóa đơn và trích trước

- Quyết toán mới dùng `AccrualSettlementMode.PerInvoice`. Tổng trích trước được chia theo số CKTM từng hóa đơn (phần cuối nhận dư làm tròn), lưu bất biến tại `DiscountAgreementRunInvoice.AccruedAmount`.
- Khi ghi sổ hóa đơn nào, bù trừ phần trích trước của hóa đơn đó trong **chính voucher hóa đơn**, cùng transaction. Không tạo chứng từ hoàn nhập toàn bộ độc lập cho run mới.
- Ví dụ đã trích 60.000, quyết toán thực 40.000 chia hai hóa đơn bằng nhau: hóa đơn đầu ghi CKTM 20.000 và hoàn nhập 30.000 trích trước; số trích trước còn 30.000. Sau hóa đơn thứ hai trích trước hết, CKTM thực cuối cùng 40.000.
- Đảo một hóa đơn khôi phục đúng phần trích trước tương ứng. Chặn đảo nguồn trích trước khi còn chứng từ/hóa đơn quyết toán đã ghi sổ.
- Tài khoản nguồn được giữ tại run; nếu tài khoản đã trích khác ánh xạ khi quyết toán thì dừng để đối chiếu, không tự xóa số dư ở tài khoản khác.

## Chứng từ đã ghi nhận rồi mới có hóa đơn

1. Lập và duyệt/ghi sổ chứng từ quyết toán như trước. Chứng từ mới phân bổ vào khoản công nợ gốc theo từng hóa đơn; phần vượt số còn mở giữ là khoản chưa phân bổ, không tự thu/chi tiền.
2. Tạo hoặc nhận hóa đơn CKTM nháp đúng đối tượng, hóa đơn nguồn và thuế suất.
3. Tại **Tiến độ / Hóa đơn**, liên kết trước khi gửi phát hành hoặc ghi sổ. Phần nháp đã liên kết cũng giữ hạn mức nguồn; không được liên kết vượt tiền/dòng của snapshot, liên kết hai lần hoặc liên kết sau khi đã gửi phát hành.
4. Ghi sổ hóa đơn theo quy trình duyệt hiện có. Khoản CKTM đã hạch toán không ghi giảm lần nữa. VAT và chứng cứ pháp lý được theo dõi riêng.

Bán: chứng từ đã ghi Nợ CKTM / Có phải thu 20.000. Hóa đơn đến sau có CKTM 20.000 và VAT 2.000 chỉ thêm Nợ VAT đầu ra / Có phải thu 2.000; CKTM vẫn 20.000. Với hóa đơn không VAT, voucher đối chiếu cân bằng trên cùng tài khoản, không đổi số dư/công nợ.

Mua: chứng từ đã Nợ phải trả / Có tài khoản CKTM mua 100.000. Khi nhận hóa đơn giảm 100.000 + VAT 10.000, hệ thống bù trừ 100.000 đã ghi và chuyển về đúng tồn kho/giá vốn theo phân bổ của đường hóa đơn; phải trả chỉ giảm thêm phần VAT 10.000. Ví dụ còn 60% hàng: giảm tồn kho 60.000, giảm giá vốn 40.000, tài khoản CKTM mua đã ghi được bù trừ hết. Sổ kho đối chiếu với sổ cái.

- Chặn ghi sổ hóa đơn CKTM độc lập trên nguồn đã có quyết toán bằng voucher nếu chưa xác định liên kết; khoản CKTM khác cần quyết toán theo thỏa thuận riêng.
- Chặn đảo chứng từ nguồn khi hóa đơn liên kết đã ghi sổ hoặc đang chờ kết quả phát hành. Không cho hủy nháp ERP khi hóa đơn đã gửi phát hành, trừ trường hợp nhà cung cấp đã từ chối; kết quả chưa xác định phải đối soát/retry cùng hóa đơn. Hủy run sau khi đảo voucher nguồn sẽ hủy cả hóa đơn liên kết còn nháp chưa phát hành; liên kết gốc vẫn giữ làm chứng cứ.
- Bút toán nháp được đối chiếu với snapshot tài khoản, số tiền, ngày và khoản công nợ được phân bổ ở các bước duyệt/ghi sổ; sửa cân cả hai bên cũng bị phát hiện.

## Trạng thái và ngày nhận

`LifecycleAsync` tách kế toán, hóa đơn pháp lý, thuế và tình trạng phân bổ mua. Ghi sổ tay không tự xác nhận đã có chứng cứ HĐĐT. Chứng cứ nhập được ghép theo người bán, ký hiệu/số, tham chiếu gốc, số có dấu và trạng thái mới nhất; chứng từ HAK đã phát hành cũng được xét. `ReadyForReview` chỉ có nghĩa đủ điều kiện đối chiếu, không có nghĩa đã lập/nộp tờ khai.

Hóa đơn mua có ngày nhận được lưu cùng liên kết hoặc qua nút **Lưu ngày nhận** ở run theo hóa đơn. Ví dụ hóa đơn CKTM lập/ghi sổ tháng 4, nhận tháng 5: bảng kê và đối chiếu VAT CKTM đầu vào xét tháng 5; ngày hạch toán trên sổ không đổi. Thông tin ngày nhận xuất được trong bảng kê. Quy tắc này giới hạn ở hóa đơn CKTM có chứng cứ ngày nhận trong chu trình, không tự đoán ngày nhận cho mọi hóa đơn đầu vào.

Căn cứ kỳ nhận CKTM: Điều 10(5), (6)(d) của [TT91/2026 trên Công báo](https://congbao.chinhphu.vn/van-ban/thong-tu-so-91-2026-tt-btc-470171.htm), đã đọc bản gốc. Kế toán vẫn phải xác minh hồ sơ kê khai cụ thể.

## Migration và dữ liệu cũ

- `Phase27DiscountLifecycle` đủ SQLite/PostgreSQL, thêm phân bổ trích trước, tài khoản đã lưu và bảng liên kết bất biến. Không đổi số đã ghi sổ.
- SQLite bảo vệ cả trường tài khoản/mode mới trong run. Đường hạ phiên bản dùng DDL phù hợp với SQLite đi kèm, tháo/khôi phục trigger đúng thứ tự; đã bổ sung kiểm tra hạ/nâng và giữ dữ liệu bằng test có sẵn.
- Run cũ dùng hoàn nhập độc lập giữ cơ chế cũ và có cảnh báo đối chiếu. Không tự viết lại lịch sử thành PerInvoice.
- Nháp cũ thiếu snapshot bút toán cần hủy/lập lại sau đối chiếu. Run đã ghi sổ nhưng thiếu snapshot nguồn/tài khoản không được tự suy để liên kết bù trừ; cần xử lý hồ sơ cũ có kiểm soát.

## Các phần chưa hoàn tất

- CKTM mua trước hóa đơn vẫn là ghi nhận tạm ở tài khoản vai trò đã chọn; phân bổ kho/giá vốn hoàn tất khi hóa đơn được ghi sổ. Chưa phân bổ tự động xuyên sản xuất dở dang, giá thành đã chốt hoặc tài sản.
- Đợt Phase28 đã bổ sung run bổ sung/thu hồi có lịch sử, không sửa snapshot cũ. Xem `2026-10-26-rebate-amendments-fx-replacement.md`.
- Chưa có giao diện đính chính ngày nhận đã lưu bằng chứng cứ bổ sung; không sửa/xóa trực tiếp dòng chứng cứ.
- Phase28 đã hỗ trợ chuyển phân bổ ngoại tệ khi cùng tiền tệ/tỷ giá và khớp giá trị ghi sổ; trường hợp đánh giá lại hoặc đổi cơ sở CK thanh toán vẫn chặn. Chưa có hóa đơn điều chỉnh gộp nhiều nguồn, hoặc nhập danh sách số hóa đơn NCC thực tế thay cho chuỗi số đầu tiên của nhiều hóa đơn.
- Chưa kiểm thử PostgreSQL thực, adapter/chữ ký số thật hoặc mẫu bảng kê pháp lý chính thức. Kiểm tra model và sinh SQL không thay thế các bước này.

Kiểm chứng đợt hoàn thiện: **541/541 backend** (126 domain + 415 SQLite, gồm 10 ca nghiệp vụ mới), **64/64 frontend**, **13/13 e2e**, build đạt, lint 0 lỗi/25 cảnh báo có sẵn. Test hạ/nâng phiên bản giữ dữ liệu đạt; model SQLite/PG khớp; SQL PostgreSQL sinh thành công. Chưa chạy PostgreSQL thực hoặc NCC thật.
