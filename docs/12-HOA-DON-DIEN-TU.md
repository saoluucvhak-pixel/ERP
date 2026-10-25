# 12 — Giải pháp nâng cấp phân hệ Hóa đơn điện tử (HĐĐT) — v0.1 (25/10/2026)

Trạng thái: **ĐÃ CÀI ĐẶT phần không phụ thuộc nhà cung cấp** (25/10/2026, mục 8) — phần ký số / gọi API thật chờ chị chọn nhà cung cấp. Mọi điều khoản pháp lý trong tài liệu là bản em hiểu từ văn bản gốc và **phải được kế toán trưởng đối chiếu** trước khi cấu hình (nguyên tắc dự án: không đưa quy định chưa xác minh vào hệ thống). Chỗ chưa chắc ghi **[CẦN XÁC MINH]**.

Khung pháp lý cần đối chiếu: Nghị định 123/2020/NĐ-CP (hóa đơn, chứng từ; xử lý sai sót ở Điều 19), Thông tư 78/2021/TT-BTC (hướng dẫn; ghi chú: yêu cầu ghi "TT-CP", đúng là **TT-BTC**), và các văn bản sửa đổi, thay thế ban hành sau đó (như Nghị định 70/2025/NĐ-CP, Thông tư 32/2025/TT-BTC **[CẦN XÁC MINH hiệu lực và nội dung]**). Tài liệu viết theo cấu trúc nên khi quy định đổi, chỉ đổi bảng cấu hình ở mục 6.

---

## 0. Hiện trạng (As-Is) của HAK — nền để nâng cấp

| Năng lực | Đã có | Còn thiếu |
|---|---|---|
| Hóa đơn bán / mua trong ERP | `CommercialInvoice` (Normal, Return, Discount, Increase), liên kết phiếu kho / đơn hàng, ghi sổ bất biến, đảo, phê duyệt điều chỉnh, chiết khấu trên hóa đơn và sau bán | Số / ký hiệu hóa đơn **người dùng gõ tay**; chưa có khái niệm hóa đơn thay thế, điều chỉnh thông tin, thông báo sai sót |
| Dữ liệu HĐĐT | `EInvoiceRecord` + `EInvoiceStatusChange`: **nhập tệp** (XML/CSV) từ cổng thuế hoặc nhà cung cấp; trạng thái Original / Replacement / Adjustment / Replaced / Cancelled; khóa trùng theo MST + ký hiệu + số | Không có kênh **phát hành** (ký số, gửi T-VAN), không nhận phản hồi tự động |
| Đối soát | `VatService.ReconcileAsync`: đối chiếu 3 nguồn (HĐĐT nhập vào ↔ sổ sách ↔ tờ khai đã lưu), bảng kê, số đã kê khai | Chưa tự sinh việc xử lý cho từng chênh lệch; chưa đối chiếu trạng thái hiệu lực |
| Chữ ký số | — | Toàn bộ (docs/07 L-04 đ ghi rõ còn hạn chế) |

Kết luận: HAK mạnh ở **hạch toán và đối soát**, yếu ở **phát hành và đồng bộ trạng thái**. Thiết kế dưới đây thêm lớp phát hành mà **không đổi lõi sổ cái**.

---

## 1. Kiến trúc tích hợp và luồng dữ liệu

### 1.1 Nguyên tắc
1. **ERP là nguồn sự thật nghiệp vụ; nhà cung cấp HĐĐT / cơ quan thuế (CQT) là nguồn sự thật pháp lý về trạng thái.** Hai bên không ghi đè nhau mà đối soát (mục 4).
2. **Ghi sổ không chờ mạng:** gửi hóa đơn đi qua hàng đợi (Outbox). Mạng lỗi thì hóa đơn vẫn ở trạng thái đã biết, gửi lại an toàn, không bao giờ phát hành hai lần.
3. **Kết nối qua cổng (adapter):** một giao diện `IEInvoiceProvider` (Issue, Query, Cancel, Download, Webhook). Đổi nhà cung cấp chỉ đổi adapter.
4. **Bất biến:** XML đã ký, PDF thể hiện, băm SHA-256 và phản hồi của CQT là dữ liệu chỉ thêm, không sửa (trigger như các bảng sổ khác).

### 1.2 Sơ đồ (luồng bán hàng; mua hàng là chiều nhận)

