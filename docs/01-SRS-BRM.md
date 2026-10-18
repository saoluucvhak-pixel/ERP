# 01 — SRS & Business Requirement Matrix (v0.1)

> Phạm vi bản này: phân tích lại toàn bộ yêu cầu, danh sách module, ma trận nghiệp vụ, đặc tả đầy đủ 20 mục cho **Phân hệ Quản trị hệ thống (SYS)** và **Kế toán tổng hợp (GL)** — hai phân hệ thuộc Giai đoạn 1–2 và là nền của mọi phân hệ khác. Các phân hệ còn lại có yêu cầu nghiệp vụ, bất biến và tiêu chí nghiệm thu; 20 mục chi tiết sẽ bổ sung khi bắt đầu giai đoạn tương ứng (theo mục 46).

## 1. Thuật ngữ

| Thuật ngữ | Nghĩa |
|-----------|-------|
| Pháp nhân / Company | Đơn vị có sổ kế toán độc lập. Mọi dữ liệu nghiệp vụ mang `company_id`. |
| Chứng từ / Voucher | Bản ghi nghiệp vụ có vòng đời Nháp → Đã gửi duyệt → Đã duyệt → Đã ghi sổ (→ Đã đảo). |
| Bút toán sổ cái / Journal entry | Bản ghi bất biến sinh ra khi ghi sổ một chứng từ. |
| Chế độ kế toán / Regime version | Bộ cấu hình có hiệu lực theo thời gian: hệ thống tài khoản, quy tắc định khoản, mẫu báo cáo, quy tắc kiểm tra. |
| Số dư có dấu | Nợ dương, Có âm. Số dư cuối = Dư đầu + PS Nợ − PS Có. |
| AWAITING_REAL_DATA_VALIDATION | Trạng thái chức năng đã đạt DoD kỹ thuật nhưng chưa đối chiếu dữ liệu thực. |

## 2. Danh sách module

| Mã | Phân hệ | Giai đoạn | Phụ thuộc |
|----|---------|-----------|-----------|
| SYS | Quản trị hệ thống (công ty, chi nhánh, năm TC, kỳ, người dùng, vai trò, đánh số, sao lưu, nhật ký) | 1 | — |
| REG | Legal Accounting Configuration Engine | 1 | SYS |
| CAT | Danh mục | 1–2 | SYS, REG |
| GL | Kế toán tổng hợp + Posting Engine | 1–2 | SYS, REG, CAT |
| CASH | Tiền mặt | 2 | GL, CAT |
| BANK | Ngân hàng | 2 | GL, CAT, ARAP |
| ARAP | Công nợ (phải thu/phải trả, SettlementAllocation) | 2 | GL, CAT |
| PUR | Mua hàng | 3 | ARAP, INV, TAX |
| SAL | Bán hàng | 3 | ARAP, INV, TAX |
| INV | Kho (kèm MT/BDMT dăm gỗ, sở hữu, mượn/trả) | 3 | GL, CAT |
| TAX | Thuế, hóa đơn điện tử, đối chiếu 3 nguồn | 4 | PUR, SAL, GL |
| FA | Tài sản cố định | 4 | GL, CAT |
| TOOL | Công cụ dụng cụ | 4 | GL, INV |
| COST | Chi phí & giá thành | 4 | GL, INV |
| PAY | Tiền lương (tích hợp JSON) | 4 | GL, XCH |
| FX | Ngoại tệ | 4 | GL, ARAP, BANK |
| LOAN | Vay & nguồn vốn | 4 | GL, ARAP |
| MULTI | Đa công ty, liên công ty, cộng gộp | 5 | GL, ARAP |
| XCH | JSON Exchange & Sync Engine | 5 (khung từ GĐ1) | SYS |
| RPT | Report Engine & báo cáo | 6 (khung từ GĐ2) | GL, REG |
| RECON | Real Accounting Data Testing Engine | 7 | XCH, RPT, GL |
| SEC | RBAC, audit log, backup/restore | 1 → 7 | SYS |

## 3. Business Requirement Matrix

Cột: **B** = bắt buộc theo master prompt; **Post** = phát sinh bút toán sổ cái; **Sub** = sổ chi tiết; **JSON** = có contract trao đổi; **Legal** = cần xác minh pháp lý.

