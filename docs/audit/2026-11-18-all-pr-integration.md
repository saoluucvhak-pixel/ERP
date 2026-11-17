# Tích hợp toàn bộ PR đang mở ngày 18/11/2026

Nguồn main df1ede0db46463c4452001085e0a6c3fdca062e1. Người dùng yêu cầu gộp tất cả PR, xử lý xung đột và viết prompt nâng cấp theo 8 nhóm nghiệp vụ, dữ liệu, thiết kế, quyền, biểu mẫu, bố cục, lưu trữ và vận hành.

## Phạm vi
- #62 tài liệu đóng gói Windows và sơ đồ dữ liệu.
- #63 lịch sao lưu SQLite, verify, bản chép thứ hai, retention và tình trạng quản trị.
- #64 DataRoot, đường dẫn DB/files/backup/key, công cụ đổi nơi lưu và giữ dữ liệu khi gỡ.
- #78 mốc ngày điều chỉnh khi trích trước CKTM.
- #82 JsonSchema.Net 9.5.0.
- #83–86 các gói testing/SQLite/EF Core 10.0.14 theo PR.
- #87 @types/node 26.6.6; #89 Playwright 1.65.0; #90 Vite 8.3.6; #91 oxlint 1.92.0; #92 plugin-react 6.2.1.

## Xử lý xung đột và sửa liên quan
Giữ đồng thời nội dung hướng dẫn CKTM, backup và DataRoot trong CLAUDE/docs vận hành. Project test giữ cả Mvc.Testing và Data.Sqlite 10.0.14. Package.json/lockfile giữ các phiên bản nâng cấp của mọi PR, đã kiểm bằng npm ci. Các migration và kiểm soát mới trên main được giữ khi merge ba chiều từ các nhánh cũ.

Test tách mua/bán trước đây tạo đơn ngày 20/10/2026 nhưng màn hình lọc tháng hiện tại. Ngày 18/11 test không thấy đơn dù API tạo thành công. Đã đổi ngày fixture sang ngày chạy theo lịch máy, giữ nguyên kiểm tra hai chiều và chạy lại đủ 17 E2E.

## Kiểm chứng local
Backend 601/601 (126 domain + 475 tích hợp SQLite), 0 fail/skip; frontend 77/77; E2E 17/17; build đạt; lint 0 lỗi/26 cảnh báo. Ảnh CKTM 1440/390 px và ảnh đại diện phân hệ dùng dữ liệu giả. Công cụ chụp ảnh nằm trong output của phiên làm việc. Kết quả local không thay cho xác nhận provider PostgreSQL của CI hoặc nghiệm thu bộ cài Windows.

## Cảnh báo bảo mật còn mở
npm audit đầy đủ báo 5 high cùng bắt nguồn từ picomatch 4.0.7 trong chuỗi Vite/tinyglobby/fdir/plugin-react. Phiên bản picomatch này cũng có trong lockfile main trước tích hợp. npm audit --omit=dev báo 0. Thử cập nhật trong giới hạn semver vẫn giữ phiên bản 4.0.7. Advisory tại thời điểm kiểm tra ghi chưa có bản vá phát hành.

Giữ nguyên script audit của CI nên bước frontend có thể fail vì cảnh báo này. Báo cáo phát hành phải hiển thị kết quả thực tế; theo dõi bản vá và kiểm tra lại trước khi mở công cụ build/dev cho đầu vào glob không tin cậy. Đây là rủi ro dev tooling được ghi nhận trong đợt gộp theo yêu cầu người dùng.

[GitHub Advisory GHSA-fc5q-39xc-vjpf](https://github.com/advisories/GHSA-fc5q-39xc-vjpf)

## Các phần cần nghiệm thu tiếp
Đổi nơi lưu dữ liệu bằng script và trang Inno Setup cần thử trên Windows với đường dẫn Unicode, dữ liệu đích đã tồn tại và app còn chạy. Khôi phục trên máy mới cần kiểm DB, attachments, khóa mã hóa và báo cáo đối chiếu. Sao lưu PostgreSQL hiện đi theo runbook pg_dump. Pháp lý biểu mẫu cần mapping văn bản/phụ lục/hiệu lực. Prompt triển khai chi tiết tại ../roadmap/2026-11-18-ERP-UPGRADE-PROMPT.md.