```
 BÁN HÀNG (SD)                         KẾ TOÁN (FI/CO)                       HĐĐT / CQT
 ─────────────                         ───────────────                       ──────────
 Đơn bán → Giao hàng ─► Hóa đơn nháp ──(1) Gửi duyệt──► Duyệt (người khác)
                         (ERP: Draft)                         │
                                                              ▼
                                         (2) Lệnh phát hành ghi vào OUTBOX (cùng transaction với trạng thái "Approved→Issuing")
                                                              │   Idempotency-Key = Id hóa đơn ERP
                                                              ▼
                                              Worker gửi ──► Adapter T-VAN ──► Ký số (HSM / token từ xa)
                                                                  │               ──► CQT cấp mã / tiếp nhận
                                                              ◄───┘ (3) Webhook hoặc polling: Accepted(mã CQT, số, ngày ký) | Rejected(lỗi)
                                         (4) Cùng transaction:    lưu XML+PDF+băm, gán Series/Number thật
                                                              ▼
                                         (5) GHI SỔ tự động: Nợ 131 / Có 511, 3331 (+ giá vốn đã ghi ở phiếu xuất)
                                                              ▼
                                         (6) Gửi hóa đơn cho khách (email / cổng tra cứu) ─ nhật ký gửi
                                                              ▼
                                         (7) Đối soát định kỳ ERP ↔ HĐĐT ↔ Tờ khai 01/GTGT (mục 4)
```

**Chọn “phát hành trước, ghi sổ sau”.** Số hóa đơn pháp lý chỉ có sau khi ký, nên bút toán ghi sổ dùng số thật và không phải sửa. Phần xuất kho / giá vốn đã ghi ở phiếu xuất như hiện nay. Nếu doanh nghiệp bắt buộc ghi sổ ngay (đóng kỳ gấp), dùng chế độ “ghi sổ tạm” chỉ khi CQT từ chối thì **đảo** bằng chính cơ chế đảo hiện có.

### 1.3 Mô hình dữ liệu bổ sung (không đụng bảng sổ cái)

| Bảng | Nội dung chính |
|---|---|
| `einvoice_documents` | `invoice_id` (ERP), `provider`, `provider_ref`, `template_code` (ký hiệu mẫu), `series`, `number`, `issue_date`, `signed_at`, `tax_authority_code` (mã CQT), `status`, `relation_type` (Original / Replacement / Adjustment), `related_document_id`, `adjustment_nature` (Tăng / Giảm / Thông tin), `xml_ref`, `xml_sha256`, `pdf_ref`, `row_version` |
| `einvoice_outbox` | `document_id`, `operation` (Issue / Cancel / SendCustomer), `payload_sha256`, `idempotency_key`, `attempts`, `next_attempt_at`, `state` (Pending / Sent / Confirmed / Failed), `last_error` |
| `einvoice_messages` | Nhật ký chỉ-thêm các thông điệp gửi / nhận (nội dung băm, mã phản hồi, thời điểm) |
| `einvoice_error_notices` | Thông báo sai sót Mẫu 04/SS-HĐĐT: danh sách hóa đơn sai, loại sai sót, lý do, trạng thái (Soạn / Đã ký / Đã gửi / CQT tiếp nhận / CQT không tiếp nhận), mã giao dịch |
| `einvoice_status_events` | Mở rộng `EInvoiceStatusChange` hiện có: mọi biến động trạng thái của một hóa đơn (kể cả do người mua / CQT) |

Máy trạng thái hóa đơn phát hành:

```
Draft → Submitted → Approved → Issuing → Issued(có mã) → Posted
                         │          └──► Rejected (lỗi CQT/T-VAN: sửa dữ liệu, phát hành lại với cùng hóa đơn ERP)
                         └──► Cancelled (trước khi phát hành)
Issued → Adjusted (có HĐ điều chỉnh) | Replaced (có HĐ thay thế) | Voided (hủy theo quy định + thông báo 04)
```

### 1.4 Ba kịch bản kỹ thuật tự động

