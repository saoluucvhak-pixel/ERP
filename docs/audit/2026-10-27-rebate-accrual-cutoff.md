# Mốc ngày điều chỉnh trong trích trước CKTM

Xuất phát từ main `df1ede0`: phần sửa cơ sở doanh số CKTM đã có trong main, với hai test `RebateBase29` kiểm tra tăng giá trước và sau quyết toán. Đợt này giữ các thay đổi đó và bổ sung kiểm soát ngày khi trích trước.

## Lỗi và kết quả sửa

`AccrueAsync(asOf)` lọc hóa đơn gốc đến ngày ước tính nhưng trước đây lấy cả các hóa đơn điều chỉnh có ngày hạch toán sau `asOf`. Do đó, với hóa đơn bán 100 triệu, CKTM 2%, trích đến ngày 20 có thể ra 2,4 triệu nếu đã nhập điều chỉnh tăng 20 triệu ngày 21, hoặc 1,6 triệu nếu là điều chỉnh giảm. Số đúng tại ngày 20 trong hai ca này là 2 triệu.

`PlanAsync` nay giới hạn `PostingDate` của các điều chỉnh khi nhận tham số `upTo`. Điều chỉnh đúng ngày được tính; sau ngày đó chưa được tính. Lần trích tiếp theo tính phần tăng/hoàn nhập so với số đã trích trước. Preview/quyết toán không truyền `upTo` vẫn nhận các hồ sơ điều chỉnh đến muộn theo quy trình hiện hành. Không đổi điều khoản, snapshot, sổ đã ghi hoặc phân loại thu hồi CKTM.

Không cần migration. Không tự tính lại hoặc sửa các chứng từ trích trước cũ. Đợt này không xây dựng cơ chế phục dựng toàn bộ trạng thái lịch sử của chứng từ đã đảo; trạng thái nguồn hiện hành và các chặn hiện có tiếp tục áp dụng.

## Đối chiếu chứng từ cũ

Rà các lần trích trước và hóa đơn điều chỉnh gắn nguồn của thỏa thuận có ngày hạch toán sau ngày ước tính. Đối chiếu số ước tính với hồ sơ nguồn tại ngày đó, kể cả bậc doanh số và các điều chỉnh đã đảo; không lấy số quyền lợi hiện tại thay cho số lịch sử. Khi chưa quyết toán, lần trích tiếp theo vẫn lấy quyền lợi ước tính mới trừ số trích đã ghi sổ. Nếu kỳ cũ đã khóa hoặc thỏa thuận đã quyết toán, cần kế toán xem hồ sơ và dùng quy trình điều chỉnh được duyệt; không cập nhật thẳng dữ liệu/snapshot để ép số khớp.

## Kiểm chứng

Thêm bốn ca `RebateCutoff30`: tăng/giảm giá, đúng ngày 20 hoặc ngày 21. Ca ngày 21 còn kiểm tra lần trích cuối tháng ghi đúng phần chênh; mọi ca kiểm tra preview cuối kỳ vẫn tính điều chỉnh. Trước sửa: hai ca ngày 21 thất bại; hai ca đúng ngày và hai ca `RebateBase29` đạt (4/6).

Sau sửa: **6/6 ca trọng tâm** đạt; toàn bộ **600/600 backend** (126 domain + 474 tích hợp SQLite), **77/77 frontend**, **17/17 E2E**, web build đạt, lint 0 lỗi/25 cảnh báo có sẵn. E2E dùng cùng API binary với suite tích hợp. Bộ domain được restore/chạy riêng trong artifacts cô lập; kiểm tra bằng TRX, không suy số test từ mã thoát đơn thuần. Dữ liệu thử độc lập; không chạy trên DB doanh nghiệp hoặc nhà cung cấp hóa đơn thật. Chưa xác nhận PostgreSQL chạy thật trong đợt này.
