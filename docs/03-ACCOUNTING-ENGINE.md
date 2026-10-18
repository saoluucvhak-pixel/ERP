# 03 — Accounting Engine Specification (v0.1)

## 1. Bất biến (Invariants) — được kiểm thử tự động

| ID | Bất biến | Nơi cưỡng chế | Test |
|----|----------|---------------|------|
| INV-01 | Mọi journal_entry: Σ amount(side=+1) = Σ amount(side=−1), > 0 | `VoucherValidator` + kiểm tra lại trong transaction | `Posting_Unbalanced_IsRejected` |
| INV-02 | Voucher `Posted` ⇔ tồn tại đúng 1 journal_entry đã commit | Cùng transaction; unique `(company_id, voucher_id)` | `Post_IsAtomic_WhenFailureInsideTransaction`, `Post_Twice_IsIdempotentlyRejected` |
| INV-03 | journal_entries/journal_lines không UPDATE/DELETE | Interceptor + trigger | `Ledger_DirectUpdate_IsBlockedByTrigger` |
| INV-04 | Không ghi sổ khi kỳ khóa / ngoài năm TC | `PeriodGuard` | `Post_InLockedPeriod_IsRejected` |
| INV-05 | Mọi tài khoản/đối tượng thuộc đúng công ty, `is_postable`, `active` | Validator | `Post_AccountOfOtherCompany_IsRejected` |
| INV-06 | Số tiền > 0, số chữ số thập phân ≤ độ chính xác đồng tiền | `Money` | `Money_*` |
| INV-07 | Đảo: chứng từ REV mới, dòng hoán đổi Nợ/Có, liên kết `reverses_voucher_id`; chứng từ gốc → `Reversed`; mỗi chứng từ đảo tối đa 1 lần | `ReverseVoucher` + unique index | `Reverse_CreatesMirrorAndNetsToZero` |
| INV-08 | Không trùng nghiệp vụ nguồn `(company, source_system, source_id)` | Unique index | `Create_DuplicateSource_IsRejected` |
| INV-09 | Dư cuối có dấu = Dư đầu + PS Nợ − PS Có; BCĐPS: ΣNợ = ΣCó ở mọi cột | `TrialBalanceService` | `TrialBalance_IsBalanced` |
| INV-10 | Người duyệt ≠ người lập (khi bật SoD) | `ApproveVoucher` | `Approve_BySameUser_IsRejected` |
| INV-11 | Mọi thao tác thay đổi trạng thái được ghi `voucher_history` + `audit_logs` | Engine | `AuditChain_IsVerifiable` |
| INV-12 | Ghi đè đồng thời → 409 | `row_version` | `ConcurrentEdit_IsRejected` |

## 2. Máy trạng thái chứng từ

```
Draft ──submit──► Submitted ──approve──► Approved ──post──► Posted ──reverse──► Reversed
  ▲                  │ reject                │ reject
  └──────────────────┴───────────────────────┘
Draft/Submitted ──cancel──► Cancelled
```
Chỉ `Draft` được sửa nội dung. Mọi chuyển trạng thái khác bảng trên → `INVALID_STATE_TRANSITION`.

## 3. API Engine