| Kịch bản | Điều kiện nghiệp vụ | Xử lý kỹ thuật |
|---|---|---|
| **Hóa đơn gốc** | Hóa đơn ERP đã duyệt | Outbox → ký → mã CQT → ghi sổ (mục 1.2) |
| **Hóa đơn điều chỉnh** (tăng / giảm / thông tin) | Hóa đơn gốc đã **Issued** và đã ghi sổ; sai về số tiền, thuế suất, hàng hóa, hoặc thỏa thuận giảm / tăng sau bán | Sinh `CommercialInvoice` điều chỉnh (Return / Discount / Increase đã có; **thêm** loại *Thông tin*) kèm `relation_type = Adjustment`, `related_document_id`; XML chứa dòng tham chiếu “Điều chỉnh cho hóa đơn mẫu … ký hiệu … số … ngày …” và cờ tăng / giảm; ghi sổ theo mục 3 |
| **Hóa đơn thay thế** | Hóa đơn gốc sai và quy định cho phép thay thế **[CẦN XÁC MINH các trường hợp]** | Một **thao tác nguyên tử**: (a) tạo hóa đơn mới `relation_type = Replacement`, (b) sau khi CQT chấp nhận, **đảo** hóa đơn gốc (cơ chế đảo hiện có, ngày đảo do người dùng chọn trong kỳ còn mở) và ghi sổ hóa đơn mới; (c) gốc → `Replaced`. Nếu bước (a) bị từ chối, không đụng gốc |
| **Thông báo sai sót 04/SS-HĐĐT** | Sai sót không phải lập hóa đơn mới (ví dụ sai tên / địa chỉ người mua nhưng không sai MST) **[CẦN XÁC MINH danh mục]** | Chỉ tạo `e_invoice_error_notices` đính kèm hóa đơn cần sửa; **không đổi hóa đơn, không phát sinh bút toán** (chị chốt 25/10); theo dõi trạng thái tiếp nhận của CQT; nếu CQT không tiếp nhận thì sinh việc cho kế toán |

Rào chắn dùng chung: không điều chỉnh / thay thế hóa đơn đang `Issuing`; không điều chỉnh khi hóa đơn gốc còn điều chỉnh nháp trái chiều; tổng giảm trừ mỗi dòng gốc không vượt giá trị dòng (đã có ở `RecheckAdjustmentLimitsAsync`).

---

## 2. Quy trình nghiệp vụ

### 2.1 Tạo, duyệt, phát hành trên giao diện ERP

| Bước | Người làm | Màn hình / hành động | Kiểm soát |
|---|---|---|---|
| 1. Lập | Kế toán bán hàng | Hóa đơn bán → từ phiếu giao hàng (giữ nguyên) | Đối chiếu số lượng, giá, chiết khấu, thuế suất; kiểm tra MST người mua với danh mục |
| 2. Gửi duyệt | Người lập | Nút Gửi duyệt | `rowVersion` bắt buộc |
| 3. Duyệt | Kế toán trưởng / người được ủy quyền | Duyệt; công tắc “lập ≠ duyệt” theo công ty (đã có) | Người duyệt khác người lập |
| 4. Phát hành | Hệ thống (tự động sau duyệt) hoặc người có quyền `einvoice.issue` bấm | Hàng đợi hiển thị trạng thái từng hóa đơn; nút **Phát hành lại an toàn** khi mạng lỗi | Ký số bằng khóa của đơn vị trên nhà cung cấp; chữ ký không nằm trong ERP |
| 5. Ghi sổ | Hệ thống | Tự động khi CQT chấp nhận | Cùng transaction với lưu XML |
| 6. Gửi khách | Hệ thống / kế toán | Email + liên kết tra cứu | Nhật ký gửi, gửi lại |
| 7. Xử lý ngoại lệ | Kế toán | Màn “Việc cần làm” (Worklist): hóa đơn bị từ chối, thông báo sai sót chờ ký, hóa đơn chờ CQT quá N giờ | Hạn xử lý, người phụ trách |

Quyền mới (thêm vào danh mục quyền, backend kiểm tra): `einvoice.issue`, `einvoice.cancel`, `einvoice.error_notice`, `einvoice.configure` (chỉ quản trị).

### 2.2 Tự sinh chứng từ đối ứng

