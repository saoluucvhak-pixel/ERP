# 13 — Thiết kế nghiệp vụ "Chiết khấu thanh toán" (Cash / Prompt-Payment Discount)

Trạng thái: **bản thiết kế 25/10/2026**, viết theo prompt của chị; đối chiếu từng mục với phần mềm HAK đang có (mục 5, 8, 9). Mọi kết luận pháp lý ghi **[CẦN XÁC MINH]** là bản em hiểu, **chưa tra được văn bản chính thức hiện hành trong phiên làm việc** (không có kênh tra cứu pháp luật) — kế toán trưởng phải đối chiếu trước khi cấu hình (nguyên tắc dự án: không đưa quy định chưa xác minh vào hệ thống).

## 1. Tóm tắt phương án đề xuất
- Chiết khấu thanh toán (CKTT) là **ưu đãi theo thời điểm trả tiền**, không đổi giá hàng: bên bán ghi **chi phí tài chính**, bên mua ghi **thu nhập tài chính**; **không đổi doanh thu / giá mua / thuế GTGT trên hóa đơn** và không lập hóa đơn điều chỉnh. (Chị chốt 25/10: CKTT không xuất hóa đơn nên không ghép với hóa đơn; chỉ là một dòng trong bút toán thanh toán riêng.)
- Mô hình HAK: điều khoản (P%, N ngày) ghi trên hóa đơn hoặc kế thừa từ đối tượng → hệ thống **liệt kê khoản đủ điều kiện và tính CKTT dự kiến** (chưa hạch toán) → người lập **chọn tài khoản tiền + ngày thanh toán** → sinh **chứng từ nháp** bù trừ công nợ + dòng CKTT → duyệt → ghi sổ; ghi sổ **kiểm tra lại** hạn và số tiền. Ngoài ra có chế độ **độc lập, không theo điều khoản** (chọn đối tượng + khoản công nợ mở + số chiết khấu).
- Số tiền đầu vào của thanh toán một phần được định nghĩa là **phần công nợ được tất toán** (không phải tiền thực chuyển); tiền thực chuyển = công nợ tất toán − CKTT.

## 2. Căn cứ và giả định
**Căn cứ cần đối chiếu [CẦN XÁC MINH tại ngày áp dụng]:**

| Chủ đề | Văn bản em hiểu là liên quan | Lưu ý |
|---|---|---|
| Chế độ kế toán | Thông tư 99/2025/TT-BTC (chị quyết định áp dụng từ 01/01/2026; bản gốc ở `legal/TT99-2025`); Thông tư 200/2014/TT-BTC chỉ còn là tham chiếu cho kỳ trước / mẫu gợi ý | Không mặc định TT200 còn áp dụng; chị là người xác minh |
| TK ghi nhận | Chi phí tài chính (TK 635) bên bán; doanh thu hoạt động tài chính (TK 515) bên mua — là cách hiểu theo hệ thống TK quen dùng; HAK **không hard-code**, dùng vai trò `PAYMENT_DISCOUNT_EXPENSE` / `PAYMENT_DISCOUNT_INCOME` do công ty ánh xạ | Mã cụ thể theo TT99 chị xác nhận khi ánh xạ |
| Thuế GTGT | CKTT không làm thay đổi giá tính thuế trên hóa đơn; không lập hóa đơn điều chỉnh | Luật GTGT và văn bản hướng dẫn hiện hành |
| Hóa đơn | Nghị định 123/2020/NĐ-CP và văn bản sửa đổi; CKTT không phải điều chỉnh hóa đơn | Xem docs/12 |
| Thuế TNDN | Khoản chi/thu tài chính; điều kiện hồ sơ: thỏa thuận (hợp đồng/phụ lục/chính sách), chứng từ thanh toán qua ngân hàng | Đối chiếu văn bản hướng dẫn thuế TNDN hiện hành |
| IFRS | Không áp dụng: nếu cần, đánh giá theo "đối giá biến đổi" (IFRS 15) / giá trị hàng mua — là phần đối chiếu riêng, **không** dùng TK 635/515 máy móc | Ngoài phạm vi HAK (chế độ Việt Nam) |

**Giả định (thay cho thông tin chưa có):** ngành thương mại/sản xuất; đồng tiền hạch toán VND; phạm vi AR và AP; VAT khấu trừ; cơ sở tính CKTT = **giá trị hàng chưa thuế sau chiết khấu thương mại** (theo hợp đồng); ngày cơ sở = ngày hóa đơn; ERP = HAK (đã xác định, không cần giả định mã giao dịch của hệ khác); chính sách hiện tại: chưa có / theo từng hóa đơn.

