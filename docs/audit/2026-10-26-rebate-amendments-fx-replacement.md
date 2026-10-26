# CKTM bổ sung/thu hồi và chuyển phân bổ ngoại tệ

Đợt triển khai tiếp sau sửa retry trong PR #66. Mọi số tiền dùng decimal, theo vai trò tài khoản do công ty ánh xạ. Không cập nhật snapshot hoặc các dòng đã ghi sổ của lần quyết toán trước.

## CKTM bổ sung / thu hồi

Mở **Bán hàng / Mua hàng → CK thương mại → Bổ sung / Thu hồi** tại thỏa thuận đã quyết toán. Màn hình có lịch sử từng lần, quyền lợi lũy kế đã lập (gồm nháp), bảng tính chênh lệch và nút mở tiến độ/bảng kê của từng lần. Người lập tạo hóa đơn nháp; người có quyền ghi sổ kiểm tra/phát hành/ghi sổ theo phân nhiệm hiện hành. Không tự ghi sổ khi bấm lập bổ sung.

Mỗi run mới lưu `ParentRunId`, số thứ tự và lý do, nối với lần còn hiệu lực trước đó. Snapshot phiên bản 3 lưu đồng thời các dòng chênh lệch và quyền lợi lũy kế làm mốc cho lần sau. Run gốc vẫn giữ nguyên. Chỉ có một gốc hiệu lực và một con hiệu lực cho mỗi cha; writer lock, row version và hash nguồn ngăn hai người quyết toán trùng hoặc dùng preview đã cũ.

Số hiện tại được tính theo **điều khoản đã chốt**, trên nguồn đủ điều kiện hiện tại của kỳ thỏa thuận; loại các hóa đơn điều chỉnh thuộc chính chuỗi và các hóa đơn liên kết với voucher để không trừ CKTM của chính nó vào doanh số lần nữa. Đối chiếu quyền lợi mới với quyền lợi lũy kế đã chốt theo từng hóa đơn/dòng/thuế suất/loại tiền.

- Dương: tạo hóa đơn CKTM điều chỉnh giảm bổ sung.
- Âm: tạo hóa đơn tăng để thu hồi CKTM. Chiều bán ghi Có tài khoản CKTM đã xác định từ lần gốc, không ghi vào doanh thu bán hàng thông thường. Chiều mua điều chỉnh tăng lại tồn kho/giá vốn qua cơ chế phân bổ theo nguồn.
- Tổng chênh lệch bằng 0 nhưng các nguồn khác nhau: vẫn tạo các phần tăng/giảm riêng, vì thuế suất và công nợ theo hóa đơn có thể khác nhau. Lifecycle xét tổng trị tuyệt đối và trạng thái từng hóa đơn, không báo đã ghi sổ chỉ vì tổng bằng 0.
- Không có chênh lệch từng dòng: không tạo chứng từ.

Ví dụ: 100 triệu × 2% đã chốt 2 triệu, thêm 20 triệu cùng bậc thì chỉ lập 400 nghìn. Nếu điều khoản hồi tố có bậc 5% từ 110 triệu, quyền lợi mới 6 triệu, bổ sung 4 triệu. Nếu cùng điều khoản là lũy tiến, quyền lợi mới 2,7 triệu, chỉ bổ sung 700 nghìn.

Phải hoàn tất ghi sổ toàn bộ hóa đơn của các lần trước, kể cả các hóa đơn liên kết với voucher, trước khi lập lần tiếp theo. Không tiêu thụ khoản trích trước lần hai. Tài khoản CKTM gốc được giữ cho phần bổ sung/thu hồi; dữ liệu cũ thiếu snapshot hoặc không xác định được tài khoản gốc phải đối chiếu riêng.