| Sự kiện từ CQT / nhà cung cấp / thỏa thuận | ERP tự làm | Người quyết |
|---|---|---|
| Khách trả hàng, giảm giá, chiết khấu sau bán (Credit Memo) | Sinh hóa đơn điều chỉnh giảm **nháp**, trỏ đúng dòng hóa đơn gốc; sau khi phát hành thì ghi sổ (mục 3.1) | Duyệt hóa đơn điều chỉnh (kể cả phê duyệt điều chỉnh bán hàng nếu công ty bật) |
| Điều chỉnh tăng (Debit Memo) | Sinh hóa đơn điều chỉnh tăng nháp | Duyệt |
| CQT từ chối hóa đơn | Hóa đơn → `Rejected`, **chưa ghi sổ**; sinh việc sửa; phát hành lại bằng chính hóa đơn ERP đó | Kế toán |
| Hóa đơn thay thế được chấp nhận | Đảo gốc + ghi sổ mới (mục 1.4) | Duyệt thay thế |
| Nhà cung cấp phát hành điều chỉnh / thay thế cho hóa đơn mua (nhận qua nhập tệp hoặc API) | So khớp theo MST + ký hiệu + số của hóa đơn gốc; gợi ý bút toán điều chỉnh mua (giảm tồn kho / giá vốn, giảm thuế đầu vào, giảm công nợ) hoặc đảo gốc + ghi mới; **hóa đơn nháp để người có quyền duyệt** | Kế toán mua hàng |
| Thông báo sai sót 04/SS-HĐĐT được CQT tiếp nhận / không tiếp nhận | Cập nhật trạng thái; không tiếp nhận → việc xử lý lại | Kế toán |

Công nợ đồng bộ nhờ cơ chế sẵn có: hóa đơn điều chỉnh giảm bù trừ khoản phải thu của hóa đơn gốc còn mở; nếu đã thu hết thì để lại khoản có chưa phân bổ cho khách.

---

## 3. Hạch toán tự động

Mã tài khoản dưới đây chỉ để minh họa (Thông tư 200); HAK dùng **vai trò tài khoản** do công ty ánh xạ **[CẦN XÁC MINH mã khi áp dụng Thông tư 99]**.

### 3.1 Bên bán

| Tình huống | Định khoản | Ghi chú |
|---|---|---|
| Phát hành hóa đơn gốc | Nợ 131 / Có 511, Có 3331 | Giá vốn đã ghi ở phiếu xuất: Nợ 632 / Có 156 |
| Điều chỉnh giảm — hàng trả lại | Nợ 5212 (hoặc 511), Nợ 3331 / Có 131; đồng thời nhập kho: Nợ 156 / Có 632 theo **giá vốn gốc** của phiếu xuất | Dăm gỗ: BDMT trả lại theo tỷ lệ dòng gốc (đã chốt) |
| Điều chỉnh giảm — chiết khấu thương mại / giảm giá sau bán | Nợ 5211 hoặc 5213, Nợ 3331 / Có 131 | Phân biệt `DiscountKind`; cuối kỳ kết chuyển Nợ 511 / Có 521 |
| Điều chỉnh tăng | Nợ 131 / Có 511, Có 3331 | Không đụng giá vốn trừ khi kèm hàng |
| Điều chỉnh thông tin | Không bút toán | Chỉ lưu liên kết và XML |
| Thay thế | Đảo gốc (Nợ 511, 3331 / Có 131) + ghi hóa đơn mới | Hai bút toán độc lập, mỗi cái đúng ngày của nó; giá vốn không đảo trừ khi dòng hàng đổi |
| Thông báo 04/SS | Không bút toán | |

### 3.2 Bên mua

| Tình huống | Định khoản |
|---|---|
| Hóa đơn gốc | Nợ 156 / 152 hoặc chi phí, Nợ 1331 / Có 331 (qua 3388 hàng về chưa có hóa đơn nếu nhập trước) |
| Điều chỉnh giảm (giảm giá, chiết khấu, trả hàng) | Nợ 331 / Có 156 (phần còn tồn), Có 632 (phần đã xuất), Có 1331 |
| Điều chỉnh tăng | Nợ 156 (phần còn tồn), Nợ 632 (phần đã xuất), Nợ 1331 / Có 331 |
| Thay thế | Đảo hóa đơn gốc (kể cả giá trị kho / giá vốn đã chia) rồi ghi hóa đơn mới; chặn đảo nếu làm lệch giá thành đã chốt (đã chốt 25/10) |

### 3.3 Xử lý kỳ, thuế và khóa sổ