| Thao tác | Quyền | Kiểm tra | Kết quả |
|----------|-------|----------|---------|
| `ValidateVoucher` | voucher.view | Toàn bộ quy tắc §4, không ghi | `ValidationResult` (danh sách lỗi có mã) |
| `CreateDraft` / `UpdateDraft` | voucher.create / voucher.edit_draft | Công ty, row_version, nguồn trùng | Voucher Draft |
| `SubmitVoucher` | voucher.submit | Validate đầy đủ | Submitted |
| `ApproveVoucher` | voucher.approve | Validate + SoD | Approved |
| `RejectVoucher` | voucher.approve | | Draft |
| `PostVoucher` | voucher.post | Trạng thái Approved, kỳ mở, validate lại trong transaction, idempotency | Journal + Posted |
| `ReverseVoucher` | voucher.reverse | Posted, kỳ của ngày đảo mở | Voucher REV đã ghi sổ |
| `AdjustVoucher` | voucher.create | Gốc Posted | Draft ADJ liên kết `adjusts_voucher_id` |
| `ClosePeriod` / `ReopenPeriod` | period.lock / period.reopen | Không còn chứng từ Approved chưa ghi sổ trong kỳ (cấu hình); lý do bắt buộc khi mở | Trạng thái kỳ + audit |
| `GenerateClosingEntries` | voucher.create | Bộ quy tắc kết chuyển Active có phiên bản; không còn chứng từ chờ ghi sổ | Draft CLS, nguồn `HAK.CLOSING/<kỳ>`; chạy lại bị từ chối (đã hiện thực; nội dung quy tắc pháp lý chưa seed) |
| `CalculateTrialBalance` | report.view | | Bảng theo TK |
| `CalculateAccountBalance` | report.view | | Dư có dấu tại ngày |
| `ReconcileSubledger` | report.view | | Chênh lệch sổ chi tiết công nợ ↔ sổ cái theo TK/đối tượng, phát hiện phân bổ vượt (đã hiện thực) |

## 4. Quy tắc validation (mã lỗi)

| Mã | Điều kiện |
|----|-----------|
| `VOUCHER_NO_LINES` | Không có dòng |
| `VOUCHER_UNBALANCED` | ΣNợ ≠ ΣCó |
| `AMOUNT_NOT_POSITIVE` | Số tiền ≤ 0 |
| `AMOUNT_PRECISION` | Quá số chữ số thập phân của đồng tiền |
| `ACCOUNT_NOT_FOUND` / `ACCOUNT_WRONG_COMPANY` / `ACCOUNT_NOT_POSTABLE` / `ACCOUNT_INACTIVE` | |
| `PARTNER_REQUIRED` / `PARTNER_WRONG_COMPANY` | |
| `PERIOD_NOT_FOUND` / `PERIOD_LOCKED` | |
| `INVALID_STATE_TRANSITION` | |
| `SOD_VIOLATION` | Người duyệt = người lập |
| `ALREADY_POSTED` | Đã có journal |
| `DUPLICATE_SOURCE` | Trùng nguồn |
| `CONCURRENCY_CONFLICT` | row_version lệch |
| `FORBIDDEN` | Thiếu quyền hoặc không thuộc công ty |
| `REGIME_NOT_VERIFIED` | Production + phiên bản chế độ chưa xác minh |
| `LEDGER_IMMUTABLE` | Cố sửa sổ cái/chứng từ đã ghi sổ |

## 5. Tiền tệ và làm tròn

- `decimal` toàn bộ; làm tròn **MidpointRounding.AwayFromZero** (quy ước kế toán VN thông dụng — **[CẦN XÁC MINH chính sách công ty]**, cấu hình được `ToEven`).
- Độ chính xác: VND 0, USD/EUR 2 (bảng `currencies.decimal_places`); lưu trữ 4 chữ số; tỷ giá 6 chữ số.
- Quy đổi: `amount = Round(fc_amount × rate, dp(functional))`. Chênh lệch làm tròn trên chứng từ nhiều dòng ngoại tệ: dòng chênh lệch tách riêng vào TK theo cấu hình — **không tự nhét vào dòng lớn nhất**.

## 6. Posting transaction (pseudo)

```
BEGIN (SQLite: BEGIN IMMEDIATE; PG: SERIALIZABLE hoặc FOR UPDATE trên voucher)
  load voucher (+row_version) WHERE company_id = ctx.company
  assert status = Approved
  assert no journal for voucher
  assert period(posting_date) open
  re-validate lines against current accounts
  insert journal_entry + journal_lines
  voucher.status = Posted; row_version++
  insert voucher_history, audit_log(hash chain)
COMMIT  — bất kỳ lỗi → ROLLBACK, voucher giữ Approved
```

## 7. Accounting Rule Engine (đã hiện thực — `src/Hak.Application/Rules`)

`posting_rules(rule_id, regime_version_id, company_id NULL=template, document_type, business_event, conditions_json, debit_account_expr, credit_account_expr, amount_expr, dimensions_json, effective_from, effective_to, priority, version, status{Draft,Tested,Active,Retired})`.