| ID | Yêu cầu | Module | B | Post | Sub | JSON | Legal | Bất biến/kiểm soát chính |
|----|---------|--------|---|------|-----|------|-------|--------------------------|
| BR-SYS-01 | Quản lý nhiều công ty, chi nhánh phân cấp | SYS | ✔ | | | ✔ | | Dữ liệu cô lập theo `company_id` |
| BR-SYS-02 | Năm tài chính, kỳ kế toán, khóa/mở kỳ | SYS | ✔ | | | | ✔ | Không ghi sổ vào kỳ khóa; mở kỳ cần quyền + log |
| BR-SYS-03 | Người dùng, vai trò, phạm vi công ty | SYS/SEC | ✔ | | | | | Kiểm tra quyền ở backend |
| BR-SYS-04 | Đánh số chứng từ theo loại/năm | SYS | ✔ | | | | | Số duy nhất, cấp trong transaction |
| BR-SYS-05 | Sao lưu/phục hồi có mã hóa, kiểm tra toàn vẹn | SEC | ✔ | | | | | Không restore đè DB đang chạy |
| BR-SYS-06 | Nhật ký hoạt động chống sửa đổi | SEC | ✔ | | | | | Chuỗi băm audit |
| BR-REG-01 | Chế độ kế toán có phiên bản, hiệu lực từ–đến | REG | ✔ | | | ✔ | ✔ | Không sửa phiên bản đã dùng ghi sổ |
| BR-REG-02 | Lưu nguồn văn bản pháp luật mỗi nhóm quy tắc | REG | ✔ | | | | ✔ | Không kích hoạt khi chưa xác minh |
| BR-REG-03 | Dữ liệu năm cũ xem theo phiên bản lúc phát sinh | REG/RPT | ✔ | | | | ✔ | Journal lưu `regime_version_id` |
| BR-CAT-01 | Danh mục tài khoản phân cấp, chỉ hạch toán TK chi tiết | CAT | ✔ | | | ✔ | ✔ | Không xóa TK đã phát sinh |
| BR-CAT-02 | Đối tượng (KH, NCC, NV), kho, vật tư, ngân hàng… | CAT | ✔ | | | ✔ | | Mã duy nhất, phát hiện trùng, ngừng sử dụng thay vì xóa |
| BR-GL-01 | Chứng từ nhiều dòng Nợ/Có | GL | ✔ | ✔ | | ✔ | | ΣNợ = ΣCó |
| BR-GL-02 | Quy trình Nháp→Kiểm tra→Gửi duyệt→Duyệt→Ghi sổ→Khóa kỳ | GL | ✔ | ✔ | | | | Chuyển trạng thái hợp lệ duy nhất |
| BR-GL-03 | Không sửa/xóa chứng từ đã ghi sổ; sửa bằng đảo/điều chỉnh | GL | ✔ | ✔ | | | | Bất biến sổ cái |
| BR-GL-04 | Không ghi nhận 2 lần một nghiệp vụ nguồn | GL/XCH | ✔ | ✔ | | ✔ | | Unique (company, source_system, source_id) + idempotency |
| BR-GL-05 | Đối tượng chi tiết bắt buộc khi TK yêu cầu | GL | ✔ | ✔ | ✔ | | | Validation theo `account.requires_partner` |
| BR-GL-06 | Kết chuyển cuối kỳ theo quy tắc có phiên bản | GL/REG | ✔ | ✔ | | | ✔ | Chạy lại không tạo trùng |
| BR-GL-07 | Bảng cân đối số phát sinh | GL/RPT | ✔ | | | | ✔ | Σ dư đầu Nợ = Σ dư đầu Có, tương tự PS và dư cuối |
| BR-CASH-01 | Phiếu thu/chi, tạm ứng/hoàn ứng, kiểm kê quỹ | CASH | ✔ | ✔ | ✔ | ✔ | ✔ | Cảnh báo/chặn quỹ âm theo cấu hình |
| BR-BANK-01 | Lệnh thanh toán với 6 trạng thái tách biệt | BANK | ✔ | ✔ | | ✔ | | Hủy lệnh chưa thực hiện không tạo bút toán |
| BR-BANK-02 | Nhập & đối chiếu sao kê, hàng chờ khi nhiều khả năng khớp | BANK | ✔ | | ✔ | ✔ | | Đối chiếu không sửa chứng từ gốc |
| BR-ARAP-01 | SettlementAllocation thanh toán ↔ nghĩa vụ | ARAP | ✔ | | ✔ | ✔ | | Σ phân bổ = chứng từ thanh toán; không vượt số dư |
| BR-ARAP-02 | Tuổi nợ, xác nhận công nợ, đối trừ | ARAP | ✔ | ✔ | ✔ | | | Chi tiết = sổ cái TK công nợ |
| BR-PUR-01 | Đơn mua→Phiếu nhập→Hóa đơn→Công nợ→Thanh toán, n-n | PUR | ✔ | ✔ | ✔ | ✔ | | Tách ngày HĐ / nhận hàng / hạch toán |
| BR-SAL-01 | Đơn bán→Xuất→Giao→HĐ→Thu tiền | SAL | ✔ | ✔ | ✔ | ✔ | ✔ | Không ghi trùng doanh thu |
| BR-INV-01 | Nhiều kho, quy đổi ĐVT, nhập/xuất/điều chuyển/kiểm kê | INV | ✔ | ✔ | ✔ | ✔ | ✔ | Phương pháp giá xuất theo chính sách đã duyệt |
| BR-INV-02 | Sở hữu vs vị trí; giữ hộ, mượn, cho mượn, trả mượn | INV | ✔ | ✔ | ✔ | ✔ | ✔ | Hàng không thuộc sở hữu không vào giá trị ghi sổ |
| BR-INV-03 | Dăm gỗ: MT, BDMT, độ ẩm, phương pháp quy đổi có phê duyệt | INV | ✔ | | ✔ | ✔ | | Không giả định độ khô chuẩn; không hạch toán chênh lệch BDMT thành CLTG |
| BR-FA-01 | Ghi tăng, khấu hao, thanh lý, đối chiếu TSCĐ↔sổ cái | FA | ✔ | ✔ | ✔ | ✔ | ✔ | Chạy lại khấu hao không trùng |
| BR-TOOL-01 | Phân bổ CCDC nhiều kỳ | TOOL | ✔ | ✔ | ✔ | | ✔ | Idempotent theo (tool, kỳ) |
| BR-COST-01 | Phân bổ, dở dang, giá thành có phiên bản lần tính | COST | ✔ | ✔ | ✔ | | ✔ | Kỳ khóa không bị tính lại âm thầm |
| BR-PAY-01 | Nhập bảng lương JSON, kiểm tổng, sinh bút toán | PAY | ✔ | ✔ | | ✔ | ✔ | Không nhập lại bảng đã ghi sổ; quyền dữ liệu nhạy cảm |
| BR-TAX-01 | Hóa đơn XML/JSON, phát hiện trùng, thay thế/điều chỉnh/hủy | TAX | ✔ | | ✔ | ✔ | ✔ | Không tự lập tờ khai chính thức |
| BR-TAX-02 | Đối chiếu 3 nguồn HĐĐT – sổ – tờ khai | TAX | ✔ | | | | ✔ | Liệt kê từng sai lệch |
| BR-FX-01 | Nguyên tệ, tỷ giá theo loại GD, CLTG thực hiện/đánh giá lại | FX | ✔ | ✔ | ✔ | ✔ | ✔ | Không một công thức cho mọi GD |
| BR-LOAN-01 | Khoản vay, lịch gốc/lãi, tài sản bảo đảm, góp vốn | LOAN | ✔ | ✔ | ✔ | | ✔ | Không tự chuyển lỗ lũy kế thành phải thu/vốn |
| BR-MULTI-01 | Cộng gộp quản trị ≠ hợp nhất pháp lý; loại trừ nội bộ | MULTI | ✔ | | | | ✔ | Ghi nhãn rõ loại báo cáo |
| BR-XCH-01 | Gói JSON có schema_version, package_id, checksum, chữ ký | XCH | ✔ | | | ✔ | | Checksum ≠ xác thực; canonical serialization |
| BR-XCH-02 | Dry-run, preview, duplicate, conflict, commit, audit | XCH | ✔ | | | ✔ | | Không ghi đè chứng từ đã ghi sổ |
| BR-RPT-01 | Report Engine metadata + công thức có phiên bản, drill-down | RPT | ✔ | | | | ✔ | Không tuyên bố đúng pháp luật khi chưa đối chiếu |
| BR-RECON-01 | Nhập dữ liệu phần mềm hiện hành, đối chiếu 5 cấp | RECON | ✔ | | | ✔ | | Không ép số; lưu nguyên nhân sai lệch |
| BR-SEC-01 | RBAC 10 vai trò, 12 nhóm quyền | SEC | ✔ | | | | | Backend enforce |
| BR-NFR-01 | Offline Windows 10/11, EXE/MSI | ALL | ✔ | | | | | Không phụ thuộc cloud |
| BR-NFR-02 | Hiệu năng: 100k chứng từ, 1M dòng, 20 user LAN | ALL | ✔ | | | | | Chỉ công bố khi đã đo |

