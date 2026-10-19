# 10 — Thiết kế theo chu trình (bám tài liệu "Phân tích thiết kế hệ thống ERP", 20/12/2016)

Trạng thái: **ĐANG THỰC HIỆN** (19/10/2026) — chị đã giao em quyết các điểm mở theo đề xuất (mục 8); lát cắt đầu **Cung ứng → Thanh toán đã làm xong** (mục 9). Bản này bám tệp chị gửi; số mục (I–V) và tên tập tin lấy từ tệp. Tệp là mô hình quy trình và kiểm soát, **không phải căn cứ pháp lý**: pháp lý vẫn theo `legal/` (TT99). Tệp chỉ mô tả chi tiết 3 chu trình (doanh thu, cung ứng, chuyển đổi — chuyển đổi mới đến phần giới thiệu và danh sách chức năng); chu trình nhân sự – tiền lương và tài chính chỉ được giới thiệu ở mục IV.2.

## 1. Quy trình kế toán trong tệp (mục I.5.2) — nền của "phát sinh / chuyển đổi / kết quả"
| Giai đoạn trong tệp | Dữ liệu | Trong tệp là | Trong hệ thống hiện có |
|---|---|---|---|
| **Ghi nhận** | **Dữ liệu phát sinh**: chứng từ nghiệp vụ (đơn đặt hàng, lệnh bán hàng, phiếu giao hàng, phiếu xuất/nhập kho, hóa đơn, phiếu thu/chi, giấy báo) | các tập tin nghiệp vụ + danh mục | `orders`, `stock_documents`, `invoices`, `payment_orders`, `bank_statements`, `vouchers` (Nháp) |
| **Xử lý** | **Dữ liệu chuyển đổi**: ghi **Nhật ký** theo trình tự thời gian, phân loại vào **Sổ cái**, theo dõi **sổ chi tiết**; tính giá xuất, giá thành | tập tin **Nhật ký** + **Chi tiết Nhật ký** (MACT, SOCT, NGAY, SOTIEN, TKNO, TKCO, CT_TKNO, CT_TKCO) | `vouchers` + `journal_lines` (TK Nợ/Có, đối tượng chi tiết trên dòng), Posting Rule Engine, tính giá xuất kho, khấu hao, giá thành |
| **Báo cáo** | **Dữ liệu kết quả**: bảng cân đối tài khoản, đối chiếu tổng hợp–chi tiết, BCTC, báo cáo quản trị | nhóm **tập tin tồn / tổng hợp** (TH_PHAITHUnn, TH_PHAITRAnn, TH_HANGHOAnn, TH_VATTUnn: dư đầu, phát sinh từng tháng, dư cuối) | tính trực tiếp từ sổ (BCĐPS, sổ chi tiết, tuổi nợ, nhập–xuất–tồn, Report Engine). **Khác tệp có chủ ý:** tệp cho phép lưu tập tin tồn và cập nhật theo từng nghiệp vụ hoặc định kỳ; hệ thống tính từ sổ để không bao giờ lệch |

**"Phát sinh nghiệp vụ kế toán hay không"** = khâu đó có ghi vào Nhật ký hay không (tệp: phiếu xuất kho khi giao hàng, hóa đơn mua, phiếu thu… "cập nhật vào các tập tin Nhật ký và Chi tiết Nhật ký"; còn nhận đơn đặt hàng, lập lệnh bán hàng, yêu cầu mua hàng, đặt hàng chỉ lưu tập tin nghiệp vụ).

## 2. Chu trình doanh thu (mục V.1) — các khâu theo tệp
Chuỗi: Đơn đặt hàng → (kiểm tra tín dụng và tồn kho) → **Lệnh bán hàng** → Chuẩn bị hàng → **Giao hàng** (phiếu giao hàng, vận đơn, phiếu xuất kho) → **Hóa đơn bán hàng** → Theo dõi phải thu → **Thu tiền** → xử lý hàng trả lại, giảm giá, nợ quá hạn → phân tích tiêu thụ.