- Biểu thức tài khoản tham chiếu **vai trò tài khoản** (`ROLE:AP_TRADE`, `ROLE:BANK(bank_account_id)`) được ánh xạ sang tài khoản chi tiết của công ty qua `account_role_mappings` → không ghi cứng TK chi tiết.
- Chọn quy tắc: lọc theo document_type, business_event, hiệu lực tại posting_date, điều kiện; nhiều quy tắc cùng priority khớp → lỗi `RULE_AMBIGUOUS`; không quy tắc → `RULE_MISSING` (negative test mục 40).
- Kích hoạt (`Active`) chỉ khi bộ test của phiên bản quy tắc đã chạy pass và lưu kết quả (`posting_rule_test_runs`).

## 8. Legal Accounting Configuration Engine

`accounting_regimes` → `accounting_regime_versions(code, effective_from, effective_to, verification_status{Unverified, Verified}, verified_by, verified_at)` → `legal_sources(document_no, issued_date, article, url, checksum_of_copy)`.
Công ty ở môi trường Production chỉ ghi sổ khi phiên bản chế độ `Verified` (mã lỗi `REGIME_NOT_VERIFIED`). Journal lưu `regime_version_id` để báo cáo năm cũ dùng đúng phiên bản.

## 9. Công nợ theo chứng từ (SettlementAllocation) — đã hiện thực

- Khoản mở = dòng sổ cái trên TK có `OpenItemNature` (Receivable/Payable) và có đối tượng. Còn lại = số tiền − Σ phân bổ còn hiệu lực.
- `settlement_allocations(debit_line, credit_line, amount, allocation_date, origin, voided_*)`: cùng công ty, TK, đối tượng; một Nợ một Có; không vượt số còn lại của cả hai dòng; ngày phân bổ ≥ ngày hạch toán của cả hai dòng và thuộc kỳ mở.
- Nguồn tạo: dòng chứng từ có `SettleJournalLineId` (tạo khi ghi sổ, cùng transaction), phân bổ tay, bù trừ FIFO, đảo chứng từ (giải phóng phân bổ cũ rồi ghép dòng gốc với dòng đảo).
- Bất biến kiểm thử: Σ(Nợ còn lại) − Σ(Có còn lại) = số dư sổ cái theo TK/đối tượng (`ReconcileAsync`); không xóa, chỉ hủy một lần (trigger).

## 10. Kiểm soát số dư âm

