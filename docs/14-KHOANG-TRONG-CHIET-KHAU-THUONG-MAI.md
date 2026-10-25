# 14 — Phân tích khoảng trống: prompt "Trade Discount & Rebate Management" so với HAK (25/10/2026)

Nguồn: tệp chị gửi `PROMPT_ERP_CHIET_KHAU_THUONG_MAI.md` (13 mục). Mục này là **bước 1** của prompt (khảo sát hệ thống hiện tại, phân tích khoảng trống); chưa lập trình thêm. Chiết khấu thanh toán nằm ở docs/13.

| Yêu cầu của prompt | HAK hiện có | Khoảng trống |
|---|---|---|
| Tách CKTM / giảm giá hàng bán / CKTT, không dùng chung chứng từ & TK | Có: `DiscountKind` Trade/Allowance, vai trò `SALES_TRADE_DISCOUNT` / `SALES_DISCOUNT`, CKTT riêng (`PAYMENT_DISCOUNT_*`) | Marketing/hỗ trợ thương mại, hàng tặng chưa có |
| Chiết khấu trực tiếp % hoặc số tiền theo dòng | Có (dòng hóa đơn `DiscountPercent`/`DiscountAmount`, mặc định % từ dòng đơn hàng) | Theo toàn đơn, nhóm KH/hàng, kênh, khu vực, số lượng: **chưa**; không có điều kiện hiệu lực/cấm cộng dồn |
| Rebate theo kỳ, ngưỡng, quyết toán | Có: thỏa thuận theo bậc (`DiscountAgreement`), **hồi tố toàn bộ** theo bậc đạt, quyết toán bằng hóa đơn điều chỉnh nháp hoặc bằng hạch toán điều chỉnh giảm, hủy quyết toán | **Lũy tiến từng phần: ĐÃ CÓ (26/10, cờ `Progressive`, migration `Phase22DiscountProgressive`)**; mức trần/sàn, loại trừ, **trích trước/ước tính + hoàn nhập** chưa có; chi tiền / bù trừ nghĩa vụ rebate chưa có (mới giảm công nợ trực tiếp) |
| Khuyến mãi (X tặng Y, combo, voucher, ngân sách) | Không | **Chưa có** (kể cả hàng tặng qua kho) |
| Quy trình tạo → mô phỏng → duyệt → kích hoạt → quyết toán → đóng kỳ | Thỏa thuận: lập/sửa/quyết toán cần quyền duyệt; xem trước (`Preview`) | Phiên bản chính sách, mô phỏng giá, gửi duyệt/từ chối riêng: **chưa** |
| Snapshot kết quả tính trên báo giá/đơn/hóa đơn; không tính lại chứng từ đã ghi sổ | Hóa đơn bất biến sau ghi sổ; số liệu chiết khấu lưu trên dòng | Chưa lưu giải thích phép tính (chính sách/phiên bản/điều kiện) |
| Trả hàng/hủy HĐ/credit note ảnh hưởng cơ sở tích lũy | Có: cơ sở = doanh số đã ghi sổ trừ trả lại/giảm giá; đảo hóa đơn gốc bị chặn khi còn điều chỉnh | Clawback sau quyết toán (trả hàng sau khi đã chi rebate) chưa có |
| Pricing Engine (condition type, access sequence, stacking, cap/floor, thứ tự) | Không | **Chưa có** (HAK không có bộ máy điều kiện giá) |
| Cộng dồn (cộng tỷ lệ vs tuần tự vs ưu tiên) | Không | Chưa có; ví dụ 100 → 85,5 (tuần tự) và 85 (cộng) cần test khi làm |
| Phân bổ về dòng hàng, tổng khớp, phần dư tái lập | Có: chia theo tỷ lệ doanh số hóa đơn rồi dòng, dòng cuối nhận phần dư; kiểm tra tổng | Trọng số cấu hình / theo số lượng chưa có |
| Ví dụ bậc thang 250 triệu: hồi tố 7,5 triệu; lũy tiến 4,5 triệu | Cả hai đạt (test thang đo 250.000: 7.500 và 4.500; biên bậc không hở/chồng) | Lũy tiến chưa hỗ trợ hóa đơn ngoại tệ cùng thỏa thuận (báo lỗi rõ) |
| Đa tiền tệ, quy đổi đơn vị | Có: hóa đơn ngoại tệ trong thỏa thuận | Hợp đồng bằng ngoại tệ riêng chưa |
| Posting rules, idempotency, kỳ khóa, hai chiều giữa quyết toán ↔ bút toán | Có (engine ghi sổ ACID; `DiscountAgreementRun` ↔ hóa đơn/chứng từ; khóa kỳ; chống trùng quyết toán) | Nghĩa vụ rebate (TK phải trả rebate) chưa có; chỉ giảm công nợ ngay lúc quyết toán |
| Mô hình dữ liệu 19 thực thể | Có `DiscountAgreement`, `Tier`, `Run`, `RunInvoice`, `AuditLog`, quyền | Chưa có `PolicyVersion`, `EligibilityRule`, `PricingCondition`, `PromotionBudget`, `EligibleTransaction`, `RebateAccrual`, `RebateSettlement`, `Approval` riêng |
| API/tác vụ nền (retry, tiến độ, khóa chạy trùng) | API đồng bộ; quyết toán một lần có khóa duy nhất | Tác vụ nền khối lượng lớn: chưa |
| UI (dashboard, mô phỏng, hàng đợi duyệt, theo dõi doanh số…) | Màn Chiết khấu thương mại (thỏa thuận, tính, quyết toán, báo cáo) | Dashboard ngân sách, mô phỏng giá, hàng đợi duyệt: chưa |
| Kiểm soát (tách quyền, ngân sách nguyên tử, chống chia nhỏ đơn, clawback, ghi lý do override) | Phân quyền backend, công tắc lập/duyệt, nhật ký | Ngân sách, phát hiện chia nhỏ, clawback, lý do override: chưa |
| Kiểm thử | Có nhóm test chiết khấu (TradeTests: chiết khấu trên hóa đơn, thỏa thuận, ngoại tệ, mua) | Cần thêm: lũy tiến, cộng dồn, ngân sách đồng thời, clawback |

## Đề xuất lộ trình (cần chị chọn phạm vi trước khi làm)
1. ✔ ĐÃ LÀM 26/10: bậc **lũy tiến từng phần** cho thỏa thuận + test ví dụ (7,5 vs 4,5). Còn thiếu: hiển thị giải thích phép tính theo từng bậc.
2. **Nghĩa vụ rebate:** trích trước theo kỳ (Nợ giảm doanh thu / Có phải trả rebate), hoàn nhập khi quyết toán, bù trừ công nợ hoặc chi tiền, clawback khi trả hàng sau quyết toán. Cần chị chốt TK phải trả rebate và cách xử lý thuế khi trích trước [CẦN XÁC MINH].
3. **Khuyến mãi / hàng tặng / ngân sách chương trình:** lớn; cần thiết kế riêng (xuất kho hàng tặng, thuế hàng tặng).
4. **Pricing Engine tổng quát (condition/access sequence):** lớn nhất; chỉ nên làm nếu chị muốn bộ giá bán nhiều điều kiện — hiện bám tệp thiết kế gốc thì chưa cần.

Câu hỏi quyết định: chị muốn làm bước nào trước (em đề xuất 1 rồi 2)?