| Khâu (mục tệp) | Tập tin và trường theo tệp | Ghi Nhật ký? | Hiện trạng |
|---|---|---|---|
| Nhận đơn đặt hàng (V.1.1.2.1, V.1.2.1) | Đơn đặt hàng: số, mã KH, ngày đặt, **ngày giao**, **điều kiện thanh toán**, **mã NV bán hàng**; chi tiết: mã hàng, số lượng | Không | Có đơn bán; **thiếu** ngày giao, điều kiện thanh toán, NV bán hàng |
| Kiểm tra tín dụng và tồn kho (V.1.2.1) | Danh mục KH: **hạn mức tín dụng**, **số dư công nợ hiện tại**; Danh mục hàng: **tồn tối thiểu**, **số lượng đặt tối ưu**, **tồn hiện tại**. Vượt hạn mức → yêu cầu trả nợ cũ hoặc trình trưởng phòng; thiếu hàng → **đơn giao trễ** | Không | **Thiếu** cả hai kiểm tra và các trường danh mục |
| Lập lệnh bán hàng (V.1.1.2.3) | Lệnh bán hàng + chi tiết (số lệnh, số đơn, ngày); in nhiều liên: kho (như phiếu xuất), chuẩn bị hàng, lập hóa đơn, khách | Không | **Thiếu** |
| Chuẩn bị hàng, giao hàng (V.1.2.2) | Gửi hàng: số phiếu, số lệnh, ngày, **nơi nhận**, **đơn vị vận tải**, **NV giao hàng**; Phiếu xuất kho: số phiếu, ngày, **số phiếu gửi hàng**, mã kho, **NV xuất hàng**; chi tiết có đơn giá (theo phương pháp giá xuất ở chu trình chuyển đổi) | **Có** (xuất kho ghi Nhật ký) | Có phiếu xuất; **thiếu** phiếu giao hàng/vận đơn và các trường liên kết |
| Lập hóa đơn (V.1.2.3) | Đối chiếu phiếu giao hàng ↔ lệnh bán ↔ đơn đặt; tự lấy **giá bán**, **thuế suất GTGT** từ danh mục; hóa đơn: số, ngày, số phiếu xuất, mã KH, doanh thu, thuế, **ngày đáo hạn** | **Có** | Có hóa đơn gắn dòng phiếu kho; **thiếu** tự điền giá bán/thuế suất từ danh mục |
| Theo dõi phải thu (V.1.2.3b) | Theo từng hóa đơn **và** theo khách hàng (tệp khuyên kết hợp); CT_TKNO/CT_TKCO mã KH trên Chi tiết Nhật ký; TH_PHAITHU | — | **Có** (phân bổ theo hóa đơn, tuổi nợ, đối chiếu sổ chi tiết–sổ cái) |
| Thu tiền (V.1.2.3b, hình 5.9) | Phiếu thu / giấy báo có → kiểm tra, hiệu chỉnh → Nhật ký; báo cáo quỹ, báo cáo thu tiền bán hàng | **Có** | Có; **thiếu** báo cáo thu tiền bán hàng |
| Hàng trả lại, giảm giá (V.1.1.2.9) | biên bản ghi nhận thỏa thuận, phê duyệt | **Có** | Có điều chỉnh sau bán; **thiếu** biên bản/phê duyệt riêng |
| Nợ quá hạn (V.1.1.2.9) | thông báo cho khách, xóa nợ có thẩm quyền | Xóa nợ: **Có** | **Thiếu** thông báo và xóa nợ có phê duyệt |
| Phân tích tiêu thụ (V.1.2.3) | doanh thu, số lượng theo mặt hàng/ngành hàng/KH; giá vốn theo mặt hàng | — | **Thiếu** báo cáo quản trị |

## 3. Chu trình cung ứng (mục V.2) — các khâu theo tệp
Chuỗi: **Yêu cầu mua hàng** → chọn/phê duyệt NCC → **Đơn đặt hàng** → **Nhận hàng** (chứng từ nhận hàng, biên bản kiểm nghiệm, số lượng thực nhận) → **Phiếu nhập kho** → **Hóa đơn mua hàng** (đối chiếu) → **Chấp nhận và theo dõi thanh toán** → **Thanh toán** → xử lý bất thường.