## 4. Yêu cầu phi chức năng

- **NFR-01 Offline**: mọi nghiệp vụ cốt lõi không gọi mạng ngoài. Cập nhật phần mềm là thao tác tùy chọn, có thể dùng gói cài offline.
- **NFR-02 Toàn vẹn**: ghi sổ trong một transaction ACID; chứng từ chỉ ở trạng thái `Posted` khi các dòng sổ cái đã commit.
- **NFR-03 Chính xác tiền tệ**: `decimal` trong mã; DB lưu số nguyên đã nhân hệ số (SQLite) hoặc `numeric` (PostgreSQL). Xem 02 §5.
- **NFR-04 Kiểm toán**: audit log chuỗi băm; mọi thay đổi cấu hình quan trọng có log.
- **NFR-05 Bảo mật**: mật khẩu băm PBKDF2-SHA256 ≥ 600.000 vòng; API cục bộ chỉ lắng nghe `127.0.0.1` với token phiên; LAN dùng TLS.
- **NFR-06 Hiệu năng**: ngưỡng mục 41 cấu hình được, benchmark tự động; chưa đo thì chưa công bố.
- **NFR-07 Môi trường**: `Development`, `Test`, `Production` tách DB; test không bao giờ dùng file DB production (kiểm tra cứng trong code khởi tạo test).