TK có `NegativeBalancePolicy` Block/Warn (ví dụ quỹ tiền mặt): số dư lũy kế từ ngày hạch toán trở đi, tính cả chứng từ đang xét, không được âm. Block → lỗi `NEGATIVE_BALANCE` ở Submit/Approve/Post; Warn → `NEGATIVE_BALANCE_WARNING` chỉ hiện ở Validate.
## 11. Tài sản cố định & công cụ dụng cụ (đã hiện thực — `src/Hak.Application/Assets`)
- **Nguyên giá không do phân hệ tự hạch toán**: thẻ tài sản gán từ các dòng **phát sinh Nợ đã ghi sổ** trên TK tài sản (hóa đơn mua, chứng từ khác, chứng từ số dư đầu). Một dòng có thể chia cho nhiều thẻ; tổng gán cho các thẻ đã ghi tăng không vượt số tiền dòng; dòng của chứng từ đã đảo không dùng được. Nhờ vậy sổ tài sản ↔ sổ cái khớp theo cấu trúc.
- Vòng đời thẻ: Nháp → Ghi tăng (người khác người lập khi công ty bật tách nhiệm vụ) → Ghi giảm; Nháp có thể hủy. Sau ghi tăng thông số khấu hao bất biến (interceptor + trigger SQLite/PostgreSQL).
- **Khấu hao đường thẳng theo mục tiêu lũy kế**: mỗi kỳ ghi = round(cơ sở × số tháng đã dùng ÷ số tháng) − số đã ghi; tháng cuối luôn khép đúng bằng cơ sở, không dồn sai số làm tròn. Cơ sở = nguyên giá − giá trị thu hồi − hao mòn đầu (tài sản chuyển từ hệ thống cũ, khấu hao phần còn lại trong số tháng còn lại).
- Cách tính tháng đầu/cuối là cấu hình theo thẻ **[CẦN XÁC MINH căn cứ pháp lý]**: *Tròn tháng* (tính tháng tăng, không tính tháng giảm) hoặc *Theo ngày* (từ ngày bắt đầu đến ngày trước ngày ghi giảm). Phương pháp số dư giảm dần / theo sản lượng: DESIGNED.
- **Lần chạy khấu hao** theo kỳ, toàn công ty, đúng thứ tự kỳ (lần đầu phải từ kỳ chứa ngày bắt đầu sớm nhất); sinh chứng từ `DEP` (Nợ TK chi phí / Có TK hao mòn — CCDC: Có thẳng TK CCDC). Hủy chỉ lần gần nhất; số đã ghi bị đánh dấu hủy (không xóa) và chứng từ được đảo.
- **Ghi giảm** (chứng từ `FAD`): ghi khấu hao tháng ghi giảm theo cách tính tháng, Nợ TK hao mòn (toàn bộ lũy kế), Nợ TK do người dùng chọn (giá trị còn lại), Có TK nguyên giá. Tiền thu thanh lý lập hóa đơn bán riêng. Yêu cầu đã chạy khấu hao đến hết kỳ trước và chưa chạy kỳ ghi giảm. Hủy ghi giảm khi chưa chạy khấu hao từ kỳ đó.
- Chứng từ `DEP`/`FAD` thuộc nguồn `HAK.ASSET`: không đảo trực tiếp ở màn hình Chứng từ.
- **Đối chiếu sổ tài sản ↔ sổ cái** tại một ngày theo từng TK (nguyên giá / giá trị CCDC còn lại / hao mòn lũy kế); chênh lệch = bút toán trên các TK đó chưa gắn thẻ.

## 12. Ngoại tệ — số dư nguyên tệ và đánh giá lại cuối kỳ (đã hiện thực — `src/Hak.Application/Fx`)
- Số dư ngoại tệ theo nhóm (tài khoản, đối tượng, loại tiền) từ các dòng bút toán có mã nguyên tệ: số dư nguyên tệ = Σ nguyên tệ (chỉ dòng có số nguyên tệ); số dư quy đổi = Σ số tiền hạch toán của mọi dòng mang mã loại tiền đó; tỷ giá ghi sổ = quy đổi ÷ nguyên tệ.
- **Dòng đánh giá lại** chỉ mang mã loại tiền (không số nguyên tệ, không tỷ giá) — chỉ hợp lệ trên chứng từ `FXR`; mọi loại chứng từ khác vẫn bắt buộc đủ nguyên tệ + tỷ giá và số tiền = round(nguyên tệ × tỷ giá).
- **Lần đánh giá lại** tại một ngày: mỗi nhóm thuộc TK **không theo dõi công nợ chi tiết** (tiền, tiền gửi, vay…) được đưa về round(nguyên tệ × tỷ giá cuối kỳ); số dư Nợ dùng "tỷ giá cho số dư Nợ", số dư Có dùng "tỷ giá cho số dư Có" (người dùng nhập, có thể bằng nhau; chính sách **[CẦN XÁC MINH căn cứ TT99]**). Nhóm có nguyên tệ = 0 nhưng còn số quy đổi được đưa về 0.
- Chênh lệch ghi vào TK ánh xạ vai trò `FX_REVALUATION`; kết chuyển tiếp là quy tắc kết chuyển của doanh nghiệp (không gán cứng TK pháp lý). Lưu tỷ giá + nguồn tỷ giá làm chứng cứ. Lần sau phải có ngày sau lần trước; hủy chỉ lần gần nhất (nguồn `HAK.FX` không đảo trực tiếp).
- **Công nợ gốc ngoại tệ** (khoản mở trên TK theo dõi công nợ): hóa đơn mua/bán ngoại tệ ghi dòng công nợ mang nguyên tệ (tỷ giá ghi sổ = tỷ giá tái tạo đúng tổng quy đổi, tối đa 6 chữ số); thuế GTGT (tính bằng VND) là dòng VND riêng. Mỗi phân bổ lưu cả số quy đổi và số nguyên tệ; nguyên tệ còn lại = nguyên tệ − Σ nguyên tệ đã phân bổ.
- **Thanh toán** một khoản ngoại tệ: dòng tất toán phải cùng loại tiền và ghi đúng **giá trị ghi sổ** của phần nguyên tệ thanh toán (= tỷ lệ theo nguyên tệ; phần cuối lấy toàn bộ số còn lại) — sai lệch bị chặn bằng `FX_BOOK_VALUE`; chênh lệch với tỷ giá thanh toán là **lãi/lỗ tỷ giá đã thực hiện**, ghi rõ vào TK vai trò `FX_REALIZED_GAIN` / `FX_REALIZED_LOSS`. Chức năng "Thu / chi ngoại tệ theo hóa đơn" lập sẵn chứng từ nháp như vậy (đi qua quy trình duyệt bình thường). Không phân bổ chéo VND ↔ ngoại tệ; bù trừ tự động chỉ cho khoản VND; phân bổ tay giữa hai khoản ngoại tệ chỉ khi giá trị ghi sổ hai bên bằng nhau.
- **Đánh giá lại công nợ theo từng khoản mở**: khoản được tất toán theo giá trị ghi sổ và mở lại theo tỷ giá cuối kỳ (giữ đối tượng, hạn thanh toán, số nguyên tệ); khoản Nợ dùng tỷ giá số dư Nợ, khoản ứng trước dùng tỷ giá số dư Có. Không hủy được lần đánh giá lại khi khoản mở lại đã được thanh toán.
- **Chưa hỗ trợ**: tỷ giá xuất quỹ ngoại tệ theo bình quân/đích danh (dòng tiền dùng tỷ giá thanh toán; phần quy đổi sót lại trên TK tiền ngoại tệ được xử lý khi đánh giá lại) **[CẦN XÁC MINH căn cứ TT99]**; hóa đơn điều chỉnh ngoại tệ chỉ tự tất toán hóa đơn gốc khi giá trị ghi sổ còn khớp.