1. **Ngày hạch toán của bút toán điều chỉnh** là ngày người dùng chọn trong kỳ còn mở; không bao giờ sửa bút toán kỳ đã khóa (đúng nguyên tắc sổ bất biến).
2. **Kê khai thuế:** sai sót làm thay đổi thuế đã kê khai ở kỳ trước → hệ thống gắn cờ “cần kê khai điều chỉnh / bổ sung” cho kỳ đó và liệt kê hóa đơn gây ra chênh lệch; việc lập và nộp tờ khai bổ sung do kế toán thuế quyết định **[CẦN XÁC MINH cách kê khai điều chỉnh với HĐĐT điều chỉnh / thay thế theo quy định hiện hành]**.
3. **Chênh lệch làm tròn** giữa XML và sổ: không cho phép; kiểm tra tổng tiền từng thuế suất trước khi gửi (đã có logic tách theo thuế suất ở `EInvoiceRateLine`).

---

## 4. Đối soát tự động ba nguồn

Nguồn A = **ERP** (hóa đơn đã ghi sổ + sổ cái). Nguồn B = **hệ thống HĐĐT** (dữ liệu nhà cung cấp / cổng tra cứu CQT). Nguồn C = **tờ khai 01/GTGT** (số đã lưu / đã nộp).

Khóa ghép: MST người bán + ký hiệu mẫu + ký hiệu + số (+ ngày lập để phát hiện trùng khác kỳ). Đây là khóa duy nhất đã dùng ở `EInvoiceRecord`.

| Lớp đối soát | Quy tắc | Kết quả mong đợi |
|---|---|---|
| A ↔ B (từng hóa đơn) | Cùng khóa; so tiền hàng, thuế, tổng, thuế suất từng nhóm, ngày, trạng thái hiệu lực | Khớp hoặc rơi vào nhóm ngoại lệ |
| A ↔ C (theo kỳ) | Tổng thuế đầu ra / đầu vào theo sổ, theo kỳ kê khai, so với chỉ tiêu tương ứng trên tờ khai | Chênh lệch ≤ ngưỡng cấu hình, còn lại giải thích được bằng danh sách hóa đơn |
| B ↔ C | Tổng theo dữ liệu HĐĐT trong kỳ so với tờ khai | Phát hiện hóa đơn có trong HĐĐT chưa kê khai và ngược lại |

Nhóm ngoại lệ và hành động tự động:

| Mã | Mô tả | Hành động hệ thống |
|---|---|---|
| X1 | Có ở B, thiếu ở A | Gợi ý lập hóa đơn mua / bán từ dữ liệu B, nháp |
| X2 | Có ở A đã ghi sổ, thiếu ở B | Báo “đã ghi sổ nhưng chưa phát hành / không tìm thấy”; chặn đóng kỳ nếu quá hạn |
| X3 | Lệch tiền, thuế, thuế suất | Hiển thị chênh lệch từng dòng; không tự sửa số |
| X4 | B báo Replaced / Cancelled nhưng A còn hiệu lực | Sinh đề xuất đảo / thay thế bút toán ở A |
| X5 | Hóa đơn thuộc kỳ khác với kỳ kê khai | Cảnh báo kỳ; gắn cờ kê khai điều chỉnh |
| X6 | Trùng khóa | Chặn, giữ bản đầu |
| X7 | Điều chỉnh / thay thế ở B không tìm thấy hóa đơn gốc | Việc xử lý thủ công |
| X8 | A ↔ C lệch sau khi đã giải thích X1–X7 | Báo kế toán trưởng, chặn đóng kỳ thuế |

Kết quả đối soát là **dữ liệu dẫn xuất**, không có số dư riêng nên không thể lệch sổ; mỗi lần chạy lưu dấu thời gian, phạm vi và người chạy. Bước tự động chạy hằng ngày (so A–B) và khi đóng kỳ thuế (A–B–C); tích hợp vào bảng kiểm đóng kỳ sẵn có (mục `VAT_OFFSET` hiện tại) như một mục “Đối soát HĐĐT”.

---

## 5. Lộ trình triển khai