Khi một lần có cả thu hồi và bổ sung, ghi sổ thu hồi trước. Khi đảo, đảo phần bổ sung trước thu hồi. Không đảo chứng từ nguồn khi còn run phía sau phụ thuộc. Hủy chỉ nhắm vào run được chọn (hoặc run hiệu lực mới nhất qua nút ngoài danh sách), hủy nháp chưa phát hành; đã ghi sổ phải đảo trước. Lịch sử/snapshot vẫn giữ. Ngày lập/ghi sổ lần mới không được trước lần đã ghi nhận hoặc ngày đảo trong lịch sử, kể cả run đã hủy, để tránh ghi quyền lợi hai lần ở khoảng trước ngày đảo.

Hóa đơn thu hồi vẫn mang lý do `CommercialRebate`, không tự chuyển thành `InvoiceError`. Trạng thái kế toán, chứng cứ hóa đơn, ngày nhận NCC và thuế tiếp tục tách biệt. `ReadyForReview` không xác nhận đã lập/nộp tờ khai. Bảng kê run mới chỉ gồm nguồn có chênh lệch; doanh số của một nguồn ghi một lần bằng tiền hạch toán, khác số CKTM và khác nguyên tệ. Dấu CKTM/thuế phân bổ trên bảng kê là hướng bổ sung/thu hồi, không phải dấu chỉ tiêu kê khai.

Báo cáo CKTM trừ thu hồi khỏi CKTM sau bán. Hóa đơn pháp lý đã liên kết với voucher không cộng lại khoản CKTM mà voucher đã ghi nhận, kể cả khi hai chứng từ khác kỳ.

## Chuyển phân bổ ngoại tệ khi thay thế

Ở hóa đơn thay thế nháp có nút **Phân bổ thay thế**: xem nguyên tệ chuyển, giá trị ghi sổ chuyển, nguyên tệ/giá trị thừa chưa phân bổ. API `GET /invoices/{id}/replacement-allocations` chỉ đọc; cùng kế hoạch được kiểm tra lại trước lần gửi NCC đầu tiên và lúc ghi sổ.

Hỗ trợ hóa đơn mua/bán, thanh toán một phần/toàn bộ/nhiều lần, cùng đối tượng/tài khoản/tiền tệ/tỷ giá, khi giá trị ghi sổ hai phía khớp phần chuyển. Tỷ giá dòng tiền có thể khác tỷ giá hóa đơn: giữ nguyên lãi/lỗ đã thực hiện ở phiếu thu/chi. VAT VND là dòng riêng, không cộng vào nguyên tệ.

Ví dụ 1.000 USD có giá trị ghi sổ 25 triệu, đã thu 400 USD ở tỷ giá thu tiền 25.200: thay bằng hóa đơn 900 USD sẽ chuyển phân bổ 400 USD/10 triệu, còn nợ 500 USD/12,5 triệu và khoản VAT riêng. Phiếu thu và lãi tỷ giá 80 nghìn không được tạo lại. Nếu đã thu đủ 1.000 USD thì phần 100 USD/2,5 triệu thừa để chưa phân bổ, không chuyển thành một phiếu tiền mới.

Kế hoạch dự tính số mở sau giải phóng phân bổ gốc, giới hạn đồng thời nguyên tệ và tiền hạch toán, so sánh BookValue của khoản chuyển với cả hai phía. Các phân bổ mới ghi audit tham chiếu phân bổ cũ/mới; phân bổ cũ được hủy theo cơ chế lịch sử, không sửa số tiền. Đảo gốc → ghi hóa đơn thay thế → tái phân bổ nằm trong một giao dịch. Lỗi phân bổ cuối rollback toàn bộ phần kế toán. Kết quả đã phát hành bên ngoài vẫn được giữ; lỗi cơ sở dữ liệu khi ghi sổ hiện `PostError` để thử ghi sổ lại trên cùng hóa đơn đã phát hành.

## Ranh giới có kiểm soát

