# 05 — Report Catalogue (v0.1)

> **Cảnh báo pháp lý**: chưa có mẫu báo cáo tài chính nào được tuyên bố đúng TT 99/2025/TT-BTC. Mã mẫu và công thức chỉ tiêu phải được nhập từ Phụ lục IV của văn bản gốc, đối chiếu và ký xác nhận bởi kế toán trưởng trước khi đánh dấu `Verified` (xem 07-LEGAL-REGISTER).

## 1. Report Engine — mô hình metadata

`report_definitions(report_id, name, category, regime_version_id NULL=quản trị)` → `report_versions(version, effective_from, effective_to, verification_status, legal_source_id)` → `report_formulas(line_code, line_name, order, formula_type{ACCOUNT_BALANCE, ACCOUNT_MOVEMENT, SUM_LINES, MANUAL, SUBLEDGER}, expression, sign, balance_side, rounding, drill_down)` → `report_snapshots(params, generated_at, generated_by, result_json, hash)`.

**Đã hiện thực (2026-10-15)** — `src/Hak.Domain/Reports` (công thức, đánh giá), `src/Hak.Application/Reports/ReportService.cs`, màn hình *Báo cáo theo mẫu*. Lưu trữ thực tế: `report_definitions` (mã, phiên bản, Nháp/Đã xác minh) → `report_lines` / `report_checks`; `report_snapshots` (kết quả chốt + SHA-256, chỉ thêm). Mẫu đã xác minh và ảnh chụp bị trigger CSDL khóa (SQLite + PostgreSQL).

Biểu thức (đã hiện thực):
- `BAL_D(111*, 112*)` / `BAL_C` / `BAL_NET` — số dư cuối kỳ, thuần theo từng TK (phần Nợ / phần Có / có dấu, Nợ dương); `OPEN_D/OPEN_C/OPEN_NET` đầu kỳ.
- `GROSS_D(131*)` / `GROSS_C` — dư theo từng TK **và** đối tượng (TK lưỡng tính); `OPEN_GROSS_D/C`.
- `MOV_D(632*)` / `MOV_C` phát sinh trong kỳ; `MOV_C(511* ; 911*)` chỉ phần đối ứng (dấu `;`). Bút toán có một bên một TK được ghép chính xác; bút toán nhiều Nợ – nhiều Có **bị báo lỗi kèm số chứng từ**, không phân bổ đoán.
- `NET_D(632*)` / `NET_C(511*)` phát sinh thuần trong kỳ **không tính chứng từ kết chuyển (CLS)** — số liệu B02.
- `PS_D(6424*)` / `PS_C` phát sinh trong kỳ **trừ bút toán đảo** (chứng từ REV ghi âm vào bên bị đảo) — vd khấu hao B03 gián tiếp.
- `FXR(131*)` Nợ − Có trên chứng từ đánh giá lại tỷ giá (FXR) — loại phần đánh giá lại khỏi chênh lệch số dư (B03 gián tiếp).
- `VT_D(2141* ; FAD)` / `VT_C` phát sinh chỉ trên chứng từ thuộc các loại đã nêu (FAD ghi giảm TSCĐ, DEP khấu hao…).
- `CF(01, 02)` luồng tiền theo mã B03-DN; `MAX0(biểu thức)` lấy phần dương.
- **Mẫu nhiều cột** (`columns`, tối đa 10): công thức gồm một phần cho mỗi cột ngăn bởi `|` (trống = ô trống) hoặc một công thức dùng cho mọi cột; `LINE()` lấy cùng cột; quy tắc kiểm tra chạy cho từng cột; kỳ so sánh tạo thêm một cột so sánh sau mỗi cột. Dùng cho các bảng B09 (TSCĐ theo nhóm, thuế, vốn chủ sở hữu).
- Mẫu TK có nhãn: `131*[DH]`, `131*[!DH]`, `128*[TDT,!HCSD]` (nhãn đặt ở cột Nhãn BC của TK).
- `!1388*` loại trừ; `LINE(110) + LINE(120)`; `+ - *`, ngoặc; công thức trống = dòng tiêu đề. Quy tắc kiểm tra `LINE(270) = LINE(440)`.
- Quy trình: kế toán soạn (catalog.manage) → kế toán trưởng xác minh (period.lock, **khác người soạn**) → mẫu đóng băng; sửa = phiên bản mới. Chạy có cột so sánh (kỳ khác). Chốt số liệu (period.lock) chỉ khi không có lỗi và mọi quy tắc kiểm tra đạt. Báo cáo từ mẫu chưa xác minh hiển thị "MẪU CHƯA XÁC MINH".
Mỗi chỉ tiêu drill-down: chỉ tiêu → danh sách TK/đối tượng → dòng sổ → chứng từ (đã có liên kết `journal_lines → journal_entries → vouchers`).