## 13. Vay — khế ước, giải ngân/trả nợ, lãi vay (đã hiện thực — `src/Hak.Application/Loans`)
- Khế ước: bên cho vay, TK gốc vay (không phải TK tiền, không theo dõi công nợ theo chứng từ — khoản vay theo dõi theo khế ước), TK chi phí lãi, TK lãi phải trả, loại tiền, hạn mức, ngày bắt đầu / đáo hạn, cơ sở tính ngày (thực tế/365 hoặc /360), ngày bắt đầu tính lãi (khế ước chuyển từ hệ thống cũ). Nháp → Duyệt (người khác người lập) → Tất toán khi dư nợ gốc = 0. Sau khi duyệt khế ước bất biến; lãi suất thay đổi bằng dòng lãi suất có ngày hiệu lực (chỉ sau kỳ đã trích lãi).
- Giải ngân / trả nợ là chứng từ thông thường (Có / Nợ TK vay chi tiết bên cho vay) được **gắn** vào khế ước — không ghi trùng; chứng từ đã đảo không tính. Không gắn được bút toán thuộc kỳ đã trích lãi.
- **Trích lãi** theo kỳ, đúng thứ tự, hủy từ lần gần nhất: lãi = Σ dư nợ từng ngày × lãi suất ngày đó ÷ 100 ÷ số ngày cơ sở, làm tròn một lần mỗi kỳ; ngày giải ngân tính lãi, ngày trả nợ không tính **[CẦN XÁC MINH theo hợp đồng]**. Chứng từ `LIA`: Nợ chi phí lãi / Có lãi phải trả (đối tượng bên cho vay). Vay ngoại tệ: lãi tính theo nguyên tệ, quy đổi theo tỷ giá nhập khi trích; dòng lãi phải trả mang nguyên tệ (được đánh giá lại / thanh toán như khoản ngoại tệ khác).
- Sổ theo dõi vay: đã giải ngân, đã trả, dư nợ (quy đổi + nguyên tệ), lãi suất hiện hành, lãi đã trích, cờ "đến hạn trong 12 tháng" (phục vụ phân loại ngắn/dài hạn — **[CẦN XÁC MINH TT99]**). Đối chiếu sổ vay ↔ sổ cái theo TK + bên cho vay; dòng đánh giá lại tỷ giá hiển thị riêng.
- Vốn chủ sở hữu: theo dõi trên sổ cái (chưa có phân hệ riêng).