- Không sửa điều khoản/bậc/kỳ của thỏa thuận đã chốt. Muốn thay điều khoản cần hồ sơ/thỏa thuận mới có phê duyệt; lần bổ sung chỉ tính chênh lệch dữ liệu theo chính sách cũ.
- CKTM mua đã đi vào sản xuất dở dang/giá thành hoặc vốn hóa tài sản/CCDC/sửa chữa tài sản bị chặn khi chưa có quy trình phân bổ riêng, cả lúc tính và lúc phát hành/ghi sổ. Không tự dồn các khoản này vào giá vốn.
- Chênh lệch CKTM ngoại tệ không biểu diễn được chính xác theo tiền hóa đơn và làm tròn quy định bị chặn để đối chiếu.
- Công nợ đã đánh giá lại, đổi đối tượng/tài khoản/loại tiền/tỷ giá, hoặc phần chuyển không khớp giá trị ghi sổ hai phía không được tự chuyển. Kỳ tái phân bổ phải mở; các chặn phụ thuộc/đối soát trước phát hành vẫn áp dụng.
- Nếu phiếu tiền đã có chiết khấu thanh toán: chỉ chuyển khi cơ sở tiền hàng/thuế và điều khoản không đổi. Thay trị giá phải đối chiếu lại quyền lợi; không biến CK thanh toán thừa thành ứng trước hoặc tự ghi thêm FX. Thay đổi điều khoản/ngày gốc ảnh hưởng chiết khấu cũng bị chặn.
- Chưa hỗ trợ hóa đơn điều chỉnh gộp nhiều nguồn trong một hóa đơn, số NCC không liên tiếp của một lô tự sinh, đính chính trực tiếp chứng cứ ngày nhận, hoặc tự sửa dữ liệu cũ thiếu snapshot. Không tự điền số pháp lý từ nhà cung cấp thật.
- Chưa xác nhận bằng PostgreSQL chạy thật, dữ liệu doanh nghiệp, chữ ký số hoặc NCC HĐĐT thật. Kiểm thử NCC sử dụng mô phỏng và tiền dữ liệu giả; không khẳng định mọi hồ sơ hóa đơn/kê khai đã hợp pháp chỉ vì test đạt.

## Migration và kiểm chứng

`Phase28RebateAmendments` có cho SQLite và PostgreSQL, thêm quan hệ cha/con và chỉ mục chuỗi; giữ dữ liệu cũ làm run gốc. SQLite dùng ADD COLUMN có self-reference để giữ trigger, đóng băng thêm ba trường mới; PostgreSQL dùng FK và trigger kiểm tra chuỗi. Cả hai chặn hủy cha đang có con hiệu lực. Hạ phiên bản chỉ khi chưa có lịch sử bổ sung/thu hồi; khi đã có lịch sử thì từ chối, không xóa parentage để ép chạy bản cũ.

20 ca nghiệp vụ mới kiểm tra hồi tố/lũy tiến, thu hồi/hạ bậc, nhiều thuế suất và tổng chênh bằng 0, nguồn cũ bất biến, sai quyền/kỳ khóa/hash cũ, ngoại tệ nhiều tỷ giá nguồn, gốc bằng voucher, không tiêu thụ trích trước lần hai, WIP, ngày trước nguồn/ngày đảo, thu/chi FX nhiều lần và giá thay thế lớn/nhỏ, đánh giá lại, CK thanh toán và lỗi phân bổ cuối/phục hồi không phát hành trùng. Hai E2E mới dùng trình duyệt/API SQLite tạm để lập bổ sung và xem kế hoạch FX. Số kiểm thử toàn bộ cuối cùng được ghi trong STATUS.md và báo cáo bàn giao.

Để chạy E2E bằng cùng bộ binary đã kiểm tra backend, đặt `E2E_ARTIFACTS_PATH` tới thư mục truyền cho `dotnet test --artifacts-path` (đặt trong `artifacts/` của repo để fixture tìm được tài liệu/schema). Cờ này tùy chọn; quy trình mặc định không đổi.

Kiểm chứng bản cuối: **561/561 backend** (126 domain + 435 SQLite), **77/77 frontend**, **17/17 E2E**, build đạt, lint 0 lỗi/25 cảnh báo có sẵn. Bộ 20 ca nghiệp vụ mới và toàn bộ suite chạy bằng artifacts/phase28-release; E2E dùng chính API binary đó. Model SQLite/PG không có thay đổi chưa migration, SQL PostgreSQL sinh thành công; chưa chạy PG/NCC thật.
