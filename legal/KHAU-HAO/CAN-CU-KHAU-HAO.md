# Căn cứ pháp lý của khấu hao, phân bổ TSCĐ / CCDC trong phần mềm

Đối chiếu ngày 20/10/2026 bằng tìm kiếm trên mạng (không tải được văn bản gốc từ máy chủ này; các trang tổng hợp của thuvienphapluat.vn, luatvietnam.vn, MISA đều trích cùng nội dung). **Kế toán trưởng vẫn cần đối chiếu lần cuối với văn bản gốc trên Công báo / cổng pháp luật trước khi coi là đã xác minh.**

## Văn bản áp dụng
- **Thông tư 45/2013/TT-BTC** (hướng dẫn chế độ quản lý, sử dụng và trích khấu hao TSCĐ), sửa đổi bởi **Thông tư 147/2016/TT-BTC** và **Thông tư 30/2025/TT-BTC** (ký 30/5/2025, hiệu lực 15/7/2025).
- Thông tư 30/2025 chỉ bổ sung **khoản 12 Điều 9**: doanh nghiệp nhà nước 100% vốn thuộc Đề án theo Quyết định 1468/QĐ-TTg (ngành Công Thương) được giãn hoặc tạm hoãn trích khấu hao với TSCĐ hữu hình chưa đưa vào sử dụng, vẫn trong khung thời gian của Thông tư 45. **Không thay đổi** hệ số, phương pháp, nguyên tắc ngày/tháng. Phần mềm chưa có chức năng tạm hoãn khấu hao này (chỉ áp dụng cho nhóm doanh nghiệp đó).
- Chế độ kế toán: Thông tư 99/2025/TT-BTC (áp dụng từ 01/01/2026 theo quyết định của công ty) dẫn chiếu quy định pháp luật về quản lý, sử dụng, trích khấu hao TSCĐ.

## Nội dung đã đối chiếu và cách phần mềm xử lý
| Nội dung | Căn cứ | Trạng thái | Trong phần mềm |
|---|---|---|---|
| Bắt đầu / thôi trích khấu hao **theo số ngày của tháng** TSCĐ tăng hoặc giảm | TT 45/2013 Điều 9 khoản 9 | Đã đối chiếu qua nhiều nguồn trích cùng nội dung | Thẻ TSCĐ mới mặc định "Theo số ngày sử dụng". "Tròn tháng" vẫn chọn được (chị quyết 16/10 cho kế toán nội bộ) nhưng chi phí khấu hao tính thuế phải theo quy định → phải điều chỉnh khi quyết toán. CCDC / chi phí trả trước không thuộc Thông tư 45: mặc định vẫn tròn tháng |
| Phương pháp số dư giảm dần có điều chỉnh: tỷ lệ = tỷ lệ đường thẳng × hệ số điều chỉnh; khi mức giảm dần ≤ mức bình quân (giá trị còn lại ÷ số năm còn lại) thì từ năm đó chuyển sang đường thẳng | TT 45/2013 Điều 13 khoản 2 | Đã đối chiếu | `DepreciationCalculator.DecliningYears` |
| Hệ số điều chỉnh: **1,5** (thời gian trích khấu hao đến 4 năm), **2,0** (trên 4 đến 6 năm), **2,5** (trên 6 năm) | TT 45/2013 Điều 13 khoản 2 điểm c | Các nguồn tìm được chỉ viện dẫn "theo bảng trong Thông tư", không đăng nguyên văn từng mức; mức 1,5 / 2 / 2,5 theo văn bản gốc mà em biết | `DefaultDecliningFactor`; sửa được từng thẻ (1 → 5) |
| Điều kiện áp dụng số dư giảm dần: doanh nghiệp thuộc lĩnh vực đòi hỏi công nghệ thay đổi, phát triển nhanh; TSCĐ là máy móc, thiết bị, dụng cụ đo lường, thí nghiệm **đầu tư mới** (chưa qua sử dụng) | TT 45/2013 Điều 13 khoản 2 điểm b | Đã đối chiếu | Phần mềm **không kiểm tra được** điều kiện này; form hiện cảnh báo, người lập thẻ chịu trách nhiệm. Chỉ cho TSCĐ |
| Phương pháp theo số lượng, khối lượng sản phẩm: TSCĐ trực tiếp tham gia sản xuất sản phẩm; xác định được tổng sản lượng theo công suất thiết kế; công suất sử dụng thực tế bình quân tháng không thấp hơn 100% công suất thiết kế | TT 45/2013 Điều 13 khoản 3 | Theo văn bản gốc em biết, chưa tìm được nguồn trích lại | Form cảnh báo điều kiện; phần mềm không kiểm tra công suất |
| Phương pháp theo thời gian (số ngày thực tế) và kỳ ghi sổ tháng / quý / năm | Không có trong Thông tư 45 (đây là phân bổ chi phí trả trước, chính sách kế toán của công ty) | Lựa chọn nghiệp vụ theo yêu cầu của chị | Dùng cho CCDC / chi phí trả trước (bảo hiểm, thuê nhà xưởng...); TSCĐ nên dùng đường thẳng, số dư giảm dần hoặc theo sản lượng |

## Nguồn tìm được
- Thông tư 30/2025/TT-BTC: luatvietnam.vn, thuvienphapluat.vn (bài "Đã có Thông tư 30/2025/TT-BTC bổ sung quy định về trích khấu hao tài sản cố định").
- Thông tư 45/2013/TT-BTC Điều 9, Điều 13: thuvienphapluat.vn, MISA (amis.misa.vn "Tính khấu hao theo phương pháp số dư giảm dần có điều chỉnh"), luatvietnam.vn.
- Tìm "Thông tư 162/2025/TT-BTC" không thấy văn bản nào như vậy; một trang nhắc Thông tư 141/2025/TT-BTC (quản lý, tính hao mòn, trích khấu hao TSCĐ tại cơ quan, tổ chức, đơn vị) thuộc khu vực công, không áp dụng cho doanh nghiệp.

## Việc còn lại
1. Kế toán trưởng đối chiếu bảng hệ số với Phụ lục / Điều 13 trong văn bản gốc.
2. Kiểm tra khung thời gian trích khấu hao theo nhóm TSCĐ (Phụ lục 1 Thông tư 45): phần mềm chưa cảnh báo khi nhập thời gian sử dụng ngoài khung.