## 14. Điều chỉnh hàng mua (đã hiện thực — `InvoiceService.Adjustments.cs`)
- **Hàng mua trả lại** (2 bước): phiếu xuất trả nhà cung cấp từ kho của phiếu nhập gốc, đối ứng TK vai trò `GRNI`, giá vốn theo phương pháp của kho khi ghi sổ (kho bình quân cuối tháng: ghi sổ phiếu + tính giá tháng trước) → HĐ trả hàng của NCC: Nợ phải trả (giá mua + thuế, tự tất toán HĐ gốc) / Có GRNI (đúng giá vốn đã xuất) / Có thuế GTGT đầu vào; chênh lệch giá mua – giá vốn vào vai trò `COGS` **[CẦN XÁC MINH]**. Dăm gỗ trả NCC: BDMT theo tỷ lệ dòng gốc (không đo lại) **[CẦN XÁC MINH]**. Một phiếu xuất trả chỉ gắn một HĐ; tổng trả không vượt số trên HĐ gốc.
- **Giảm giá / tăng giá hàng mua**: theo chính sách chênh lệch giá đã được doanh nghiệp xác nhận — phần hàng còn trong kho điều chỉnh giá trị tồn kho (điều chỉnh giá trị kho bất biến), phần đã xuất điều chỉnh giá vốn; dòng không gắn kho điều chỉnh TK chi phí của dòng gốc. Tổng giảm trừ không vượt giá trị dòng gốc.
- **Đảo hóa đơn mua** (thường hoặc điều chỉnh): ngoài đảo chứng từ, lập điều chỉnh giá trị kho ngược dấu cho mọi điều chỉnh do hóa đơn đó tạo ra, để sổ kho luôn khớp sổ cái. Phiếu kho gắn hóa đơn không tự đảo (đảo riêng ở phân hệ kho).

## 15. Giá thành sản xuất (đã hiện thực — `src/Hak.Application/Production`)
- Đối tượng tính giá thành = thành phẩm + **một TK chi phí SX dở dang riêng** (TK chi tiết theo đối tượng) + giá thành kế hoạch/đơn vị.
- Tập hợp chi phí bằng chức năng sẵn có: phiếu xuất nguyên liệu đối ứng TK dở dang; lương / chi phí chung kết chuyển vào TK dở dang bằng bộ quy tắc kết chuyển. Thành phẩm nhập kho trong kỳ theo giá kế hoạch (phiếu nhập đối ứng TK dở dang) để xuất bán trong kỳ vẫn có giá vốn.
- Cuối kỳ: giá thành thực tế = số dư TK dở dang cuối kỳ + giá trị thành phẩm đã nhập (giá kế hoạch) − dở dang cuối kỳ (người dùng nhập theo kiểm kê; phương pháp đánh giá **[CẦN XÁC MINH]**). Chênh lệch so với giá kế hoạch phân bổ theo số lượng từng phiếu nhập (phần dư làm tròn theo phương pháp phần dư lớn nhất); mỗi phần: thành phẩm còn tồn → giá trị tồn kho (điều chỉnh giá trị kho), đã xuất → vai trò `COGS`. Sau khi tính, TK dở dang = dở dang cuối kỳ. Chứng từ nguồn `HAK.PRODUCTION`; hủy lần gần nhất (đảo chứng từ + bù trừ điều chỉnh giá trị kho). Kho bình quân cuối tháng: tính giá thành trước khi tính giá xuất kho tháng.
- Chưa hỗ trợ: nhiều thành phẩm trên cùng một TK dở dang (sản phẩm phụ, hệ số), phân bổ chi phí chung theo tiêu thức, định mức.