## 3. Bản chất nghiệp vụ và chính sách
### 3.1 Định nghĩa và điều khoản
- CKTT: bên bán giảm số tiền phải thu nếu bên mua trả **đủ trong thời hạn ngắn hơn hạn thanh toán thông thường**.
- **2/10, net 30**: giảm 2% nếu trả trong 10 ngày, hạn thanh toán 30 ngày. **1/15, net 45**: 1% trong 15 ngày, hạn 45 ngày.
- **Quy ước đếm ngày (HAK):** ngày cơ sở (ngày hóa đơn) là **ngày 0**; hạn hưởng = ngày cơ sở + D ngày (ngày này **còn được hưởng**, ngày sau thì mất); đáo hạn = ngày cơ sở + N ngày. Ngày lịch, không loại ngày nghỉ. Ví dụ hóa đơn 01/10/2026, 2/10 net 30: hạn hưởng **11/10/2026**, đáo hạn **31/10/2026**.

### 3.2 So sánh
| Tiêu chí | Chiết khấu thanh toán | Chiết khấu thương mại |
|---|---|---|
| Bản chất | Ưu đãi để thu/trả tiền sớm (giá của việc trả tiền) | Giảm giá hàng theo lượng/doanh số/thỏa thuận (giá của hàng) |
| Điều kiện | Thời điểm (và đủ số) thanh toán | Mua đủ lượng/doanh số hoặc thỏa thuận giá |
| Thời điểm ghi nhận | Khi thanh toán thực tế đủ điều kiện (không trích trước) | Khi bán (trên hóa đơn) hoặc khi đạt ngưỡng (điều chỉnh sau bán) |
| Tác động | Chi phí/thu nhập tài chính; **không** đổi doanh thu, giá mua | Giảm doanh thu bán / giảm giá trị hàng mua (tồn kho, giá vốn) |
| Chứng từ / hóa đơn | Chứng từ thanh toán + thỏa thuận; không hóa đơn | Trên hóa đơn hoặc hóa đơn điều chỉnh giảm / hạch toán điều chỉnh giảm |
| VAT | Không đổi | Giảm thuế tương ứng khi điều chỉnh bằng hóa đơn [CẦN XÁC MINH khi chỉ hạch toán điều chỉnh] |
| TNDN | Chi phí/thu nhập tài chính; cần thỏa thuận + chứng từ thanh toán | Giảm doanh thu/giá vốn theo hóa đơn/chứng từ [CẦN XÁC MINH] |

Phân biệt thêm: **giảm giá hàng bán** (chất lượng/quy cách, có hóa đơn điều chỉnh), **hàng bán bị trả lại** (có nhập kho + hóa đơn điều chỉnh), **phí ngân hàng** (chi phí dịch vụ, không phải CKTT), **xóa nợ** (nợ khó đòi, có duyệt, dùng dự phòng/chi phí), **chênh lệch thanh toán nhỏ** (làm tròn/dung sai — chỉ xử lý khi có chính sách dung sai được duyệt, không tự coi là CKTT).

### 3.3 Bộ quy tắc chính sách
| Quy tắc | Đề xuất | Phải ghi trong hợp đồng/thỏa thuận? | HAK hiện có |
|---|---|---|---|
| Đối tượng | Theo từng đối tượng (mặc định điền sẵn từ danh mục) và ghi đè trên hóa đơn | Có (đối tác phải biết điều khoản) | Có (đối tượng + hóa đơn) |
| Tỷ lệ/bậc | Một bậc (P%, D ngày). Nhiều bậc (vd 3/5, 2/10) là mở rộng | Có | Một bậc; **nhiều bậc chưa có** |
| Cơ sở tính | Tiền hàng chưa thuế sau CKTM; loại trừ vận chuyển/phụ phí nếu hợp đồng nói | Có | Chỉ giá trị hàng chưa thuế |
| Ngày cơ sở | Ngày hóa đơn (chọn khác: ngày giao/nghiệm thu/nhận hóa đơn) | Có | Chỉ ngày hóa đơn |
| Ngày xét điều kiện | **Ngày tiền về tài khoản (ghi Có ngân hàng)**; không dùng ngày lập lệnh | Có | Ngày hạch toán của chứng từ — người lập phải nhập đúng ngày ghi Có |
| Ngày nghỉ | Ngày lịch; hạn rơi vào ngày nghỉ có thể dời nếu hợp đồng nói | Có | Ngày lịch, không dời |
| Thanh toán một phần | Hưởng CKTT **theo tỷ lệ phần tất toán trong hạn**; phần còn lại ngoài hạn không hưởng | Có | Có (tỷ lệ theo phần mở) |
| Điều chỉnh/trả hàng/giảm nợ | Cơ sở tính theo phần công nợ còn mở (đã trừ điều chỉnh) | Nên ghi | Có (theo phần còn mở) |
| Làm tròn | Làm tròn đến đồng, **một lần trên toàn khoản**; phần lẻ không phân bổ lại | Nên ghi | Làm tròn đồng |
| Trần/dung sai | Không vượt công nợ mở; dung sai ngày/số tiền chỉ khi có chính sách duyệt | Có | Không có dung sai |
| Ngoại lệ (quá hạn vẫn cho) | Người có thẩm quyền duyệt, ghi lý do | — | Qua chế độ độc lập + duyệt chứng từ |
| Thu hồi khi hủy thanh toán | Đảo chứng từ → công nợ mở lại, quyền hưởng tính lại theo ngày thanh toán mới | — | Có (đảo chứng từ) |