| Khâu (mục tệp) | Tập tin và trường theo tệp | Ghi Nhật ký? | Hiện trạng |
|---|---|---|---|
| Nhận biết nhu cầu (V.2.2) | Yêu cầu mua hàng + chi tiết (số yêu cầu); tự đưa vào danh mục cần đặt khi **tồn hiện tại < tồn tối thiểu**, số đặt = **số lượng đặt tối ưu** | Không | **Thiếu** |
| Chọn NCC (V.2.2) | Danh mục NCC thêm **xếp hạng uy tín** (cập nhật sau mỗi lần mua: chất lượng, giá, thanh toán, chiết khấu, hậu mãi); NCC chưa từng mua phải được cấp có thẩm quyền duyệt | Không | **Thiếu** |
| Đặt hàng (V.2.2) | Đơn đặt hàng + chi tiết; in liên: NCC, **bộ phận nhận hàng**, **kế toán theo dõi thanh toán** | Không | Có đơn mua |
| Nhận hàng, nhập kho (V.2.2) | Tập tin nhận hàng; **biên bản kiểm nghiệm**; phiếu nhập kho + chi tiết; TH_HANGHOA | **Có** | Có phiếu nhập theo đơn; **thiếu** biên bản kiểm nghiệm, số lượng thực nhận so với đặt |
| Hóa đơn mua (V.2.2.3) | Đối chiếu hóa đơn ↔ đơn đặt hàng ↔ phiếu giao hàng NCC ↔ biên bản kiểm nghiệm ↔ phiếu nhập kho (hệ thống tự kiểm tra thống nhất giữa các tập tin) → ghi Hóa đơn mua, Nhật ký, thuế GTGT được khấu trừ | **Có** | Có hóa đơn mua nhiều phiếu, chống vượt số đã nhận; **chưa** có màn đối chiếu nhiều bên rõ ràng |
| **Chấp nhận và theo dõi thanh toán** (V.2.1.2.4, V.2.2.3) | Theo dõi **theo hóa đơn** (xếp theo **ngày đáo hạn**; "hồ sơ chứng từ chưa thanh toán" theo ngày trong tháng, mỗi ngày lấy chứng từ đến hạn ra thanh toán) **và theo NCC** (tệp khuyên kết hợp cả hai); phục vụ **kế hoạch tiền** và hưởng chiết khấu thanh toán | Không (chấp nhận), **Có** khi chi | Có công nợ theo hóa đơn và UNC; **thiếu** bước "chấp nhận thanh toán" và danh sách đến hạn theo ngày |
| Thanh toán (V.2.1.2.5) | Phiếu chi / UNC → Nhật ký | **Có** | Có lệnh thanh toán 6 trạng thái |
| Bất thường (V.2.1.2.6) | trả hàng, giảm giá, sai lệch | **Có** | Có trả hàng và giảm giá mua |

### Ví dụ theo yêu cầu của chị: Mua hàng → Thanh toán
Trong tệp, "liên chứng từ" chuyển từ bộ phận này sang bộ phận kia (đơn đặt hàng: liên gửi kế toán theo dõi thanh toán) và "hồ sơ chứng từ chưa thanh toán" xếp theo ngày đáo hạn. Hệ thống số hóa đúng hai thứ đó:
1. Cung ứng: sau khi đối chiếu hóa đơn mua đạt, người có quyền bấm **Chấp nhận thanh toán** (không ghi Nhật ký). Hóa đơn vào **hàng chờ thanh toán theo ngày đáo hạn**.
2. Thanh toán: nhận danh sách đến hạn, **tự tạo Lệnh thanh toán Nháp** (gắn khoản công nợ cần tất toán); người khác duyệt, gửi ngân hàng, ngân hàng thực hiện → **ghi Nhật ký** (bút toán chi) và tất toán công nợ (đã có trong `PaymentOrderService`).
3. Kết quả quay lại: hóa đơn chuyển "đã thanh toán"; chỉ tiêu phải trả, kế hoạch tiền cập nhật.
Mỗi bàn giao chỉ xử lý một lần (khóa theo số hóa đơn), ghi cùng giao dịch cơ sở dữ liệu với bước chấp nhận.