## 2. Danh mục báo cáo & trạng thái

| Nhóm | Báo cáo | Nguồn | Trạng thái |
|------|---------|-------|-----------|
| A | Báo cáo tình hình tài chính | REG + GL | **Công cụ đã có** (Report Engine); mẫu pháp lý chưa nhập — chờ Phụ lục IV đã xác minh |
| A | Báo cáo kết quả hoạt động kinh doanh | REG + GL | Công cụ đã có; mẫu chờ như trên |
| A | Báo cáo lưu chuyển tiền tệ (trực tiếp/gián tiếp) | GL + phân loại dòng tiền | Chưa làm — cần thiết kế mã dòng tiền trên dòng chứng từ |
| A | Thuyết minh BCTC | Nhiều nguồn + nhập tay | Chưa làm |
| B | **Sổ nhật ký chung** | journal_lines | **Đã có truy vấn** `GeneralJournalAsync` + API `/api/reports/general-journal`; chưa có mẫu in |
| B | Sổ cái tài khoản | journal_lines | Chưa làm (dễ dựng từ truy vấn trên) |
| B | Sổ chi tiết tài khoản | journal_lines + partner | Chưa làm |
| B | **Bảng cân đối số phát sinh** | journal_lines | **Đã có** `TrialBalanceAsync` + API; hỗ trợ TK lưỡng tính trình bày gộp theo đối tượng; chưa cộng dồn lên TK cha; chưa có mẫu in |
| C–H | Tiền, ngân hàng, công nợ, kho, thuế, tài sản, quản trị | Phân hệ tương ứng | Theo giai đoạn 2–6 |

## 3. Khung đặc tả mẫu (áp dụng cho từng báo cáo — mục 32)

| # | Nội dung | Bảng cân đối số phát sinh (ví dụ đã điền) |
|---|----------|-------------------------------------------|
| 1 | Tên, mã mẫu pháp lý | Bảng cân đối số phát sinh — mã mẫu **[CẦN XÁC MINH theo Phụ lục III/IV TT99]** |
| 2 | Phạm vi | Một công ty, một khoảng ngày |
| 3 | Kỳ | Tham số `from`, `to` |
| 4 | Cột/dòng | Dòng = TK có phát sinh hoặc số dư; cột = Dư đầu Nợ/Có, PS Nợ/Có, Dư cuối Nợ/Có |
| 5 | Công thức | Dư đầu có dấu = Σ(Nợ − Có) trước `from`; PS = Σ trong [from, to]; Dư cuối = Dư đầu + PS Nợ − PS Có |
| 6 | Nguồn | `journal_lines` |
| 7 | Dấu | Nợ dương |
| 8 | Quy tắc số dư | TK `Net`: tách theo dấu số dư thuần; TK `GrossByPartner`: tách theo từng đối tượng rồi cộng |
| 9 | Làm tròn | Không làm tròn thêm — số liệu đã ở độ chính xác đồng tiền hạch toán |
| 10 | Kỳ trước | Không áp dụng |
| 11 | Kết chuyển | Không áp dụng (phản ánh bút toán kết chuyển đã ghi sổ) |
| 12 | Kiểm tra cân đối | ΣDư đầu Nợ = ΣDư đầu Có; ΣPS Nợ = ΣPS Có; ΣDư cuối Nợ = ΣDư cuối Có (`IsBalanced`) |
| 13–14 | Dữ liệu vào / kết quả kỳ vọng | Xem test `TrialBalance_*` trong `tests/Hak.Integration.Tests/PostingEngineTests.cs` |
| 15 | Nghiệm thu | Khớp BCĐPS của phần mềm hiện hành trên dữ liệu thực ở Cấp 3 (06 §5) |