## 4. Hạch toán (minh họa; tài khoản do công ty ánh xạ vai trò)
Ký hiệu: bên bán dùng 131/635/112; bên mua 331/515/112 — **theo cách hiểu TK quen dùng, [CẦN XÁC MINH mã theo TT99]**. Hóa đơn: hàng 100.000.000 + VAT 10% (giả định tính toán, không phải kết luận thuế suất) = 110.000.000; ngày 01/10/2026; 2/10 net 30; hạn hưởng 11/10; CKTT = 2% × 100.000.000 = **2.000.000**.

**Ghi nhận bán/mua ban đầu** — Bán: Nợ 131 110.000.000 / Có 511 100.000.000, Có 3331 10.000.000. Mua: Nợ 156 (hoặc 152…) 100.000.000, Nợ 1331 10.000.000 / Có 331 110.000.000. **CKTT không ghi lúc này** (quyền hưởng phụ thuộc việc trả đúng hạn; không trích trước).

| # | Sự kiện | Điều kiện | Bút toán | Số tiền | Chứng từ | Tác động công nợ |
|---|---|---|---|---|---|---|
| A-bán | Nhận đủ trong hạn | Tiền về ≤ 11/10, tất toán 110.000.000 | Nợ 112 108.000.000; Nợ 635 2.000.000 / Có 131 110.000.000 | 108.000.000 + 2.000.000 | Giấy báo Có + chứng từ CKTT | 131 về 0 |
| A-mua | Trả đủ trong hạn | Như trên | Nợ 331 110.000.000 / Có 112 108.000.000; Có 515 2.000.000 | như trên | Ủy nhiệm chi + chứng từ CKTT | 331 về 0 |
| B | Trả sau hạn, trước đáo hạn | 12/10–31/10 | Bán: Nợ 112 / Có 131 110.000.000. Mua: Nợ 331 / Có 112 110.000.000 | 110.000.000 | Chứng từ thu/chi thường | về 0, không CKTT |
| C | Một phần trong hạn | Tất toán 55.000.000 (50%) | Bán: Nợ 112 54.000.000; Nợ 635 1.000.000 / Có 131 55.000.000 | 54.000.000 + 1.000.000 | CKTT + tiền | Còn mở 55.000.000 (CKTT phần này đã hết hạn) |
| D | Gộp 2 hóa đơn (HĐ2: 55.000.000, không điều khoản) | Tiền về 10/10: 163.000.000 | Bán: Nợ 112 163.000.000; Nợ 635 2.000.000 / Có 131 165.000.000 (HĐ1 110.000.000 + HĐ2 55.000.000) | 163.000.000 + 2.000.000 | 1 giấy báo Có, **phân bổ theo hóa đơn** | Cả hai về 0; CKTT chỉ HĐ1 |
| E | Hủy/đảo thanh toán sau khi đã hưởng | Chứng từ A bị đảo | Bán: Nợ 131 110.000.000 / Có 112 108.000.000, Có 635 2.000.000 | đảo nguyên bút toán | Chứng từ đảo | 131 mở lại 110.000.000; muốn hưởng lại phải trả lại trong hạn |
| F | Thu tiền thiếu do khách tự khấu trừ sai | Khách trả 161.700.000 cho 165.000.000 (tự trừ 2% trên tổng) | CKTT hợp lệ 2.000.000 hạch toán; **phần thiếu 1.300.000 giữ mở** (đối chiếu/tranh chấp) | — | Biên bản đối chiếu | Còn mở 1.300.000, **không tự coi là chi phí** |
| G | Phí ngân hàng làm thiếu tiền | Nhận 107.978.000 (thiếu 22.000) | Nhận tiền + CKTT như A; 22.000 chờ xác định: nếu duyệt là phí NH của bên bán → Nợ chi phí ngân hàng 22.000 / Có 131 | 22.000 | Sao kê, duyệt | Mở 22.000 đến khi duyệt |
| H | Ngoại tệ | HĐ USD | Thanh toán theo tỷ giá ghi sổ bình quân di động; CKTT tính trên nguyên tệ rồi quy đổi; chênh lệch tỷ giá vào FX_REALIZED_* | — | — | **HAK chưa tính CKTT cho hóa đơn ngoại tệ** (xem mục 5) |