## 4. Chu trình chuyển đổi (mục V.3) — chỉ có danh sách chức năng trong tệp
Lập kế hoạch sản xuất → cung ứng và quản lý nguyên vật liệu → tổ chức sản xuất (theo chức năng hoặc chuỗi giá trị) → kiểm soát sản xuất → **hạch toán và phân bổ chi phí sản xuất** → báo cáo tài chính và quản trị chi phí. Nội dung dựa trên tài liệu kỹ thuật: định mức thời gian, máy móc, vật liệu, nhân công, công đoạn. Hiện có: đối tượng giá thành, giá thành, tính giá xuất kho. **Thiếu:** kế hoạch sản xuất, định mức, lệnh sản xuất, phân bổ chi phí chung. Tệp chưa mô tả chi tiết nên đợt này **chưa thiết kế sâu**; cần chị cho biết quy trình sản xuất dăm của HAK.

## 5. Nhân sự – tiền lương và tài chính (mục IV.2)
Chỉ giới thiệu trong tệp. Hiện hệ thống nhận bảng lương JSON và có vay, ngoại tệ, ngân hàng. Không thiết kế thêm khi chưa có yêu cầu.

## 6. Kiểm soát (mục III) — quy tắc áp dụng cho mọi chu trình
- **Đầu vào (III.3.4.1):** đánh số chứng từ liên tục; định dạng dữ liệu; kiểm tra vùng dữ liệu, hợp lý (ngày bán ≤ ngày nhập liệu), **có thực** (mã đã khai báo), **giới hạn** (xuất không vượt hạn mức tín dụng); thông báo lỗi kèm hướng dẫn sửa.
- **Xử lý (III.3.4.2):** báo bất thường (**tồn kho âm, quỹ âm**); tự động **đối chiếu tổng hợp ↔ chi tiết**; đối chiếu bên ngoài (công nợ với khách/NCC, sao kê ngân hàng).
- **Đầu ra (III.3.4.3):** quy định người kiểm tra báo cáo, phân quyền xem.
- **Chung (III.3.3):** phân quyền theo chức năng, bất kiêm nhiệm (lập – duyệt – ghi sổ – thủ quỹ), khóa sau số lần sai mật khẩu (tệp nêu 3 lần; hệ thống đang là 5 và cấu hình được), nhật ký truy cập, mã hóa, sao lưu và kế hoạch phục hồi.
- **Bộ mã (mục II):** mã duy nhất, ngắn, biểu diễn được thuộc tính, kiểm tra được khi nhập (mã tự kiểm tra), nới rộng được; có quy tắc cấp mã và loại mã lỗi thời.

## 7. Cách thực hiện (tăng dần, không viết lại)
1. **Danh mục theo tệp:** khách hàng (địa chỉ, SĐT, email, hạn mức tín dụng, điều khoản thanh toán), hàng hóa (giá bán, thuế GTGT, tồn tối thiểu, số lượng đặt tối ưu), NCC (xếp hạng uy tín). Số dư công nợ hiện tại và tồn hiện tại **tính từ sổ**, không lưu riêng.
2. **Kiểm soát đầu vào khi nhận đơn bán:** kiểm tra tín dụng và tồn kho; vượt hạn mức thì chặn hoặc trình người có thẩm quyền (chị chọn); đơn giao trễ.
3. **Chu trình cung ứng:** yêu cầu mua hàng từ tồn tối thiểu, biên bản kiểm nghiệm, đối chiếu hóa đơn nhiều bên, **chấp nhận thanh toán → hàng chờ theo ngày đáo hạn → lệnh thanh toán Nháp** (ví dụ ở mục 3).
4. **Chu trình doanh thu:** lệnh bán hàng, phiếu giao hàng/vận đơn, hóa đơn tự điền giá và thuế từ danh mục, báo cáo bán hàng và thu tiền, phân tích tiêu thụ.
5. **Màn hình theo chu trình:** mỗi chu trình có màn hình các khâu (số chứng từ đang ở từng khâu, khâu nào ghi Nhật ký), thay cho menu phẳng.
6. **Chiều phân tích** (bộ phận, hợp đồng/lô tàu…): tệp coi là một phần của "kế toán trách nhiệm"; kế thừa dọc chuỗi chứng từ. Chờ chị chọn chiều.
7. Chu trình chuyển đổi: sau khi chị mô tả quy trình sản xuất.

