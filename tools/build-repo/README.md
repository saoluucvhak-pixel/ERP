# ERP-build

Repo công khai chỉ để build `HAK-Setup.exe` trên máy Windows miễn phí của GitHub. **Không chứa mã nguồn.** Mã nguồn lấy từ repo riêng tư `saoluucvhak-pixel/ERP` khi chạy.

## Thiết lập một lần
1. Tạo token: GitHub → Settings → Developer settings → Fine-grained personal access tokens → Generate. Repository access: chỉ `ERP`. Permissions: `Contents: Read-only`. Đặt hạn dùng ngắn (ví dụ 90 ngày).
2. Trong repo `ERP-build`: Settings → Secrets and variables → Actions → New repository secret, tên `ERP_READ_TOKEN`, dán token.
3. Settings → Actions → General → "Fork pull request workflows": giữ mặc định (không chạy với fork). Workflow này chỉ chạy tay.

## Tạo bộ cài
Actions → build-setup → Run workflow → chọn nhánh/tag của repo ERP (mặc định `main`) và số phiên bản. Sau 10–15 phút, bộ cài và mã băm SHA-256 nằm ở mục Releases.

## Lưu ý
- Releases của repo công khai ai cũng tải được: bộ cài (chương trình đã biên dịch) sẽ công khai.
- Nhật ký chạy (log) của repo công khai cũng công khai: có tên tệp và thông báo build của repo ERP, không có nội dung mã.
- Bộ cài chưa ký số: Windows SmartScreen cảnh báo "nhà phát hành không xác định".