| Giai đoạn | Việc chính | Sản phẩm bàn giao | Thời lượng gợi ý |
|---|---|---|---|
| 1. Khảo sát (As-Is) | Kiểm kê mẫu hóa đơn, ký hiệu, luồng phát hành hiện tại; chọn nhà cung cấp / T-VAN; phân loại các ca điều chỉnh thực tế 12 tháng qua; rà quy định hiện hành | Báo cáo As-Is, danh sách ca sai sót điển hình, quyết định nhà cung cấp | 2–3 tuần |
| 2. Thiết kế (To-Be) | Chốt luồng, máy trạng thái, quyền, danh mục sai sót cho phép điều chỉnh / thay thế / thông báo 04; hợp đồng API; thiết kế đối soát | Tài liệu này bản 1.0, ma trận RACI, danh mục ca kiểm thử | 2 tuần |
| 3. Phát triển và cấu hình | Adapter nhà cung cấp, outbox, màn hình phát hành và thông báo sai sót, hóa đơn thay thế, đối soát tự động, quyền, nhật ký; cấu hình ký hiệu mẫu và vai trò tài khoản | Bản phát hành thử, tài liệu vận hành | 6–8 tuần |
| 4. Kiểm thử SIT / UAT | SIT với môi trường thử của nhà cung cấp và CQT; UAT bằng dữ liệu thật đã che; diễn tập đóng kỳ và kê khai | Báo cáo kiểm thử, danh sách lỗi đã đóng, biên bản nghiệm thu | 3–4 tuần |
| 5. Đào tạo và Go-live | Đào tạo theo vai trò (lập, duyệt, kế toán thuế, quản trị); cutover số hóa đơn; chạy song song một kỳ; hỗ trợ sau go-live | Hướng dẫn người dùng, kế hoạch cutover, báo cáo sau go-live 30 ngày | 2–3 tuần |

**Ma trận kiểm thử tối thiểu (SIT/UAT):**

1. Phát hành gốc thành công; CQT từ chối; mất mạng giữa chừng và gửi lại không phát hành trùng; hai người bấm đồng thời.
2. Điều chỉnh giảm: trả hàng một phần, giảm giá, chiết khấu theo doanh số; đảo điều chỉnh; vượt giá trị dòng gốc bị chặn.
3. Điều chỉnh tăng; điều chỉnh thông tin không bút toán.
4. Thay thế: thành công, bị từ chối giữa chừng, gốc ở kỳ đã khóa.
5. Thông báo 04: CQT tiếp nhận / không tiếp nhận.
6. Bên mua: nhận điều chỉnh và thay thế từ nhà cung cấp; đối chiếu công nợ, tồn kho, giá vốn, thuế đầu vào.
7. Đối soát: đủ X1–X8; số liệu A–C khớp sau khi xử lý.
8. Mất quyền giữa chừng; sai phiên bản (`rowVersion`); khôi phục từ sao lưu có outbox đang chờ.
9. Hiệu năng: phát hành hàng loạt 1.000 hóa đơn/giờ không vượt giới hạn của nhà cung cấp (xếp hàng, giãn cách).

**Cutover:** chọn ranh giới là đầu kỳ thuế; dừng gõ tay số hóa đơn từ ngày go-live; hóa đơn nháp cũ còn dang dở phải phát hành hoặc hủy trước; nhập sẵn dải số / ký hiệu mẫu đã thông báo; chạy song song đối soát một kỳ (tay và tự động) rồi mới tắt quy trình cũ.

---

## 6. Rủi ro và phương án dự phòng