## 5. Đặc tả chi tiết — Phân hệ Quản trị hệ thống (SYS)

1. **Mục đích**: thiết lập pháp nhân, kỳ kế toán, người dùng, quyền và các tham số nền.
2. **Đối tượng sử dụng**: Administrator, Kế toán trưởng (một phần), Kiểm toán nội bộ (chỉ xem).
3. **Danh mục dữ liệu**: companies, branches, fiscal_years, accounting_periods, users, roles, permissions, user_company_roles, voucher_number_sequences, audit_logs.
4. **Chứng từ phát sinh**: không phát sinh chứng từ kế toán; phát sinh sự kiện audit.
5. **Màn hình**: Danh sách công ty; Hồ sơ công ty; Chi nhánh (cây); Năm tài chính & kỳ; Người dùng; Vai trò & quyền; Đánh số chứng từ; Sao lưu/Phục hồi; Nhật ký hoạt động.
6–7. **Trường dữ liệu & validation** (chính):

| Màn hình | Trường | Kiểu | Bắt buộc | Quy tắc |
|----------|--------|------|----------|---------|
| Công ty | Mã | text(20) | ✔ | Duy nhất toàn hệ thống, `^[A-Z0-9_-]+$` |
| | Tên | text(250) | ✔ | |
| | Mã số thuế | text(14) | | 10 số hoặc 10 số + "-" + 3 số **[CẦN XÁC MINH quy định MST hiện hành]** |
| | Đồng tiền hạch toán | char(3) | ✔ | ISO 4217; thay đổi chỉ khi chưa có ghi sổ hoặc theo quy trình chuyển đổi TT99 |
| | Chế độ kế toán | FK | ✔ | Phiên bản phải `Verified` mới được dùng ở môi trường Production |
| Năm TC | Ngày bắt đầu/kết thúc | date | ✔ | Không chồng lấn; tối đa 15 tháng **[CẦN XÁC MINH Luật Kế toán]** |
| Kỳ | Trạng thái | enum Open/Locked | ✔ | Khóa/mở cần quyền `period.lock`/`period.reopen` |
| Người dùng | Tên đăng nhập | text(50) | ✔ | Duy nhất, không phân biệt hoa thường |
| | Mật khẩu | — | ✔ | ≥ 10 ký tự; chỉ lưu hash |