## 8. Các điểm mở đã chốt (chị giao em quyết theo đề xuất, 19/10/2026)
1. **Mức tự động:** chu trình thanh toán tự tạo **lệnh Nháp**, người khác duyệt (tách nhiệm vụ). Không tự duyệt, không tự chuyển tiền.
2. **Quyền "chấp nhận thanh toán"** (`payment.accept`): Kế toán trưởng, Kế toán tổng hợp, Kế toán công nợ (người rà soát đối chiếu theo tệp mục V.2.2.3). Duyệt lệnh vẫn là `payment_order.approve` (khác người lập). Chưa có hạn mức theo vai trò; thêm khi có yêu cầu.
3. **Hạn mức tín dụng:** vượt hạn mức thì **trình người có thẩm quyền** (không chặn cứng), hạn mức để trống = không kiểm tra. Làm ở đợt doanh thu.
4. **Chiều phân tích:** hoãn. Trọng tâm trước là luồng dữ liệu; chiều (hợp đồng/lô tàu, bộ phận) thiết kế sau khi luồng ổn, kế thừa dọc chuỗi chứng từ.
5. **Chu trình chuyển đổi:** hoãn đến khi có mô tả quy trình sản xuất dăm; tệp mới nêu danh sách chức năng.

## 9. Đã làm: Cung ứng → Thanh toán (19/10/2026)
- Bảng `payment_acceptances` (migration `Phase7PaymentAcceptance`, SQLite và PostgreSQL): hóa đơn, nhà cung cấp, dòng công nợ phải trả cần tất toán, số còn nợ, ngày đáo hạn, trạng thái (Chờ tạo lệnh / Đã có lệnh / Bị từ chối / Đã rút), lệnh thanh toán, người và lúc chấp nhận/đóng, lý do.
- `PaymentAcceptanceService` (`src/Hak.Application/Cycles`): hàng chờ thanh toán theo ngày đáo hạn (hóa đơn mua VND đã ghi sổ còn nợ), **chấp nhận thanh toán** (không ghi Nhật ký; kiểm tra hóa đơn đã ghi sổ, còn nợ, chưa được chấp nhận), **tự tạo lệnh Nháp** khi công ty ánh xạ vai trò `BANK_PAYMENT_DEFAULT` và người chấp nhận có quyền lập lệnh (không thì chờ trong hộp thư), tạo lệnh tay, từ chối kèm lý do (trả về Cung ứng), rút chấp nhận (hủy luôn lệnh nếu chưa duyệt).
- **"Đã thanh toán" không lưu**: suy ra từ số còn lại của dòng công nợ và trạng thái lệnh, nên không bao giờ lệch sổ.
- API `/api/cycles/...`; màn hình **Mua hàng → Thanh toán** (các khâu kèm cờ ghi Nhật ký, hàng chờ theo ngày đáo hạn, hộp thư thanh toán).
- Test: 2 test tích hợp (chấp nhận không sinh bút toán; từ chối rồi chấp nhận lại; tạo lệnh; duyệt, gửi, chi, ghi sổ → đã thanh toán; ngân hàng mặc định tự tạo lệnh; rút chấp nhận hủy lệnh).
- Chưa làm tiếp: yêu cầu mua hàng từ tồn tối thiểu, biên bản kiểm nghiệm, xếp hạng nhà cung cấp, hóa đơn ngoại tệ (đi qua màn Ngoại tệ).