**Phương án cơ sở gồm VAT (so sánh):** 2% × 110.000.000 = 2.200.000; tiền = 107.800.000. VAT trên hóa đơn **vẫn 10.000.000** (dùng tổng công nợ làm cơ sở tính khác với điều chỉnh thuế: không lập hóa đơn điều chỉnh, không đổi 3331/1331). Phần chênh 200.000 là bên bán nhường nhiều hơn theo hợp đồng; phải ghi rõ trong thỏa thuận [CẦN XÁC MINH quan điểm thuế]. **HAK mặc định cơ sở chưa thuế.**

**Đối chiếu:** công nợ trước thanh toán = tiền + CKTT hợp lệ + giảm công nợ hợp lệ khác + còn lại.
A: 110.000.000 = 108.000.000 + 2.000.000 + 0 + 0 ✔. C: 110.000.000 = 54.000.000 + 1.000.000 + 0 + 55.000.000 ✔. D: 165.000.000 = 163.000.000 + 2.000.000 ✔. F: 165.000.000 = 161.700.000 + 2.000.000 + 1.300.000 (còn mở) ✔.

Thanh toán một phần — hai cách hiểu, **chốt: số đầu vào là phần công nợ được tất toán** (S); CKTT = 2.000.000 × S/110.000.000; tiền = S − CKTT. Nếu khách chỉ báo tiền X (vd 55.000.000), tất toán S = X × 110/108 = 56.018.519 (làm tròn) — nên yêu cầu khách ghi rõ khoản tất toán, tránh suy ngược.

## 5. Cấu hình ERP — ánh xạ vào HAK
Mô hình chung: Payment Terms (D, P%, N), Baseline Date, Discount Base, tài khoản CKTT được hưởng/cấp cho khách, dung sai, chiều phân tích, phân quyền.

| Tham số | Ý nghĩa | Ví dụ | Nguồn dữ liệu | Người quản lý | Kiểm soát | Tình trạng HAK |
|---|---|---|---|---|---|---|
| Điều khoản mặc định (P%, D ngày) | Điền sẵn vào hóa đơn | 2% / 10 | Danh mục đối tượng (`PaymentDiscountPercent/Days`, cũng theo nhóm danh mục) | Kế toán trưởng | Ai sửa danh mục có quyền; nhật ký | **Có** |
| Điều khoản trên hóa đơn | **Snapshot** đã áp dụng | 2% / 10 | Hóa đơn (`PaymentDiscountPercent/Days`) | Người lập | Hóa đơn ghi sổ không sửa; đổi danh mục sau này không ảnh hưởng hóa đơn cũ | **Có** (snapshot) |
| Net due days | Hạn thanh toán | 30 | `DueDate` hóa đơn / `PaymentTermsDays` đối tượng | Kế toán trưởng | — | Có |
| Baseline date | Ngày bắt đầu đếm | Ngày hóa đơn | Hóa đơn | — | — | Chỉ ngày hóa đơn; ngày khác = **tùy chỉnh** |
| Nhiều bậc | 3/5, 2/10 | — | — | — | — | **Chưa có** |
| Discount base | Chưa thuế / gồm thuế / theo hợp đồng | Chưa thuế | — | KTT | Ghi hợp đồng | Chỉ chưa thuế; lựa chọn khác = tùy chỉnh |
| TK CKTT được hưởng / cấp cho khách | Vai trò `PAYMENT_DISCOUNT_INCOME` / `PAYMENT_DISCOUNT_EXPENSE` | 515 / 635 (theo chế độ) | Thiết lập › vai trò TK | KTT | Mã do công ty ánh xạ, **không seed** | **Có** |
| Dung sai ngày/số tiền | Cho phép lệch nhỏ | 0 | — | — | Duyệt ngoại lệ | **Chưa có** (hiện không dung sai) |
| Ghi đè | Quá hạn vẫn cho CKTT | — | Chế độ độc lập (nhập số) + duyệt chứng từ | KTT duyệt | Người lập ≠ người duyệt (công tắc quy trình duyệt) | Có |
| Chiều phân tích | Bộ phận, trung tâm chi phí | — | Dòng chứng từ | — | — | Chưa gắn riêng cho CKTT |
| Quyền | Lập chứng từ tiền + xem báo cáo | — | `voucher.create`, `cash.document`, `report.view` | — | Kiểm tra ở backend | Có |