8. **Quy trình**: Tạo công ty → chọn chế độ kế toán đã xác minh → tạo năm TC → sinh kỳ tháng → nạp hệ thống tài khoản từ template của phiên bản → gán người dùng & vai trò.
9. **Phê duyệt**: đổi chế độ kế toán/đồng tiền hạch toán khi đã có số liệu: bị chặn (trả lỗi `REGIME_CHANGE_BLOCKED`); quy trình chuyển đổi riêng sẽ đặc tả ở GĐ5.
10–11. **Hạch toán / Sổ**: không áp dụng.
12. **Báo cáo**: danh sách người dùng–quyền, nhật ký hoạt động, lịch sử khóa kỳ.
13. **API**: `POST /api/auth/login`, `POST /api/companies`, `POST /api/companies/{id}/fiscal-years`, `POST /api/periods/{id}/lock`, `POST /api/periods/{id}/reopen`, `GET /api/audit`.
14. **JSON**: master-data.schema.json (company, branch).
15. **Phân quyền**: `system.admin`, `period.lock`, `period.reopen`, `audit.view`.
16. **Lỗi**: trùng mã; năm chồng lấn; mở kỳ khi kỳ sau đã khóa (cảnh báo bắt buộc xác nhận); người dùng không có quyền ở công ty.
17. **Unit test**: validation mã, chồng lấn năm, sinh kỳ.
18. **Integration test**: tạo công ty→năm→kỳ→khóa→ghi sổ bị chặn.
19. **Đối chiếu dữ liệu thực**: không áp dụng (cấu hình).
20. **Nghiệm thu**: người dùng công ty A không đọc được dữ liệu công ty B qua API (test tự động); mọi lần khóa/mở kỳ có audit log.

## 6. Đặc tả chi tiết — Kế toán tổng hợp (GL)

1. **Mục đích**: ghi nhận nghiệp vụ tổng hợp, kết chuyển, phân bổ, điều chỉnh, đảo; là điểm duy nhất ghi vào sổ cái (mọi phân hệ khác gọi Posting Engine).
2. **Người dùng**: Kế toán tổng hợp (tạo/sửa nháp/gửi duyệt), Người duyệt/Kế toán trưởng (duyệt, ghi sổ, đảo), Kiểm toán nội bộ (xem).
3. **Danh mục**: accounts, business_partners, cost_centers, voucher types.
4. **Chứng từ**: GJ (chứng từ khác), CLS (kết chuyển), ALC (phân bổ), ADJ (điều chỉnh), REV (đảo), FXR (đánh giá cuối kỳ).
5. **Màn hình**: Danh sách chứng từ (lọc theo kỳ/trạng thái/loại); Form chứng từ (header + lưới định khoản); Hàng chờ duyệt; Sổ nhật ký chung; Sổ cái; Bảng cân đối phát sinh; Khóa kỳ.
6–7. **Trường**:

| Trường | Kiểu | Bắt buộc | Validation |
|--------|------|----------|------------|
| Loại chứng từ | enum | ✔ | |
| Số chứng từ | text(30) | tự sinh | Duy nhất (company, type, fiscal year) |
| Ngày chứng từ | date | ✔ | |
| Ngày hạch toán | date | ✔ | Thuộc kỳ đang mở của công ty |
| Diễn giải | text(500) | ✔ | |
| Dòng: TK Nợ/Có | FK account | ✔ | TK tồn tại, thuộc công ty, `is_postable`, `active` |
| Dòng: số tiền | decimal(19,4) | ✔ | > 0; đúng số chữ số thập phân của đồng tiền |
| Dòng: đối tượng | FK partner | theo TK | Bắt buộc khi `account.requires_partner` |
| Dòng: nguyên tệ, tỷ giá | char(3), decimal(18,6) | khi ngoại tệ | quy đổi = round(nguyên tệ × tỷ giá, độ chính xác đồng tiền hạch toán) |
| Nguồn | source_system, source_id | khi từ phân hệ/JSON | Duy nhất theo công ty |