## 9b. Đã làm đợt 2 (19/10/2026): danh mục theo tệp, kiểm tra đầu vào đơn bán, danh mục cần đặt hàng
- **Danh mục khách hàng/NCC** (tệp mục V.1.2.1): thêm địa chỉ, điện thoại, email, **hạn mức tín dụng**, điều kiện thanh toán (số ngày); sửa trên màn Danh mục đối tượng (`PUT /partners/{id}/commercial`). Số dư công nợ hiện tại **tính từ sổ**, không lưu riêng.
- **Danh mục hàng hóa** (tệp mục V.1.2.1): giá bán, thuế suất GTGT mặc định, **tồn tối thiểu**, **số lượng đặt tối ưu** (Thiết lập → Kho & vật tư; `PUT /items/{id}/commercial`). Tồn hiện tại tính từ sổ kho.
- **Kiểm tra tín dụng và tồn kho khi xác nhận đơn bán** (`SalesOrderCheckService`, `GET /api/cycles/revenue/orders/{id}/check`): hạn mức so với số dư công nợ + giá trị đơn ước tính (đã gồm thuế); số lượng đặt so với tồn hiện tại. **Vượt hạn mức** thì xác nhận bị từ chối `CREDIT_LIMIT_EXCEEDED`, trừ khi người có quyền `credit.override` (Kế toán trưởng, Giám đốc) xác nhận kèm lý do, được ghi nhật ký kiểm toán (`order.credit_override`); **thiếu tồn không chặn** (đơn giao trễ). Khách không có hạn mức, đơn ngoại tệ, hoặc chưa ánh xạ vai trò AR_TRADE thì không kiểm tra. Kiểm soát nằm ở `OrderService.ConfirmAsync` (backend), không chỉ ở giao diện.
- **Danh mục cần đặt hàng** (`ReorderService`, `GET /api/cycles/procurement/reorder-list`): vật tư có tồn hiện tại < tồn tối thiểu, đề xuất = số lượng đặt tối ưu; hiện ở khâu 1 "Yêu cầu mua hàng" của màn Mua hàng → Thanh toán. Tính khi xem, không lưu.
- Migration `Phase7Catalog` (SQLite và PostgreSQL). Test: 2 test tích hợp mới (180 → 182); thử tay trên trình duyệt.

## 9c. Đã làm đợt 3 (19/10/2026): chu trình doanh thu (tệp mục V.1)
Chuỗi trên màn hình **Bán hàng → Thu tiền**: Đơn đặt hàng (+ kiểm tra tín dụng, tồn kho) → **Lệnh bán hàng** → **Phiếu giao hàng** (sinh phiếu xuất kho Nháp) → **Hóa đơn bán** → hàng chờ thu → **Thu tiền** → báo cáo.
- **Lệnh bán hàng** (`LBH{năm}-{số}`, không ghi Nhật ký): lập từ đơn bán đã xác nhận, mỗi dòng không vượt số đặt (theo dung sai của đơn) kể cả khi có nhiều lệnh; trạng thái Đang lập → Đã phát hành (kho được giao hàng) hoặc Đã hủy (chỉ khi chưa có phiếu giao hàng). **In 4 liên**: kho, chuẩn bị hàng, lập hóa đơn, khách hàng.
- **Phiếu giao hàng** (`PGH{năm}-{số}`): nơi nhận (mặc định theo lệnh), đơn vị vận tải, nhân viên giao hàng, số vận đơn; sinh **phiếu xuất kho Nháp** (TK đối ứng theo vai trò COGS) liên kết dòng đơn hàng; giao không vượt lệnh; **in 4 liên** (kèm hàng, lập hóa đơn, vận chuyển, lưu kho). Ghi sổ phiếu xuất kho là lúc **ghi Nhật ký** (người khác ghi sổ). Hủy được khi phiếu xuất kho còn Nháp và chưa có hóa đơn.
- **Lập hóa đơn từ phiếu giao hàng đã xuất kho**: giá, cơ sở tính giá (số lượng hoặc BDMT) và thuế suất lấy từ dòng đơn (đơn để giá 0 hoặc không có thuế suất thì lấy giá bán và thuế suất mặc định của danh mục hàng hóa); **hạn thanh toán = ngày hóa đơn + điều kiện thanh toán của khách**; ký hiệu và số hóa đơn do người lập nhập. Hóa đơn Nháp, ghi sổ do người khác như trước.
- **Hàng chờ thu tiền theo ngày đáo hạn** (hóa đơn bán VND đã ghi sổ còn nợ, số ngày quá hạn); nút **Lập phiếu thu** mở sẵn phiếu thu đúng khách, đúng số còn nợ, đã chọn hóa đơn cần tất toán.
- **Báo cáo**: phân tích tiêu thụ theo mặt hàng / khách hàng / tháng (doanh thu chưa thuế, số lượng, thuế GTGT; hàng trả lại và giảm giá ghi âm); báo cáo thu tiền bán hàng theo khách (phiếu thu tiền mặt và báo có ngân hàng). Giá vốn theo mặt hàng chưa có.
- Bảng `sales_dispatch_orders/lines`, `sales_deliveries/lines` (migration `Phase7SalesDispatch`, SQLite và PostgreSQL). Test: 1 test tích hợp (182 → 183); thử tay trên trình duyệt cả chuỗi.

