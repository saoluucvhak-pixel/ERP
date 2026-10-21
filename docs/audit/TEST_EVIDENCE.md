# Bằng chứng kiểm thử

## Đã chạy (CI GitHub Actions, nhánh `audit/erp-full-quality-upgrade`)
- **run #314 (commit `e9f4779`): đạt backend (SQLite), backend-postgres, frontend** — gồm `AuditFixTests`, đảo hóa đơn mua có tính lại đơn giá, tất toán khế ước phải trích lãi, kết chuyển tạo lại sau khi đảo, chứng từ phân hệ không sửa tay. Lần chạy #313 (commit `8f5ebfb`) hỏng 2 test do lỗi chia hai lần khi đảo; đã sửa ở `e9f4779`.
- run #309 (commit `d2d3786`): đạt backend (SQLite), backend-postgres, frontend — gồm `RouteContractTests` và các sửa OPS-01/03/CI.
- Trước đó trên `main`: run #306/#303 đạt — diễn tập dữ liệu mẫu FAST (nhập → ghi sổ → sao lưu → khôi phục → đối chiếu từng TK), kiểm thử hai người dùng cùng lúc (chi quỹ, ghi sổ trùng, giữ hàng, phân bổ công nợ) trên SQLite và PostgreSQL.
- Cục bộ: `npm test` 42/42, `npm run build` đạt (web).

## Chưa chạy / không chứng minh được
- Không có .NET SDK trong môi trường kiểm toán → mọi test .NET chạy qua CI, không chạy cục bộ.
- Chưa có benchmark hiệu suất; chưa có đối chiếu với báo cáo in từ FAST/MISA; chưa thử tay trên trình duyệt.