8. **Quy trình**: Draft → (Validate) → Submitted → Approved → Posted → (Reversed). Trả lại: Submitted → Draft. Hủy: chỉ Draft/Submitted → Cancelled.
9. **Phê duyệt**: người duyệt ≠ người lập (cấu hình `segregation_of_duties`, mặc định bật).
10. **Hạch toán**: từ dòng chứng từ; với chứng từ phân hệ thì từ Posting Rule.
11. **Tác động sổ**: 1 journal_entry + n journal_lines; số dư tính từ journal_lines.
12. **Báo cáo**: Sổ nhật ký chung, Sổ cái, Sổ chi tiết TK, Bảng cân đối số phát sinh.
13. **API**: xem 03 §3.
14. **JSON**: journal-voucher.schema.json.
15. **Quyền**: `voucher.view/create/edit_draft/submit/approve/post/reverse`.
16. **Lỗi**: xem 03 §6 (mã lỗi chuẩn).
17–18. **Test**: xem 06 §3.
19. **Đối chiếu thực**: Cấp 2–3 (06 §5).
20. **Nghiệm thu**: BCĐ phát sinh cân; mọi chứng từ Posted có journal; không API nào sửa được journal; reversal tạo chứng từ mới liên kết.

## 7. Yêu cầu nghiệp vụ trọng yếu các phân hệ còn lại (rút gọn — chi tiết theo giai đoạn)

### 7.1 CASH
Phiếu thu/chi; tạm ứng/hoàn ứng; rút tiền NH về quỹ, nộp tiền vào NH (dùng TK tiền đang chuyển hoặc trực tiếp — **theo chính sách công ty, cấu hình**); kiểm kê và xử lý chênh lệch. Nhiều quỹ, mỗi quỹ một thủ quỹ. Chính sách quỹ âm: `Block` | `Warn`. Nghiệm thu: sổ quỹ = sổ cái TK tiền mặt theo quỹ.

### 7.2 BANK
Trạng thái lệnh: `Created → Approved → SentToBank → ExecutedByBank → Posted → Reconciled` (+ `Cancelled` chỉ trước `ExecutedByBank`). Bút toán chỉ sinh ở `Posted`. Đối chiếu sao kê: điểm khớp theo mã GD, TK NH, số tiền, ngày (± n ngày cấu hình), nội dung, mã tham chiếu; ≥ 2 ứng viên có điểm ngang nhau → `PendingConfirmation`. Chuyển nội bộ dùng cặp chứng từ liên kết, không qua TK doanh thu/chi phí.

### 7.3 ARAP
`receivable_items`/`payable_items` là nghĩa vụ; `settlement_allocations(payment_doc, obligation, amount, fx_amount)` là nguồn sự thật cho số dư từng nghĩa vụ. Ứng trước = nghĩa vụ âm/khoản chưa phân bổ. Bất biến: Σallocation(payment) ≤ payment amount; Σallocation(obligation) ≤ obligation amount trừ khi cờ `allow_overpayment_as_advance`. Tổng chi tiết = sổ cái TK công nợ (reconcile tự động).

### 7.4 PUR / SAL
Quan hệ n-n giữa phiếu nhập/xuất và dòng hóa đơn qua bảng `document_links(from_line, to_line, qty, amount)`. Ba ngày tách biệt: ngày chứng từ (hóa đơn), ngày nhận/giao hàng, ngày hạch toán. Doanh thu ghi nhận theo **chính sách ghi nhận doanh thu được cấu hình** (giao hàng / chuyển giao quyền kiểm soát / khác) — **[CẦN XÁC MINH theo TT99 & chuẩn mực]**.

