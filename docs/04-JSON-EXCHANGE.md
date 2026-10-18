# 04 — JSON Exchange Specification (v0.1)

## 1. Nguyên tắc

- JSON là định dạng **trao đổi**, không phải cơ sở dữ liệu kế toán (master prompt §4.4).
- Chế độ LAN: client ghi trực tiếp qua API; gói JSON chỉ dùng cho tích hợp phần mềm ngoài và chi nhánh độc lập.
- Gói nhập **không bao giờ** tự ghi sổ và **không bao giờ** ghi đè chứng từ đã có; chứng từ nhập vào ở trạng thái Nháp và đi qua quy trình duyệt bình thường.

## 2. Quy tắc dữ liệu

| Hạng mục | Quy tắc |
|----------|---------|
| Số tiền | Chuỗi thập phân `^(0|[1-9][0-9]{0,13})(\.[0-9]{1,4})?$` — không dấu phân cách nghìn, dấu chấm thập phân. Số JSON (number) bị từ chối. |
| Tỷ giá | Chuỗi, tối đa 6 chữ số thập phân |
| Ngày | `yyyy-MM-dd`; thời điểm: ISO-8601 có múi giờ |
| Mã | `^[A-Z0-9_.-]{1,30}$` |
| Chiều | `"D"` (Nợ) / `"C"` (Có) |
| Giới hạn | ≤ 10.000 chứng từ/gói, ≤ 1.000 dòng/chứng từ, số tiền ≤ 99.999.999.999.999,9999 |

## 3. Phong bì gói (envelope)

Trường bắt buộc: `schema_version, package_id, event_id, source_system, company_code, branch_code (nullable), created_at, document_type, source_id, source_version, payload, checksum, auth`.

- **Canonical serialization**: RFC 8785 (JCS) — khóa sắp theo UTF-16, không khoảng trắng; HAK chỉ cho phép số nguyên trong JSON nên không phát sinh vấn đề biểu diễn số thực.
- **checksum** = `SHA-256 hex(JCS(payload))` — chỉ phát hiện hỏng dữ liệu, **không** chứng minh nguồn gửi.
- **auth** (v1) = `HMAC-SHA256(secret, JCS(gói với auth.signature bị loại))`, Base64. Khóa bí mật theo (công ty, source_system, key_id) trong `exchange_partner_keys`. v2 dự kiến Ed25519 (khóa công khai) để không phải chia sẻ bí mật.
- Thứ tự kiểm tra: parse → JSON Schema → company_code khớp phiên → khóa → chữ ký → checksum → replay (`package_id` đã nhập) → từng chứng từ.

## 4. Xử lý từng chứng từ

| Kết quả | Điều kiện | Commit |
|---------|-----------|--------|
| `VALID` | Ánh xạ mã TK/đối tượng thành công, qua VoucherValidator, kỳ mở | Tạo chứng từ Nháp |
| `DUPLICATE_IDENTICAL` | Đã có chứng từ cùng (source_system, source_id, source_version) | Bỏ qua (idempotent) |
| `CONFLICT` | Cùng source_id nhưng khác source_version | Toàn gói bị từ chối; đưa vào hàng chờ xung đột (UI: GĐ5) |
| `INVALID` | Lỗi ánh xạ/validation/kỳ khóa/source_id lặp trong gói | Toàn gói bị từ chối |

Chế độ: `commit=false` (dry-run + preview), `commit=true` (all-or-nothing). Kết quả theo `schemas/import-result.schema.json`. Mọi lần commit/từ chối ghi audit log.

## 5. Danh mục schema

| Schema | Trạng thái |
|--------|-----------|
| `journal-voucher.schema.json` | **Đã viết + test** (ví dụ hợp lệ/lỗi trong `schemas/examples/`) |
| `import-result.schema.json` | **Đã viết + test** |
| master-data, bank-transaction, payment-request, purchase-invoice, sales-invoice, inventory-movement, payroll, fixed-asset, exchange-rate, sync-conflict | Chưa viết — làm cùng phân hệ tương ứng (GĐ2–5) |

## 6. Sync Engine (thiết kế, GĐ5)

`sync_packages` (gói gửi/nhận), `sync_events` (event_id duy nhất), `external_id_mappings` (source_system, source_id ↔ id nội bộ), `sync_errors` + retry queue, conflict queue. **Chủ sở hữu dữ liệu**: mỗi bản ghi có `owner_node`; nút nhận không phát lại bản ghi mà nó không sở hữu → tránh vòng lặp đồng bộ.

## 7. Giới hạn hiện tại

- Ví dụ trong `schemas/examples/journal-voucher.valid.json` có checksum/chữ ký minh họa (toàn số 0/chữ A), chỉ hợp lệ về schema, không qua được bước xác thực.
- Khóa HMAC đang lưu Base64 trong DB; cần bảo vệ bằng Windows DPAPI trước khi dùng thật (TODO GĐ5).
- Chưa có chức năng export gói.

## Gói bảng lương (`document_type = "payroll"`, schema `schemas/payroll.schema.json` 1.0) — đã hiện thực
- Vỏ gói, checksum, chữ ký HMAC, chống nhập lại `package_id` giống gói journal-voucher. `source_id` = mã bảng lương; `source_version` tăng khi tính lại.
- Mỗi nhân viên: `employee_code` (đối tượng có vai trò Nhân viên), `expense_group` (GENERAL / PRODUCTION / SELLING / ADMIN), `gross`, `deductions` (BHXH, BHYT, BHTN, thuế TNCN, khác), `net`, `employer` (BHXH, BHYT, BHTN, KPCĐ). HAK **không tính tỷ lệ** bảo hiểm / thuế (phần mềm lương tính; tỷ lệ pháp lý [CẦN XÁC MINH]); HAK kiểm tra gross − khấu trừ = net (cả ở CSDL bằng check constraint).
- Kết quả: `VALID` (xem trước), `CREATED` (một chứng từ GJ nháp, đi qua quy trình duyệt), `DUPLICATE` (đã nhập phiên bản này), `CONFLICT` (phiên bản khác đang có hiệu lực — hủy/đảo chứng từ đó trước), `INVALID` (không ghi gì; liệt kê lỗi từng nhân viên).
- Hạch toán theo vai trò: Nợ `PAYROLL_EXPENSE_<GROUP>` (thiếu thì dùng `PAYROLL_EXPENSE_GENERAL`) lương gộp + phần DN đóng / Có `PAYROLL_PAYABLE` thực lĩnh từng nhân viên (khoản mở theo người) / Có `PAYROLL_SI_PAYABLE`, `PAYROLL_HI_PAYABLE`, `PAYROLL_UI_PAYABLE` (phần NLĐ + DN), `PAYROLL_PIT_PAYABLE`, `PAYROLL_OTHER_DEDUCTION`, `PAYROLL_UNION_PAYABLE`.
- Bảng lương lưu bất biến; báo cáo tổng hợp theo nhân viên chỉ tính bảng lương có chứng từ đã ghi sổ.
- **Khóa trao đổi**: quản trị tạo khóa (bí mật 256-bit sinh ở máy chủ, hiển thị một lần, không ghi vào nhật ký), ngừng dùng khóa (không xóa). Đã kiểm chứng: gói do chương trình Node.js độc lập ký (JCS + SHA-256 + HMAC) được HAK xác thực.