**Dung sai và quyền hưởng:** dung sai đối trừ công nợ (làm tròn, chênh phí nhỏ) **không tự làm phát sinh quyền hưởng CKTT**; quyền hưởng chỉ từ điều khoản hợp đồng + thanh toán đúng điều kiện. Nếu doanh nghiệp muốn dung sai (vd 1.000đ), phải ghi vào thỏa thuận và cấu hình như tham số riêng có duyệt — HAK hiện không có.

**Phiên bản điều khoản:** điều khoản được **sao vào hóa đơn lúc lập**; hóa đơn đã ghi sổ bất biến nên danh mục đổi sau đó không làm đổi hóa đơn cũ. Chứng từ CKTT lưu nguồn (`HAK.PAYDISC` + hóa đơn hoặc `PARTNER:…#manual:`) và được kiểm tra lại khi ghi sổ.

## 6. Luồng AR / AP và ngoại lệ
**AR:** hóa đơn bán (có điều khoản) → hệ thống tính hạn → màn *Chiết khấu thanh toán* liệt kê khoản còn hạn, số CKTT **dự kiến** và số tiền khách phải trả → nhận sao kê/giấy báo Có → nhận diện khách → người lập chọn tài khoản tiền, ngày = **ngày ghi Có** → chứng từ **nháp** (bù trừ công nợ + dòng CKTT) → duyệt → ghi sổ (kiểm tra lại hạn/công thức) → công nợ cập nhật.
**AP:** hóa đơn mua → tính hạn → đề xuất thanh toán (hàng chờ thanh toán) → đánh giá trả sớm (xem mục 7) → duyệt → chi tiền → đối chiếu ngân hàng → chứng từ CKTT bù trừ.

- **Dự kiến vs hạch toán:** chỉ **chứng từ ghi sổ** mới hạch toán; danh sách/báo cáo "dự kiến" không ghi sổ.
- **Matching ngân hàng vs clearing công nợ:** matching = khớp dòng sao kê với bút toán tiền (có/không khớp); clearing = bù trừ khoản mở công nợ. Dòng sao kê không mã giao dịch chỉ **gợi ý**, người dùng xác nhận (chị chốt 16/10).
- **Chống ghi hai lần:** một khoản công nợ mở chỉ bù trừ được một lần (phân bổ không vượt phần mở); hóa đơn đã có chứng từ CKTT chưa hủy thì chặn lập chứng từ thứ hai (trùng nguồn); CKTT chỉ do chứng từ CKTT mang, chứng từ thu/chi thường không tự tạo dòng CKTT.
- **Hoàn nguyên:** hủy chứng từ nháp (chưa ghi sổ), hoặc đảo chứng từ đã ghi sổ → bút toán đảo, khoản mở lại.

**Ma trận ngoại lệ** (cấp duyệt: người có quyền duyệt chứng từ; dấu vết: nhật ký kiểm toán `payment_discount.*`, lịch sử chứng từ):

| Trường hợp | Quy tắc | Phân bổ | Trạng thái công nợ | Ghi chú |
|---|---|---|---|---|
| Trả sau hạn nhưng vẫn cho | Mặc định **không**; ngoại lệ có duyệt, ghi lý do, dùng chế độ độc lập | Theo hóa đơn chọn | Tất toán | Dấu vết: lý do + người duyệt |
| Một phần trong hạn, còn lại ngoài hạn | CKTT theo tỷ lệ phần tất toán trong hạn | Theo khoản chọn | Phần còn lại mở (Partial/Residual) | Tuổi nợ vẫn tính từ hóa đơn gốc |
| Gộp nhiều hóa đơn khác điều khoản | Tính riêng từng hóa đơn | Theo từng khoản | Từng khoản về 0 | 1 giấy báo Có → nhiều dòng bù trừ |
| Trả thừa/thiếu | Thiếu: giữ khoản mở; thừa: ghi nhận tiền ứng/khoản phải trả khách | — | Mở/ứng trước | Không tự coi là CKTT |
| Phí ngân hàng | Xác định rồi duyệt mới hạch toán chi phí | — | Mở đến khi duyệt | |
| Tranh chấp/credit note | Tính trên phần còn mở sau điều chỉnh | — | — | |
| Ghi Có sau hạn dù chuyển trước hạn | Theo ngày ghi Có; ngoại lệ có chứng cứ chuyển sớm + duyệt | — | — | Hợp đồng nên nói rõ |
| Hóa đơn ngoại tệ | Tính theo nguyên tệ rồi quy đổi | — | — | **HAK chưa làm** |
| Thanh toán bị đảo sau clearing | Đảo chứng từ; mở lại khoản | — | Mở | |

*Partial Payment* = một lần thanh toán một phần còn khoản mở gốc, *Residual Item* = tạo khoản mới cho phần còn lại. HAK dùng cách thứ nhất (phân bổ trên khoản gốc): **tuổi nợ và truy vết hóa đơn gốc giữ nguyên**, nên được ưu tiên.