| # | Loại | Rủi ro | Mức | Giảm thiểu / dự phòng |
|---|---|---|---|---|
| R1 | Kỹ thuật | Nhà cung cấp ngừng dịch vụ / lỗi kéo dài | Cao | Adapter chuẩn hóa; xuất hóa đơn nháp dạng tệp để phát hành bằng cổng nhà cung cấp; giữ bản đối soát nhập tệp hiện có |
| R2 | Kỹ thuật | Phát hành trùng do gửi lại | Cao | Idempotency key = Id hóa đơn; kiểm tra trạng thái trước khi gửi lại; số hóa đơn do nhà cung cấp cấp |
| R3 | Kỹ thuật | Webhook mất / đến sai thứ tự | Trung bình | Polling bù; sự kiện có số thứ tự; áp dụng idempotent |
| R4 | Kỹ thuật | Lệch số tiền giữa ERP và XML (làm tròn) | Trung bình | Một công thức làm tròn duy nhất dùng chung (`MoneyMath`); kiểm tra tổng trước khi ký |
| R5 | Kỹ thuật | Lộ khóa ký / thông tin xác thực | Cao | Khóa ký ở nhà cung cấp / HSM; bí mật cấu hình mã hóa; không ghi vào bảng idempotency và bản sao lưu |
| R6 | Thuế | Áp sai trường hợp điều chỉnh / thay thế / thông báo 04 | Cao | Danh mục ca do kế toán trưởng xác minh, cấu hình được; hệ thống chặn ca không có trong danh mục |
| R7 | Thuế | Hóa đơn điều chỉnh sai kỳ kê khai | Cao | Cờ kỳ (X5); kiểm kê khai bổ sung trước khi đóng kỳ thuế |
| R8 | Thuế | Quy định thay đổi (kể cả văn bản mới thay thế) | Trung bình | Quy tắc nằm ở bảng cấu hình có hiệu lực theo ngày; sổ đăng ký pháp lý `docs/07` cập nhật mỗi lần đổi |
| R9 | Vận hành | Người dùng sửa dữ liệu sau phát hành | Trung bình | Hóa đơn đã phát hành bất biến; chỉ điều chỉnh / thay thế |
| R10 | Vận hành | Mất kết nối đúng hạn kê khai | Trung bình | Cảnh báo trước hạn; quy trình phát hành thủ công có kiểm soát |
| R11 | Dữ liệu | Danh mục MST / địa chỉ khách sai gây từ chối hàng loạt | Trung bình | Kiểm tra MST khi lập hóa đơn; báo cáo rà soát danh mục trước go-live |

---

## 7. Việc cần chị quyết (không chặn các việc khác)

1. Chọn nhà cung cấp HĐĐT / T-VAN và hình thức kết nối (API / SFTP); chưa chọn thì em dựng sẵn adapter giả lập để kiểm thử toàn luồng.
2. Danh mục sai sót nào được điều chỉnh, thay thế, hay chỉ cần thông báo 04 (kế toán trưởng xác minh).
3. Chế độ ghi sổ: “phát hành trước, ghi sổ sau” (đề xuất) hay cho ghi sổ tạm.
4. Phân quyền người phát hành và người ký thông báo sai sót.

Đề xuất làm trước (nhỏ, không phụ thuộc nhà cung cấp): bảng `einvoice_documents`, máy trạng thái, loại hóa đơn *điều chỉnh thông tin* và *thay thế*, thông báo sai sót, đối soát X1–X8 trên dữ liệu đã nhập. Phần ký số và gọi API làm sau khi chọn nhà cung cấp.


---

## 8. Đã cài đặt trong HAK (25/10/2026)

