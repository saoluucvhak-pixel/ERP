# Bằng chứng kiểm thử

## Đã chạy (CI GitHub Actions, nhánh `audit/erp-full-quality-upgrade`)
- run #309 (commit `d2d3786`): đạt backend (SQLite), backend-postgres, frontend — gồm `RouteContractTests` và các sửa OPS-01/03/CI.
- Trước đó trên `main`: run #306/#303 đạt — diễn tập dữ liệu mẫu FAST (nhập → ghi sổ → sao lưu → khôi phục → đối chiếu từng TK), kiểm thử hai người dùng cùng lúc (chi quỹ, ghi sổ trùng, giữ hàng, phân bổ công nợ) trên SQLite và PostgreSQL.
- Cục bộ: `npm test` 42/42, `npm run build` đạt (web).

## Chưa chạy / không chứng minh được
- Không có .NET SDK trong môi trường kiểm toán → mọi test .NET chạy qua CI, không chạy cục bộ.
- Chưa có benchmark hiệu suất; chưa có đối chiếu với báo cáo in từ FAST/MISA; chưa thử tay trên trình duyệt.
- Test mới của đợt này (`AuditFixTests`) chỉ coi là đạt khi CI đạt.
