# 06 — Test Strategy & Real Data Reconciliation Specification (v0.1)

## 1. Tầng kiểm thử

| Tầng | Công cụ | Vị trí | Hiện có |
|------|---------|--------|---------|
| Unit | xUnit | `tests/Hak.Domain.Tests` | 48 test: Money, làm tròn, quy đổi, số dư, validator, máy trạng thái, sinh kỳ, 3 phương pháp tính giá xuất kho |
| Integration | xUnit + SQLite file thật trong `%TEMP%\hak-tests` | `tests/Hak.Integration.Tests` | 93 test: engine, bảo mật, JSON, công nợ, quy tắc, tiền, ngân hàng, kho, BDMT, mua/bán, kết chuyển, API HTTP |
| Frontend unit | node:test | `web/src/*.test.ts` | 3 test xử lý tiền dạng chuỗi |
| E2E UI | Playwright | — | Chưa tự động hóa; đã kiểm thử tay luồng thu tiền → công nợ → BCĐPS (STATUS.md) |
| Performance | BenchmarkDotNet + bộ sinh dữ liệu | — | Chưa có — **chưa công bố số liệu hiệu năng** |
| Real data | RECON engine | — | Chưa có — chờ dữ liệu thực |

`TestDbGuard`: test chỉ mở file DB dưới thư mục tạm của test; API test cấu hình `Hak:DatabasePath` vào cùng thư mục.

## 2. Ma trận negative test (mục 40)

| Tình huống | Test | Trạng thái |
|-----------|------|-----------|
| Định khoản mất cân bằng | `Unbalanced_IsRejectedAtSubmit`, `VoucherRulesTests.Unbalanced` | ✅ |
| Chứng từ trùng nguồn | `DuplicateSource_IsRejected` | ✅ |
| Thanh toán vượt | `PartialPayment_ThenOverpaymentRefused_*`, `Allocation_MustMatch*` | ✅ |
| Chứng từ sai công ty | `AccountOfAnotherCompany_CannotBeUsed`, `VoucherOfAnotherCompany_IsInvisible` | ✅ |
| Ghi sổ kỳ khóa | `LockedPeriod_BlocksPosting_*` | ✅ |
| Mất điện giữa transaction | `Post_IsAtomic_WhenFailureOccursInsideTransaction` (lỗi giả lập trong transaction), `UncommittedTransaction_LeavesNoTrace` | ✅ mô phỏng; chưa test kill process thật |
| Đứt LAN | — | ⏳ GĐ5 (chưa có PostgreSQL/LAN) |
| JSON lỗi schema | `SchemaViolations_AreRejectedBeforeAnyProcessing` (5 biến thể) | ✅ |
| Import lại gói cũ | `Replay_OfSamePackage_IsRejected` | ✅ |
| Hai người sửa cùng chứng từ | `ConcurrentEdit_SecondWriterGetsConflict` | ✅ |
| Quy tắc hạch toán thiếu / trùng / vai trò chưa ánh xạ | `RuleActivation_*`, `UnmappedRole_And_AmbiguousRules_AreReported`, `InvalidRuleDefinitions_AreRejected` | ✅ |
| Thanh toán ghi sổ hai lần / hủy lệnh chưa thực hiện tạo bút toán / đối chiếu sửa chứng từ gốc / chuyển khoản nội bộ | `BankingTests.*`, `InternalTransfer_*` | ✅ |
| Sao kê lệch số dư, trùng file, trùng mã GD, không liên tục | `StatementImport_ValidatesBalanceDuplicatesAndContinuity` | ✅ |
| Nhiều giao dịch có khả năng khớp | `AmbiguousCandidates_GoToConfirmationQueue_*` | ✅ |
| Xuất kho âm, phiếu kho lùi ngày, tính giá trùng/sai thứ tự kỳ, đảo phiếu đã bị dùng, sửa sổ kho bằng SQL | `InventoryTests.*` | ✅ |
| Quỹ âm (kể cả chứng từ lùi ngày) | `NegativeCash_IsBlocked_IncludingBackdatedPayments` | ✅ |
| Chuyển khoản nội bộ ghi nhầm doanh thu | `InternalTransfer_NeverTouchesRevenueOrExpense` | ✅ |
| Sửa/xóa phân bổ thanh toán | `AutoFifo_ManualVoid_AndGuards` (trigger) | ✅ |
| Khóa kỳ khi còn chứng từ chờ ghi sổ; kết chuyển chạy lại | `LockedPeriod_*`, `Closing_*` | ✅ |
| Sửa chứng từ đã ghi sổ | `Ledger_DirectSqlUpdateOrDelete_IsBlockedByTriggers`, `Ledger_ModificationThroughEf_IsBlockedByInterceptor` | ✅ |
| Sửa audit log | `AuditChain_VerifiesAndDetectsTampering` | ✅ |
| Sai lệch báo cáo | — | ⏳ GĐ7 |
| Backup không hợp lệ | — | ⏳ chưa làm backup |