## 7. Dòng tiền, chi phí – lợi ích (kịch bản, không phải lãi suất thị trường)
- Công thức tham chiếu: chi phí tín dụng năm (đơn) = d/(1−d) × 365/(N−D). Với 2/10 net 30: 0,02/0,98 × 365/20 = **37,24%/năm**.
- **Giới hạn công thức:** d = 2% là tỷ lệ trên giá chưa thuế, còn khoản "tài trợ" thực tế là tổng công nợ. Tính lại từ số tiền thực: trả sớm 108.000.000 thay vì 110.000.000 sau 20 ngày → lãi 2.000.000/108.000.000 = 1,852% / 20 ngày → **33,80%/năm đơn** (ghép lãi ≈ 39,8%). Dùng con số 33,80% khi cơ sở khác nhau.
- So với lãi vay giả định (20 ngày, tiền trả sớm 108.000.000):

| Lãi vay giả định | Chi phí vay 20 ngày | Lợi ích CKTT | Chênh (bên mua lãi / bên bán lỗ) |
|---|---|---|---|
| 8%/năm | 473.425 | 2.000.000 | 1.526.575 |
| 10%/năm | 591.781 | 2.000.000 | 1.408.219 |
| 12%/năm | 710.137 | 2.000.000 | 1.289.863 |

  **Điểm hòa vốn:** lãi suất vay ≈ 33,80%/năm (đơn). Dưới mức này, bên mua có lợi khi trả sớm; bên bán chi phí cấp chiết khấu lớn hơn lợi ích dòng tiền nếu chi phí vốn của bên bán thấp hơn 33,80%.
- **Thuế (kịch bản):** nếu thuế suất TNDN giả định 20% [CẦN XÁC MINH thuế suất và tính hợp lệ chi phí]: bên bán giảm thuế 400.000; bên mua tăng thuế 400.000.
- **DSO/DPO:** nếu 60% khách hưởng (ví dụ kịch bản), DSO giảm khoảng 20 ngày × 60% = **12 ngày**; với doanh số 1 tỷ/tháng, phải thu giảm ≈ 400 triệu. Bên mua: DPO giảm tương ứng; thanh khoản bị chiếm 20 ngày sớm hơn.
- **Rủi ro:** khách có vốn dư trả sớm vẫn hưởng mà không tạo lợi ích dòng tiền thêm; khách trả muộn rồi tự khấu trừ; biên lợi nhuận mỏng bị bào mòn (2% trên giá chưa thuế).

## 8. Kiểm soát, báo cáo, yêu cầu và UAT
**Kiểm soát (CTRL):**
| Mã | Kiểm soát | HAK |
|---|---|---|
| CTRL-01 | Tách người lập chính sách / người lập chứng từ / người duyệt / người chi tiền / người đối chiếu | Lập ≠ duyệt (công tắc quy trình duyệt); vai trò riêng |
| CTRL-02 | Không sửa điều khoản sau khi hóa đơn ghi sổ | Có (hóa đơn bất biến) |
| CTRL-03 | Chấp nhận quá hạn / ghi đè tỷ lệ phải có lý do và duyệt | Một phần (qua duyệt chứng từ; chưa có trường lý do riêng) |
| CTRL-04 | Chứng từ CKTT không sửa tay ngày/số tiền; kiểm tra lại khi gửi duyệt/duyệt/ghi sổ | Có |
| CTRL-05 | Nhật ký kiểm toán + đính kèm chứng từ | Có (nhật ký, tệp đính kèm) |
| CTRL-06 | Đối chiếu AR/AP với sổ cái | Có (đối chiếu công nợ) |

**Báo cáo (RPT):** RPT-01 CKTT dự kiến/đủ điều kiện (**có**: danh sách đủ điều kiện); RPT-02 thực tế đã ghi nhận theo đối tượng (**có**: báo cáo chiết khấu, cột CKTT); RPT-03 **bỏ lỡ ở AP** (khoản mua quá hạn hưởng chưa trả — **chưa có**); RPT-04 khách tự khấu trừ sai ở AR (khoản mở nhỏ bất thường sau thanh toán — **chưa có**); RPT-05 chi phí theo đối tượng/đơn vị kinh doanh (theo đối tượng có; theo đơn vị chưa); RPT-06 DSO/DPO trước–sau (**chưa có**); RPT-07 hiệu quả dòng tiền vs chi phí (**chưa có**).