| Hạng mục thiết kế | Cài đặt | Chỗ xem |
|---|---|---|
| Cổng nhà cung cấp (`IEInvoiceProvider`) | `NullEInvoiceProvider` (mặc định, KHÔNG phát hành được), `SimulatedEInvoiceProvider` (chỉ Development / Test; chặn ở Production), chống trùng theo khóa như nhà cung cấp thật. Cấu hình `Hak:EInvoice:Provider` = `None` hoặc `Simulated`, `Hak:EInvoice:TemplateCode` | `src/Hak.Application/EInvoice/EInvoiceProvider.cs`, `Program.cs` |
| Dữ liệu | `einvoice_documents`, `einvoice_outbox`, `einvoice_messages` (chỉ-thêm), `einvoice_error_notices` + dòng; `commercial_invoices.replaces_invoice_id`; migration `Phase19EInvoiceIssue` (SQLite + PostgreSQL) | `src/Hak.Domain/Tax/EInvoiceIssue.cs` |
| Máy trạng thái | `Issuing → Issued hoặc Rejected`, `Issued → Replaced hoặc Cancelled`; hóa đơn đã Issued bất biến (interceptor: số, ký hiệu, ngày ký, mã CQT, XML, băm) | `LedgerImmutabilityInterceptor.cs` |
| Phát hành gốc / điều chỉnh / thay thế | `EInvoiceIssueService`: hàng đợi cùng giao dịch → gọi nhà cung cấp NGOÀI giao dịch → giao dịch A lưu kết quả (không mất) → giao dịch B ghi sổ bằng số thật; lỗi ghi sổ lưu ở `PostError`, **Phát hành lại** sẽ ghi sổ lại. Lỗi mạng giữ trong hàng đợi, gửi lại cùng khóa. Từ chối giữ nguyên hóa đơn nháp, phát hành lại dùng khóa mới | `EInvoiceIssueService.cs` |
| Hóa đơn chờ phát hành | `InvoiceInput.AwaitIssue`: ký hiệu tạm `CHO-PHAT-HANH`, **không ghi sổ được bằng số tạm** (chặn đi vòng) | `InvoiceService.cs` |
| Thay thế | `CreateReplacementDraftAsync` sao chép dòng + thay đổi giá / thuế / chiết khấu / đối tượng; `ReplacesInvoiceId` không bị tính trùng số lượng phiếu kho; khi cơ quan thuế chấp nhận, `PostAsync` **đảo hóa đơn gốc rồi ghi hóa đơn mới trong cùng giao dịch**; bị từ chối thì gốc và sổ không đổi | cùng trên + test `EInvoice_Replacement…` |
| Điều chỉnh thông tin | Chỉ phát hành văn bản liên kết hóa đơn gốc, không bút toán | `AdjustInformationAsync` |
| Hóa đơn phát hành ngoài HAK | `RegisterExternalAsync` ghi nhận làm gốc cho điều chỉnh / thay thế | cùng trên |
| Thông báo sai sót 04/SS | `EInvoiceNoticeService`: số `TBSS` theo bộ mã, gửi qua nhà cung cấp; thông báo CHỈ ĐÍNH KÈM các hóa đơn cần sửa (chị chốt 25/10), **không đổi hóa đơn và không đụng sổ** — kể cả loại Hủy được tiếp nhận (hóa đơn vẫn Issued; muốn hủy/đảo thì dùng thay thế hoặc đảo hóa đơn) | `EInvoiceNoticeService.cs` |
| Đối soát | Hóa đơn gốc / thay thế được chấp nhận tự điền nguồn 1 (`EInvoiceRecord`), gốc bị thay → trạng thái Replaced; dùng lại `VatService.ReconcileAsync` (đã có X1–X5, X8). Mục bảng kiểm đóng kỳ **EINVOICE_PENDING** (chặn khóa kỳ khi còn hóa đơn chờ / từ chối / chưa ghi sổ) | `PeriodCloseChecklistService.cs` |
| Giao diện | Màn **Hóa đơn điện tử** (Bán hàng): hàng đợi, phát hành lại, điều chỉnh thông tin, thông báo sai sót; màn Hóa đơn: ô “Lấy số từ nhà cung cấp”, nút Phát hành HĐĐT, Thay thế | `web/src/Einvoice.tsx`, `Trade.tsx` |
| Quyền | `einvoice.issue` (kế toán trưởng, kế toán tổng hợp), `einvoice.notice` (kế toán trưởng) | `Security.cs`, `PermissionCatalog.cs` |

**Bổ sung cùng ngày (đợt 2):** trigger bất biến ở cả SQLite và PostgreSQL (migration `Phase20EInvoiceTriggers`); dịch vụ nền `EInvoiceWorker` gửi lại lệnh đang chờ theo lịch lùi (`Hak:EInvoice:WorkerSeconds`, mặc định 60 giây, 0 = tắt; ghi sổ kết quả bằng quyền hiện tại của người đã yêu cầu); hóa đơn điều chỉnh được ghi vào dữ liệu đối soát và được ghép với sổ (sổ ghi số âm, HĐĐT số dương); **hóa đơn mua**: `EInvoiceInboundService` liệt kê điều chỉnh / thay thế của nhà cung cấp chưa xử lý (điều chỉnh chưa ghi sổ, thay thế chưa ghi sổ, hóa đơn gốc bị thay còn hiệu lực, thiếu hóa đơn gốc) và tạo sẵn hóa đơn điều chỉnh NHÁP mang ký hiệu + số của nhà cung cấp (chia theo dòng gốc, thuế phải khớp; hàng trả lại không tạo tự động) — màn Mua hàng › HĐĐT nhà cung cấp.

**Chưa làm (cần quyết định hoặc nhà cung cấp):** adapter nhà cung cấp thật và ký số; webhook / polling trạng thái từ cơ quan thuế; lược đồ XML chính thức (hiện là cấu trúc nội bộ, nhà cung cấp chuyển đổi) [CẦN XÁC MINH]; danh mục ca được phép điều chỉnh / thay thế / thông báo 04 theo quy định (hiện không chặn theo ca, kế toán trưởng quyết); thay thế hóa đơn MUA do HAK tự thực hiện (hiện chỉ gợi ý, người dùng đảo gốc và ghi hóa đơn mới).