## 10. Sổ việc chưa làm (lưu 19/10/2026 để tiếp tục ở các phiên sau)
**Chu trình doanh thu** (mục 2): giá vốn theo mặt hàng trong phân tích tiêu thụ; biên bản và phê duyệt hàng trả lại/giảm giá; thông báo nợ quá hạn (in/gửi) và xóa nợ có phê duyệt; đơn ngoại tệ lập hóa đơn từ phiếu giao hàng (cần tỷ giá); liên kết số phiếu giao hàng hiện trên phiếu xuất kho ở màn Kho; đơn giao trễ (backorder) thành lệnh bán hàng tiếp theo tự động.
**Chu trình cung ứng** (mục 3): nút tạo đơn mua nháp từ danh mục cần đặt hàng (danh mục đã có); biên bản kiểm nghiệm và số lượng thực nhận so với đặt; xếp hạng uy tín và phê duyệt nhà cung cấp mới; màn đối chiếu nhiều bên (hóa đơn – đơn – phiếu giao hàng NCC – biên bản kiểm nghiệm – phiếu nhập); chiết khấu thanh toán và kế hoạch tiền theo ngày đáo hạn; hóa đơn mua ngoại tệ vào hàng chờ.
**Thanh toán / tài chính**: lệnh thanh toán lương (`PAYROLL_PAYMENT`), thu tiền khách hàng (`RECEIPT_EXPECTED`), xuất file UNC theo mẫu ngân hàng, hạn mức duyệt theo vai trò.
**Chuyển đổi**: kế hoạch sản xuất, định mức, lệnh sản xuất, phân bổ chi phí chung (chờ mô tả quy trình dăm).
**Nền**: màn hình theo chu trình thay menu phẳng (mới có màn Mua hàng → Thanh toán); chiều phân tích (hợp đồng/lô tàu, bộ phận, khoản mục phí, đối tượng tập hợp chi phí) kế thừa dọc chuỗi; quy tắc bộ mã và mã tự kiểm tra; khóa màn hình khi rời máy.
**Các phân hệ làm trước khi theo chu trình** (Chứng từ, Thu/chi, Công nợ, Kho, Thuế GTGT, Báo cáo, Tổng quan) chưa gắn vào dòng chảy chu trình; cần rà để mỗi chứng từ ở đó thuộc đúng một khâu và nêu rõ ghi/không ghi Nhật ký.
**Còn dang dở từ các đợt trước:** tạm ứng/hoàn ứng, kiểm kê quỹ, đơn vị tính quy đổi và lô, bộ đọc XML hóa đơn chưa thử trên tệp thật, 2FA, DPAPI cho khóa trao đổi, sao lưu tự động, bộ cài, đối chiếu dữ liệu thật.