**Yêu cầu chức năng / quy tắc:** FR-01 điều khoản P%/D/N trên đối tượng và hóa đơn; FR-02 liệt kê khoản đủ điều kiện + CKTT dự kiến; FR-03 lập chứng từ nháp bù trừ + dòng CKTT; FR-04 kiểm tra lại khi ghi sổ; FR-05 chế độ độc lập không theo điều khoản; FR-06 nhiều bậc [chưa]; FR-07 hóa đơn ngoại tệ [chưa]; FR-08 một chứng từ cho nhiều hóa đơn [chưa — hiện mỗi chứng từ một hóa đơn/khoản]. BR-01 hạn hưởng = ngày cơ sở + D (gồm ngày cuối); BR-02 CKTT = P% × tiền hàng chưa thuế × (phần tất toán / phần phải thu gốc), làm tròn đồng; BR-03 không đổi doanh thu, giá mua, thuế; BR-04 không trích trước; BR-05 phần thiếu không tự là CKTT; BR-06 hủy/đảo thì khoản mở lại.

**Kịch bản UAT** (nền: HĐ 100.000.000 + VAT 10.000.000, 01/10/2026, 2/10 net 30, hạn hưởng 11/10; đã có ánh xạ vai trò; tiền đủ):

| Mã | Dữ liệu đầu vào | Thao tác | Kết quả tính | Bút toán kỳ vọng (bán) | Công nợ còn lại | Đạt khi |
|---|---|---|---|---|---|---|
| UAT-01 | Tiền về 01/10 | Lập CKTT, ghi sổ | CKTT 2.000.000; tiền 108.000.000 | Nợ 112 108M, Nợ 635 2M / Có 131 110M | 0 | Cân Nợ=Có, 131 = 0 |
| UAT-02 | Tiền về **11/10** (ngày cuối) | như trên | như UAT-01 | như trên | 0 | Ngày cuối vẫn được hưởng |
| UAT-03 | Tiền về **12/10** | Cố lập CKTT | Bị từ chối (quá hạn); thanh toán thường 110.000.000 | Nợ 112 / Có 131 110M | 0 | Không có dòng 635 |
| UAT-04 | HĐ hàng 33.333.333 + VAT 3.333.333 = 36.666.666 | Lập CKTT trong hạn | 2% = 666.666,66 → **666.667**; tiền 35.999.999 | Nợ 112 35.999.999, Nợ 635 666.667 / Có 131 36.666.666 | 0 | Làm tròn đúng, cân |
| UAT-05 | Tất toán 55.000.000 trong hạn | Lập CKTT một phần | CKTT 1.000.000; tiền 54.000.000 | Nợ 112 54M, Nợ 635 1M / Có 131 55M | 55.000.000 | Khoản gốc còn mở đúng 55M |
| UAT-06 | Sau UAT-05, trả 55.000.000 ngày 20/10 | Thu thường | CKTT 0 | Nợ 112 / Có 131 55M | 0 | Tổng CKTT = 1.000.000 |
| UAT-07 | HĐ1 + HĐ2 (55.000.000, không điều khoản), tiền 163.000.000 ngày 10/10 | Phân bổ theo hóa đơn | CKTT chỉ HĐ1 = 2.000.000 | Nợ 112 163M, Nợ 635 2M / Có 131 165M | 0 | HĐ2 không có CKTT |
| UAT-08 | Khách trả 161.700.000 cho 165.000.000 (tự trừ 2% trên tổng) | Ghi nhận | CKTT hợp lệ 2.000.000; thiếu 1.300.000 | Nhận tiền + CKTT | 1.300.000 mở | Phần thiếu không vào 635 |
| UAT-09 | Nhận 107.978.000 (phí NH 22.000) | Ghi nhận | CKTT 2.000.000; thiếu 22.000 | Chờ duyệt phí | 22.000 mở đến khi duyệt | Không tự coi là CKTT |
| UAT-10 | Hủy sau ghi sổ UAT-01 | Đảo chứng từ | — | Nợ 131 110M / Có 112 108M, Có 635 2M | 110.000.000 | Số dư 112, 635, 131 trở về như trước |
| UAT-11 | Sau phát hành hóa đơn, đổi điều khoản mặc định đối tượng sang 3/10 | Mở danh sách | HĐ cũ vẫn 2% | — | — | Hóa đơn cũ giữ 2/10 |
| UAT-12 | Chứng từ CKTT nháp bị sửa ngày sang 15/10 | Gửi duyệt/ghi sổ | Bị từ chối | — | — | Không ghi sổ được |
| UAT-13 | Không có điều khoản; chế độ độc lập: khoản mở 110.000.000, CKTT 1.000.000 | Lập, ghi sổ | Tiền 109.000.000 | Nợ 112 109M, Nợ 635 1M / Có 131 110M | 0 | Hóa đơn không đổi |
| UAT-14 | HĐ ngoại tệ | Tìm trong danh sách CKTT | Không xuất hiện (giới hạn đã biết) | — | — | Ghi nhận giới hạn, xử lý bằng thu/chi ngoại tệ |