## 3. Integration test (mục 39)

| Luồng | Trạng thái |
|-------|-----------|
| Voucher → Journal → General Ledger → Trial balance | ✅ `Voucher_To_Journal_To_TrialBalance` |
| JSON → Import → Approval → Posting | ✅ `DryRun_Then_Commit_CreatesDrafts_ThatStillNeedApproval` |
| HTTP API → Engine (quyền, tiền dạng chuỗi, 409) | ✅ `ApiTests.FullFlow_OverHttp` |
| Payment → Settlement (phiếu thu/chi → phân bổ công nợ, đảo → giải phóng) | ✅ `SettlementTests` |
| Business document → Posting rule → Voucher → Journal | ✅ `CashDocument_*`, `Phase2Fixture` |
| Period-end closing → Journal | ✅ `Closing_MovesResultAccounts_*` |
| Inventory → Accounting (phiếu kho → IR/II; tính giá tháng → IC; sổ kho = sổ cái) | ✅ `InventoryTests` |
| Purchase → Inventory/Payables (hóa đơn định giá phiếu nhập nháp theo BDMT, GRNI → AP + VAT) | ✅ `TradeTests` |
| Sales → Receivables (hóa đơn theo BDMT chứng thư, ngoại tệ; chống ghi trùng doanh thu) | ✅ `TradeTests` |
| Payroll/Tax → Accounting; Posting → BCTC | ⏳ theo giai đoạn |

## 4. Real Accounting Data Testing Engine — đặc tả

Quy trình 12 bước theo mục 34. Thiết kế dữ liệu:

- `recon_sources(id, company_id, name, software, file_name, sha256, size, received_at, received_by)` — file nguồn lưu nguyên bản, chỉ đọc.
- `recon_mappings(source_id, target_entity, column_map_json, version)`.
- `recon_runs(id, source_id, level{1..5}, period_from, period_to, started_at, engine_version)`.
- `recon_differences(run_id, level, object_type, object_key, metric, source_value, hak_value, difference, relative_difference, category, severity, status{Open, Explained, Fixed, Accepted}, cause, resolved_by, resolved_at, evidence_ref)`.

Công thức: `difference = HAK − nguồn`; `relative = difference / |nguồn|` khi nguồn ≠ 0. Phân loại lỗi theo mục 37 (17 nhóm, enum cố định). Không có chức năng “điều chỉnh để khớp”: mọi chênh lệch được giải trình hoặc sửa bằng chứng từ có nguyên nhân.

Cấp đối chiếu đầu tiên dự kiến hiện thực: **Cấp 1 (danh mục)** và **Cấp 3 (BCĐPS)** — vì đã có danh mục và trial balance.

## 5. Dữ liệu cần từ doanh nghiệp để chạy đối chiếu

1. Danh mục TK, khách hàng, NCC (Excel/CSV xuất từ phần mềm hiện hành).
2. Số dư đầu kỳ của một năm/kỳ chọn thử.
3. Sổ nhật ký chung (toàn bộ dòng) cùng kỳ.
4. BCĐPS cùng kỳ (để làm số tham chiếu).
5. Tên/phiên bản phần mềm nguồn.

## 6. Dữ liệu tổng hợp & hiệu năng (kế hoạch)

Bộ sinh dữ liệu xác định (seeded RNG): 10.000 đối tượng, 100.000 chứng từ, 1.000.000 dòng sổ; benchmark đo thời gian ghi sổ, BCĐPS tháng/quý/năm, nhập gói 10.000 chứng từ; ngưỡng cấu hình trong `perf-thresholds.json`. **Chưa chạy — không có số liệu hiệu năng nào được công bố.**