### 7.5 INV
`stock_movements` lưu cả vị trí (`warehouse_id`) và chủ sở hữu (`owner_partner_id` null = doanh nghiệp). Giá trị ghi sổ chỉ tính cho hàng thuộc sở hữu. Phương pháp tính giá **theo từng kho** (doanh nghiệp xác nhận 14/10/2026): nhập trước xuất trước, bình quân gia quyền tức thời, bình quân gia quyền cuối tháng. Đổi phương pháp chỉ có hiệu lực từ đầu năm tài chính và không hồi tố **[CẦN XÁC MINH căn cứ theo TT99/chuẩn mực hàng tồn kho]**. Hiện thực: `src/Hak.Application/Inventory/StockService.cs`, `src/Hak.Domain/Inventory/Costing.cs`.
**Dăm gỗ**: mỗi lô có `qty_mt`, `moisture_pct`, `qty_bdmt`, `conversion_method_id`. Phương pháp quy đổi là bản ghi có phiên bản, phải được phê duyệt; hệ thống **không có giá trị độ khô mặc định**. Lịch sử đo lường lưu ở `stock_measurements`. Chênh lệch BDMT giữa nhập–xuất được phân loại (hao hụt định mức/ngoài định mức/sai số đo) theo chính sách — không mặc định vào CLTG.

### 7.6 FA / TOOL / COST
Khấu hao & phân bổ là “lần chạy” (`run`) có khóa idempotent (công ty, đối tượng, kỳ); chạy lại kỳ đã ghi sổ phải đảo lần chạy cũ trước. Giá thành lưu `costing_runs` có phiên bản; kỳ khóa chỉ đọc.

### 7.7 PAY
Nhận payroll JSON; kiểm Σgross − Σdeductions = Σnet theo từng nhân viên và tổng; khóa theo (công ty, kỳ lương, source_id). Trường lương chi tiết thuộc quyền `sensitive.view`.

### 7.8 TAX
Hóa đơn: khóa trùng (MST người bán, ký hiệu mẫu, ký hiệu HĐ, số HĐ) **[CẦN XÁC MINH cấu trúc theo quy định HĐĐT hiện hành]**. Quan hệ thay thế/điều chỉnh/hủy là liên kết, không xóa. Đối chiếu 3 nguồn xuất bảng sai lệch: loại, chênh lệch, hướng xử lý. Không sinh tờ khai “chính thức”; chỉ bảng tính dự thảo.

### 7.9 FX
Bảng tỷ giá theo (đồng tiền, loại tỷ giá: mua/bán/chuyển khoản/ghi sổ BQ gia quyền…, ngày, nguồn). Quy tắc tỷ giá theo loại giao dịch là cấu hình trong REG **[CẦN XÁC MINH theo TT99]**.

### 7.10 LOAN
`loan_contracts`, `loan_schedules`, `collaterals` (hồ sơ, không phải bút toán), `capital_contributions`. Không có chức năng tự động bù trừ lỗ lũy kế.

### 7.11 MULTI
Báo cáo cộng gộp gắn nhãn “Báo cáo quản trị – không phải BCTC hợp nhất”. Loại trừ nội bộ dựa trên `intercompany_links`.

## 8. Câu hỏi mở cần chủ doanh nghiệp/kế toán trưởng xác nhận

1. Công ty áp dụng TT 99/2025 hay chế độ DNNVV? Danh sách công ty và năm tài chính.
2. ~~Phương pháp tính giá xuất kho~~ — đã trả lời: FIFO / bình quân tức thời / bình quân cuối tháng, theo kho.
3. ~~Phương pháp quy đổi MT → BDMT~~ — đã trả lời: theo độ khô (BDMT = MT × độ khô % ÷ 100), đo khi nhập và đo ở tàu khi xuất, chứng từ là chứng thư. Còn hỏi: số chữ số làm tròn, giá trị kho theo MT hay BDMT, chênh lệch MT phiếu xuất ↔ chứng thư.
4. Chính sách quỹ âm, tách nhiệm vụ lập–duyệt.
5. Phần mềm kế toán hiện hành và định dạng xuất dữ liệu được (cho RECON).
6. Có cần chi nhánh độc lập (đồng bộ gói JSON) hay chỉ LAN?