Các UAT-01…07, 10, 12, 13 đã có hoặc tương đương test tự động trong `TradeTests` (PaymentDiscount_*); UAT-08, 09, 11, 14 cần bổ sung/xác nhận khi triển khai.

## 9. Quyết định cần chốt trước khi triển khai
1. Mã tài khoản theo TT99 cho vai trò CKTT được hưởng / cấp cho khách (chị xác nhận).
2. Cơ sở tính: chỉ chưa thuế, hay có hợp đồng tính trên tổng thanh toán? Nếu có, cần thêm tham số Discount Base.
3. Có cần **nhiều bậc** (3/5, 2/10) và **dung sai** ngày/số tiền không?
4. Có cần **CKTT cho hóa đơn ngoại tệ** và **một chứng từ cho nhiều hóa đơn** không (hiện chưa)?
5. Ngày xét điều kiện: ngày ghi Có ngân hàng (đề xuất) hay ngày hạch toán; có dời hạn khi trùng ngày nghỉ?
6. Quan điểm thuế về CKTT (VAT không đổi, chi phí/thu nhập tài chính hợp lệ, hồ sơ cần có) — chị xác minh.
7. Xử lý khách tự khấu trừ sai và phí ngân hàng: ai duyệt, hạn mức tự xử lý.
8. Có cần các báo cáo còn thiếu (RPT-03, 04, 06, 07)?

## 10. Cập nhật theo quyết định của chị 26/10/2026 (đã cài đặt)
| Quyết định của chị | Đã làm |
|---|---|
| Giữ cả hai cách (theo điều khoản trên hóa đơn và độc lập không theo hóa đơn) | Giữ nguyên |
| Cơ sở tính tùy biến | Tùy chọn `base=incl` / `excl` (snapshot trên hóa đơn, mặc định kế thừa từ đối tượng); gồm thuế thì CK = P% × (tiền hàng + thuế), **thuế GTGT trên hóa đơn không đổi**; hóa đơn ngoại tệ chỉ hỗ trợ chưa thuế |
| Ngày xét điều kiện = ngày tiền về ngân hàng | Nhãn trên giao diện là "Ngày tiền về ngân hàng"; chứng từ ghi theo ngày đó; không dời hạn khi trùng ngày nghỉ (chị không yêu cầu) |
| Nhiều bậc | `tier=P:N` (vd `tier=3:5` cùng bậc chính 2/10): bậc tỷ lệ cao nhất còn hạn; bậc xa hơn phải thấp hơn |
| Dung sai | **Dung sai ngày** `grace=N` (0–10 ngày, cộng vào mọi hạn). **Dung sai số tiền: KHÔNG làm** — trái nguyên tắc chị chốt "không tự coi phần thiếu là chiết khấu" |
| Hóa đơn ngoại tệ | Màn Thu/chi ngoại tệ có ô "Áp chiết khấu thanh toán": CK tính bằng nguyên tệ, tiền thực thu = nguyên tệ − CK, dòng CK ghi theo giá trị ghi sổ của hóa đơn nên chênh lệch tỷ giá chỉ phát sinh trên phần tiền thực; ghi sổ kiểm tra lại |
| Một chứng từ cho nhiều hóa đơn | `POST /payment-discounts/multi` + nút "Thanh toán gộp" (chọn ≥ 2 hóa đơn cùng đối tượng): CK tính riêng từng hóa đơn; hóa đơn quá hạn / không điều khoản chỉ bù trừ |
| Khách tự khấu trừ sai, phí ngân hàng: không tự coi là chiết khấu | Giữ nguyên; báo cáo "khoản dư nhỏ nghi khấu trừ" chỉ gợi ý đối chiếu |
| Báo cáo tham khảo Odoo / MISA | 4 báo cáo ở nút "Báo cáo": chiết khấu đã mất (RPT-03), khoản dư nghi khấu trừ sai (RPT-04), DSO/DPO theo tháng (RPT-06), hiệu quả dòng tiền / chi phí tín dụng quy năm (RPT-07) |
| Tài khoản phải trả rebate: 338 hoặc 138 | Vai trò `REBATE_PAYABLE` (do công ty ánh xạ; bên bán dùng nhóm 338 "phải trả khác"; **bên mua 138 chưa làm** — trích trước chỉ chiều bán); không seed mã |
| Thuế: không xuất hóa đơn nên không ảnh hưởng | CKTT không đổi doanh thu / giá mua / thuế GTGT (đúng thiết kế) |

Migration `Phase24PaymentDiscountOptions`. Chuỗi tùy chọn: `base=incl;grace=1;tier=3:5` (nhập ở Danh mục đối tượng, cột tùy chọn chiết khấu thanh toán; hóa đơn lập mới sao chép).

