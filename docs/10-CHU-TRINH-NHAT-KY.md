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

## 9d. Đã làm đợt 4 (19/10/2026): cung ứng hoàn thiện, menu theo chu trình
- **Biên bản kiểm nghiệm** (`BBKN{năm}-{số}`, không ghi Nhật ký): ghi số lượng thực nhận đạt từng dòng phiếu nhập kho; dòng không nêu thì đạt toàn bộ; mỗi phiếu nhập có tối đa một biên bản còn hiệu lực (hủy kèm lý do để lập lại); in biên bản. Phần không đạt xử lý bằng hàng mua trả lại / giảm giá hàng mua đã có. Màn Mua hàng → Thanh toán có danh sách phiếu nhập và nút lập biên bản.
- **Đối chiếu nhiều bên** (`PurchaseMatchService`, tệp mục V.2.2.3): hóa đơn ↔ đơn đặt hàng ↔ phiếu nhập ↔ biên bản kiểm nghiệm. Cờ **chặn**: số lượng hóa đơn vượt số lượng đạt trên biên bản; đơn giá hóa đơn khác đơn giá đơn đặt hàng (cùng cơ sở tính giá). Cờ thông tin: không gắn phiếu nhập, không theo đơn, chưa có biên bản.
- **Chấp nhận thanh toán bị chặn khi còn lệch** (`PAYMENT_MATCH_FAILED`, ở `PaymentAcceptanceService`, tức ở backend): người có quyền chỉ chấp nhận được khi xác nhận lệch **kèm lý do**, được ghi nhật ký kiểm toán (`mismatchAcknowledged`). Hàng chờ thanh toán hiện số chỗ lệch từng hóa đơn.
- **Menu theo chu trình**: Doanh thu (Đơn hàng, Bán hàng → Thu tiền, Công nợ) · Cung ứng (Mua hàng → Thanh toán, Hóa đơn mua/bán, Kho) · Chuyển đổi (Giá thành) · Nhân sự – tiền lương (Lương) · Tài chính, thanh toán (Thu/Chi tiền, Ngân hàng, Ngoại tệ, Vay, Thuế GTGT, Tài sản/CCDC — chuyển từ Chuyển đổi ở đợt 8) · Kế toán tổng hợp (Chứng từ, Sổ Nhật ký chung, Sổ chi tiết, Cân đối phát sinh, Báo cáo theo mẫu) · Danh mục và hệ thống.
- Bảng `goods_inspections`, `goods_inspection_lines` (migration `Phase7Inspection`, SQLite và PostgreSQL). Test: 1 test tích hợp (183 → 184); thử tay trên trình duyệt.

## 9e. Đã làm đợt 5 (19/10/2026): kế hoạch tiền và thông báo nợ quá hạn
- **Kế hoạch tiền theo ngày đáo hạn** (`CashPlanService`, `GET /api/cycles/finance/cash-plan`, tệp mục V.2.2.3): số dư tiền mặt + ngân hàng đã ghi sổ làm đầu kỳ; thu dự kiến từ hóa đơn bán còn nợ và chi dự kiến từ hóa đơn mua còn nợ, xếp theo ngày đáo hạn vào các khoảng "Quá hạn / đến hạn hôm nay", từng tuần (mặc định 8 tuần), "Sau đó", "Chưa có hạn"; tách phần chi đã chấp nhận thanh toán; số dư tiền lũy kế, cảnh báo khi âm. Hiện ở màn Tổng quan, xuất Excel và in. Là dự báo từ hạn thanh toán, không phải cam kết; hóa đơn ngoại tệ chưa tính.
- **Thông báo nợ quá hạn** (`SalesReportService.OverdueNoticesAsync`, `GET /api/cycles/revenue/overdue`, tệp mục V.1.1.2.9): nợ quá hạn theo khách hàng (hóa đơn, hạn, số ngày quá hạn, còn nợ), khách nợ lâu nhất trước; in thông báo gửi khách có địa chỉ, điện thoại, tổng bằng chữ, chỗ ký. Hiện ở màn Bán hàng → Thu tiền.
- Không có migration mới. Test: 1 test tích hợp (184 → 185); thử tay trên trình duyệt.

## 9f. Đã làm đợt 6 (19/10/2026): nhà cung cấp và đặt hàng từ danh mục cần đặt
- **Duyệt nhà cung cấp** (`SupplierService`, `BusinessPartner.SupplierStatus` Approved/Pending/Blocked, `Company.RequireSupplierApproval`): công ty bật chính sách thì nhà cung cấp mới tạo ở trạng thái Chờ duyệt; đơn mua (lập và xác nhận) chỉ cho nhà cung cấp Đã duyệt (`SUPPLIER_NOT_APPROVED`). Duyệt cần quyền duyệt chứng từ và người duyệt khác người tạo; chặn / đưa về chờ duyệt cần quyền danh mục và lý do. Mọi thay đổi vào nhật ký kiểm toán.
- **Xếp hạng uy tín** (`GET /api/cycles/procurement/suppliers/rating`): tính khi xem từ phiếu nhập đã ghi sổ trong kỳ, biên bản kiểm nghiệm và hạn giao của đơn mua, không lưu. Điểm = 60% tỷ lệ hàng đạt kiểm nghiệm + 40% tỷ lệ phiếu nhập đúng hạn (có một chỉ tiêu thì lấy chỉ tiêu đó); hạng A ≥ 90, B ≥ 75, còn lại C. Trọng số và ngưỡng là đề xuất của em, chưa được chị xác nhận.
- **Đơn mua nháp từ danh mục cần đặt** (`POST /api/cycles/procurement/reorder-list/create-order`): chọn dòng, nhà cung cấp, số lượng và đơn giá (gợi ý theo đơn mua gần nhất bằng VND, tính theo số lượng) → đơn mua Nháp, người khác xác nhận như thường.
- Màn Mua hàng → Thanh toán có thêm bảng nhà cung cấp (trạng thái, điểm, hạng, nút duyệt / chặn) và chọn dòng để tạo đơn nháp. Migration `Phase7SupplierApproval` (SQLite + PostgreSQL). 1 test tích hợp (185 → 186). Chưa thử tay trên trình duyệt.

## 9g. Đã làm đợt 7 (19/10/2026): giá vốn trong phân tích tiêu thụ, hàng chờ trả lương
- **Giá vốn và lãi gộp theo mặt hàng / khách hàng / tháng** (`SalesReportService.AnalyseAsync`): giá vốn dòng hóa đơn bán = giá trị đã tính của dòng phiếu xuất kho liên kết × phần số lượng hóa đơn bao phủ, hàng trả lại trừ ngược; dòng chưa tính giá xuất (bình quân cuối tháng chưa chạy) được đếm riêng và báo ở màn hình (giá vốn khi đó đang thiếu); dòng dịch vụ không có giá vốn. Không trừ chênh lệch giá hóa đơn mua đã điều chỉnh giá vốn.
- **Hàng chờ trả lương** (`PayrollPaymentService`, `GET /api/cycles/finance/payroll-payable`, `POST …/orders`, tệp mục V.3 bàn giao lương → thanh toán): phải trả người lao động còn nợ theo từng người (vai trò TK `PAYROLL_PAYABLE`), chọn các khoản để lập lệnh thanh toán lương (sự kiện `PAYROLL_PAYMENT`) ở trạng thái Nháp, đúng số còn nợ; khoản đã có lệnh còn hiệu lực không lập lại, hủy lệnh thì khoản lại chờ. Người khác duyệt ở màn Ngân hàng như mọi lệnh chi. Quy tắc hạch toán sự kiện `PAYROLL_PAYMENT` kế toán phải khai báo (chưa seed, vì chưa xác minh TK). Chưa có số tài khoản nhận của nhân viên trong danh mục nên lệnh chưa mang thông tin ngân hàng thụ hưởng.
- **Dải các khâu** (`FlowStrip` dùng chung) cho Nhân sự – lương (màn Lương: nhận bảng lương → kiểm tra gói → chứng từ lương → hàng chờ trả lương → ngân hàng chi) và Tài chính – thanh toán (màn Ngân hàng: lệnh Nháp → duyệt → gửi ngân hàng → ngân hàng chi + chứng từ → đối chiếu sao kê), mỗi khâu ghi rõ có/không ghi Nhật ký và số chứng từ đang chờ. Chu trình chuyển đổi chưa có dải khâu vì còn chờ mô tả quy trình dăm.
- Không có migration mới. Test: 1 test tích hợp mới + mở rộng test doanh thu (186 → 187). Thử tay trên trình duyệt (dữ liệu dev-seed): duyệt/chặn nhà cung cấp, tạo đơn mua nháp từ danh mục cần đặt (tạo được đơn DMH2026-000001; nhà cung cấp bị chặn thì không chọn được), dải khâu lương và ngân hàng hiện đúng. Chưa thử tay hàng chờ trả lương có dữ liệu và cột giá vốn.

## 9h. Đã làm đợt 8 (19/10/2026): chu trình quản lý tài sản cố định, công cụ dụng cụ
Trước đợt này phân hệ Tài sản / CCDC mới có thẻ, khấu hao, phân bổ, ghi giảm (làm ngày 15/10, theo phân hệ) và bị xếp nhầm vào menu "Chuyển đổi". Tệp phân tích xếp **mua sắm tài sản cố định, đầu tư xây dựng vào chu trình tài chính**, "quản lý tài sản cố định" là một phân hệ ERP riêng, và **kiểm kê tài sản định kỳ** là kiểm soát phát hiện. Đợt này dựng thành chu trình đủ khâu (`AssetCycleService`, `AssetRequest`, `AssetAssignment`, `AssetCount`):
| Khâu | Ghi Nhật ký | Trạng thái |
|---|---|---|
| Đề nghị mua sắm / đầu tư (duyệt, người duyệt khác người lập, từ chối cần lý do) | Không | mới |
| Mua qua chu trình cung ứng (hóa đơn mua ghi sổ → bút toán nợ TK tài sản) | Có | đã có |
| Lập thẻ, ghi tăng (nguyên giá lấy từ bút toán đã ghi sổ); gắn thẻ vào đề nghị đã duyệt → "Đã lập thẻ" | Ghi tăng chỉ đóng băng thẻ | gắn đề nghị: mới |
| Bàn giao / điều chuyển cho bộ phận, người giữ, vị trí (lần sau đóng lần trước, giữ lịch sử) | Không | mới |
| Khấu hao / phân bổ hằng kỳ | Có | đã có |
| Kiểm kê tài sản (phiếu lập sẵn từ tài sản còn hiệu lực tại ngày kiểm; kết quả Có / Không thấy / Hỏng, bộ phận thực tế; mất / hỏng bắt buộc ghi chú; chốt khi đã kiểm hết, người chốt khác người lập; báo số mất, hỏng, khác bộ phận và giá trị còn lại của tài sản mất) | Không | mới |
| Ghi giảm / thanh lý (tài sản mất sau điều tra xử lý ở đây, không tự ghi sổ từ kiểm kê) | Có | đã có |
- API dưới `/api/assets` (`/requests`, `/assignments`, `/{id}/assign`, `/counts`); màn Tài sản / CCDC có dải khâu và ba bảng mới; **menu chuyển sang nhóm Tài chính, thanh toán** theo tệp. Khấu hao vẫn là chi phí của chu trình chuyển đổi nên khâu đó ghi rõ "→ chi phí".
- Migration `Phase7AssetCycle` (SQLite + PostgreSQL). Bảng mới không có trigger bất biến vì không phải dữ liệu sổ. 1 test tích hợp (187 → 188); thử tay trên trình duyệt (lập đề nghị, bàn giao, kiểm kê có chênh lệch bộ phận).
- Chưa làm: in biên bản kiểm kê / biên bản bàn giao; đề nghị sửa chữa, nâng cấp, thay đổi thông số thẻ (thẻ đã ghi tăng đang bị đóng băng); gắn thẻ CCDC với nhiều người giữ; tự gợi ý ghi giảm từ kiểm kê mất.

## 9i. Đối chiếu với bài "Quy trình xử lý tổng hợp trong điều kiện hạch toán kế toán trên máy vi tính" (Nguyễn Mạnh Toàn, ĐH Đà Nẵng, 2011) — 19/10/2026
Chị gửi bài để tham khảo. Đối chiếu với hệ thống:
| Bài báo | Hệ thống HAK | Nhận xét |
|---|---|---|
| Kho thông tin chung = `NHATKY` (mỗi chứng từ một mẫu tin: loại, số, ngày, **họ tên, đơn vị**, diễn giải) + `CHITIETNK` (định khoản: số tiền, TK nợ, TK có, đối tượng chi tiết) | `Voucher` / `JournalEntry` + `JournalLine` | Khớp. HAK ghi mỗi dòng một vế Nợ hoặc Có (tổng quát hơn cặp Nợ–Có của bài, cho phép bút toán ghép). Thiếu **họ tên và đơn vị** ghi trên chứng từ → đã bổ sung (xem dưới). |
| Chứng từ gốc và bút toán điều chỉnh cùng đổ vào `CHITIETNK` | chứng từ nghiệp vụ và chứng từ điều chỉnh (`AdjustsVoucherId`), chứng từ đảo | Khớp. |
| `TH_TAIKHOAN`: số dư đầu năm, phát sinh nợ/có từng tháng, lũy kế, số dư cuối tháng → đầu tháng sau; cuối năm tự chuyển sang năm sau; lưu thành tập tin | Bảng cân đối phát sinh tính từ sổ cái khi xem (không lưu); số dư đầu năm sau lấy từ kết chuyển | Cùng kết quả với phương án 2 của bài (cộng dồn theo TK Nợ, TK Có rồi cập nhật) nhưng không lưu tập tin tổng hợp nên không lệch được với sổ chi tiết; giữ nguyên. **Cột lũy kế từ đầu năm chưa có** trên bảng cân đối phát sinh — ghi vào sổ việc. |
| Số dư cuối kỳ: dư nợ nếu (đầu kỳ + PS nợ − PS có) ≥ 0, ngược lại dư có | giống, kèm chính sách hiển thị theo TK (`BalanceDisplay`: thuần / gộp theo đối tượng) | Khớp, HAK linh hoạt hơn với TK công nợ. |
| Từ `TH_TAIKHOAN` lập Bảng CĐKT, báo cáo KQHĐKD, báo cáo LCTT | Report Engine + mẫu B01, B02, B03 | Khớp. |
| Cập nhật tổng hợp ngay khi cập nhật chứng từ hoặc cuối tháng | ghi sổ trong một giao dịch, báo cáo tính theo yêu cầu | Khớp. |
- **Đã làm:** thêm vào chứng từ hai trường **họ tên người giao / nhận** và **đơn vị, địa chỉ** (`Voucher.CounterpartyName`, `CounterpartyUnit`, migration `Phase7VoucherCounterparty`), nhập ở màn Thu / Chi tiền và hiện trên phiếu thu, phiếu chi in ra (trước đây dòng địa chỉ để trống, tên chỉ lấy từ danh mục đối tượng). Test: mở rộng 1 test tích hợp.
- **Chưa làm:** cột lũy kế từ đầu năm trên bảng cân đối phát sinh.

## 9j. Đã làm đợt 9 (19/10/2026): giao diện ô ứng dụng, chu trình sản xuất, lịch trả nợ vay — theo 3 tài liệu chị gửi
Chị gửi ba tệp: *thiết kế chu trình sản xuất*, *thiết kế chu trình tài chính*, *kiến trúc cơ sở dữ liệu tổng thể ERP*. Đối chiếu và làm như sau.

**Giao diện kiểu Odoo (theo đề xuất, chị đồng ý):** màn Tổng quan có **ô ứng dụng** nhóm theo chu trình, mỗi ô kèm số việc đang chờ (`HomeService`, `GET /api/cycles/home/work-queue`, tính từ chứng từ nguồn, phân hệ không có quyền xem thì bỏ qua); chứng từ có **thanh trạng thái** Nháp → Chờ duyệt → Đã duyệt → Đã ghi sổ (đỏ khi đã đảo, đã hủy) với nút hành động ngay trên thanh (`StatusBar`). Chưa làm: nút thông minh nhảy sang chứng từ liên quan, danh sách – biểu mẫu hai khung, bảng việc kanban.

**Chu trình sản xuất** (`ProductionOrderService`, màn Sản xuất có 3 tab: Lệnh sản xuất, Định mức & tổ xưởng, Giá thành cuối kỳ):
| Tài liệu | HAK |
|---|---|
| `product_bom` (thành phẩm, NVL, số lượng định mức, tỷ lệ hao hụt) | `Bom` + `BomLine`, một định mức hiện hành cho mỗi thành phẩm, sửa có nhật ký kiểm toán |
| `work_centers` (đơn giá nhân công, máy + SXC theo giờ) | `WorkCenter` |
| `production_orders` Draft → Released → In-Progress → Completed → Closed | `ProductionOrder` (thêm Cancelled); người ban hành khác người lập; số lệnh LSX{năm}-{số} |
| Lập KHSX & MRP | Nhu cầu NVL theo định mức (kể cả hao hụt), đã xuất, còn phải xuất, tồn kho và phần thiếu |
| `production_material_issues` | Phiếu xuất kho thường (đối ứng TK dở dang của đối tượng tính giá thành, giá vốn do kho tính) gắn lệnh bằng `ProductionOrderIssue`; lập sẵn từ định mức |
| `production_labor_confirmations` | Báo công (giờ công, giờ máy), đơn giá chốt lúc báo, hủy được khi lệnh chưa hoàn thành |
| Tính giá thành: CP NVL (621) + nhân công (622) + SXC (627) = TK 154, giá đơn vị = tổng ÷ số lượng thực nhập | Hoàn thành lệnh: kiểm tra mọi phiếu xuất đã ghi sổ và có giá; ghi Nhật ký phân bổ **Nợ TK dở dang / Có vai trò `PROD_LABOR_APPLIED`, `PROD_OVERHEAD_APPLIED`**; lập phiếu nhập thành phẩm Nháp theo tổng giá thành (Có TK dở dang); chốt giá thành đơn vị; bảng giá thành và so sánh thực xuất với định mức; đóng lệnh khi phiếu nhập đã ghi sổ |
- Không dùng số hiệu TK pháp lý cố định (621, 622, 627, 154, 155): TK do kế toán trưởng ánh xạ qua vai trò, chờ xác minh. Hai vai trò mới `PROD_LABOR_APPLIED`, `PROD_OVERHEAD_APPLIED` chỉ bắt buộc khi lệnh có báo công. Quyền mới `production.manage`.
- Người hoàn thành lệnh lập phiếu nhập thành phẩm nên người khác ghi sổ (tách người). Giá thành kỳ (tab Giá thành cuối kỳ) vẫn dùng khi muốn chênh lệch cuối kỳ; lệnh đã nhập thành phẩm theo giá thực tế của lệnh nên chênh lệch kỳ gần bằng 0.
- Migration `Phase8ProductionOrders`. 1 test tích hợp đầy đủ luồng (định mức, ban hành, xuất, báo công, hoàn thành, ghi sổ, đóng); thử tay trên trình duyệt.

**Chu trình tài chính:**
| Tài liệu | HAK |
|---|---|
| `debt_contracts` (vay, lãi suất, ngày giải ngân, đáo hạn) | Đã có từ trước: khế ước vay, lãi suất theo ngày hiệu lực, gắn giải ngân / trả nợ |
| Giải ngân Nợ 112 / Có 341; trả nợ gốc Nợ 341 | Đã có (chứng từ thường gắn khế ước) |
| Trích trước lãi Nợ 635 / Có 335 | Đã có (trích lãi theo kỳ, 365 ngày) |
| `debt_schedules` (kỳ trả, gốc, lãi, Pending / Paid / Overdue) | **Mới:** `LoanScheduleItem`, sinh lịch trả gốc đều (lãi = dư nợ đầu kỳ × lãi suất × số ngày ÷ 365 hoặc 360), sửa tay, lưu; trạng thái suy ra từ gốc đã trả (phân bổ theo ngày đến hạn); màn Vay → Lịch trả nợ |
| Dự báo dòng tiền = số dư + thu dự kiến − chi dự kiến, chi gồm công nợ phải trả, lương, **lịch trả nợ vay** | Kế hoạch tiền ở Tổng quan nay tính cả các kỳ trả nợ vay VND chưa trả (gốc còn phải trả + lãi kỳ, coi như đã cam kết); chi lương chưa vào dự báo; khoản vay sắp giải ngân chưa vào |
| `chart_of_accounts`, `bank_accounts` (tên ngân hàng, số tài khoản, TK 112) | TK có sẵn; **số tài khoản và tên ngân hàng chưa có trong danh mục** (đã ghi vào sổ việc cùng STK nhận của nhân viên) |
| Vốn góp, cổ tức (`contract_type` Equity) | **Chưa làm** — cần chị mô tả quy trình |
- Migration `Phase8LoanSchedule`; 1 test tích hợp (sinh lịch, trạng thái theo trả gốc, đưa vào kế hoạch tiền); thử tay lưu lịch.

**Đối chiếu tài liệu kiến trúc tổng thể:**
- Khớp: ma trận định khoản (nhập kho NVL, xuất NVL vào sản xuất, nhập thành phẩm, giá vốn, hóa đơn bán, lương, giải ngân) đều có, đi qua vai trò TK thay cho số hiệu; Sổ cái tích hợp = Nhật ký + Chi tiết Nhật ký.
- Khác có chủ ý: trạng thái đơn mua / đơn bán, số đã trả của hóa đơn bán và trạng thái hóa đơn (`Unpaid / Partially_Paid / Paid`) suy ra từ chứng từ nguồn chứ không lưu cột (tránh lệch).
- Chưa có, ghi vào sổ việc: TK công nợ phải thu / phải trả riêng theo từng đối tác (`ar_account`, `ap_account`) và TK giá vốn riêng theo từng vật tư (`cogs_account`) — hiện dùng vai trò chung `AR_TRADE`, `AP_TRADE`, `COGS`; loại vật tư (nguyên liệu, bán thành phẩm, thành phẩm, dịch vụ); cột "phân hệ nguồn" (`ref_module`) trên Sổ Nhật ký chung và loại giao dịch kho (`PURCHASE_GR`, `PROD_ISSUE`, `PROD_RECEIPT`, `SALES_ISSUE`) trên thẻ kho.

## 9k. Đã làm đợt 10 (19/10/2026): khung giao diện theo mẫu chị gửi, phân tích tài chính, số dư ban đầu
Chị gửi ảnh giao diện mẫu. Đã dựng:
- **Thanh bên theo phân hệ** (Tiền, Mua hàng, Bán hàng, Kho, Sản xuất, Tài sản – CCDC, Tiền lương, Thuế, Tổng hợp, Phân tích tài chính, Số dư ban đầu, Danh mục – hệ thống), mỗi mục có số việc đang chờ. Bên trong là **tab**: tab đầu **Quy trình** (các nghiệp vụ nối nhau trên một trục theo thứ tự các khâu của chu trình, xen kẽ trên và dưới; bấm nút mở đúng tab), bên phải danh sách **Báo cáo**, hàng **lối tắt danh mục** bên dưới; các tab sau là các màn hình hiện có. Cấu trúc khai báo ở `web/src/Modules.tsx`.
- **Giữ nguyên** theo yêu cầu của chị: màn **Việc cần làm** (ô theo chu trình kèm số việc chờ), và mọi màn **phiếu, lệnh, nhập liệu** (chỉ đổi khung bên ngoài; riêng chứng từ kế toán có thanh trạng thái ở mục 9j).
- **Thêm nhanh** ở đầu thanh bên: bảng lối tắt tới màn lập chứng từ của từng phân hệ.
- **Số dư ban đầu**: lưới thẻ theo loại dữ liệu (tài khoản, đối tượng, vật tư, số dư và công nợ, tồn kho, TSCĐ – CCDC, chứng từ), bấm thẻ vào đúng màn nhập Excel / CSV đã có.
- **Tổng quan**: thêm hai thẻ **Nợ phải thu / Nợ phải trả theo hạn nợ** (tổng, quá hạn, trong hạn, thanh tỷ lệ quá hạn); tuổi nợ chi tiết thu gọn.
- **Phân tích tài chính** (`FinancialAnalysisService`, `FinancialRatios`, `GET /api/analysis/ratios`): 15 chỉ số (hệ số nợ, vốn tự có, nợ trên vốn chủ, nợ dài hạn; thanh toán hiện hành, nhanh, tức thời, lãi vay; vòng quay hàng tồn kho, số ngày lưu kho, vòng quay phải thu, kỳ thu tiền; lợi nhuận gộp, ROS, ROA, ROE) cho kỳ này so với kỳ liền trước cùng độ dài, tính từ **mẫu B01-DN và B02-DN đã nhập theo mã số chỉ tiêu** (không tự lấy tài khoản; chưa nhập mẫu thì báo rõ). Mã số lấy theo mẫu TT99 trong `legal/TT99-2025`, chờ chị xác minh mẫu. Không có cột "Tiêu chuẩn" như mẫu của chị vì ngưỡng tham chiếu doanh nghiệp tự quyết định; nếu chị cho ngưỡng em thêm cột và tô màu. Vòng quay tính theo kỳ chọn, không quy năm; số trung bình hai đầu kỳ chưa dùng (đang dùng số cuối kỳ).
- Test: 1 test miền cho công thức chỉ số, 1 test tích hợp (mẫu thử → chỉ số, kỳ trước, thiếu mẫu); thử tay trên trình duyệt.
- **Cập nhật theo yêu cầu của chị (cùng ngày):** màn **Tổng quan** làm như mẫu — thẻ *Tình hình tài chính* (tổng tiền, tiền mặt, tiền gửi, phải thu, phải trả, doanh thu, chi phí, lợi nhuận, hàng tồn kho), thẻ *Nợ phải thu / phải trả theo hạn nợ*, biểu đồ *Doanh thu, chi phí, lợi nhuận* theo tháng, *Hàng hóa tồn kho*, *Doanh thu*, *Mặt hàng bán chạy*, *Dòng tiền* (thu, chi, tồn), *Chi phí* (cơ cấu); phía dưới vẫn giữ kế hoạch tiền, tiền theo tài khoản, kiểm soát. Số liệu từ `DashboardOverviewService` (`GET /api/dashboard/overview`): tiền, công nợ, tồn kho, bán chạy lấy từ sổ đã ghi; **doanh thu, chi phí, lợi nhuận, cơ cấu chi phí lấy từ mẫu B02-DN đã nhập theo mã số chỉ tiêu** (doanh thu = 10 + 21 + 22 + 31, chi phí = 11 + 23 + 25 + 26 + 32 + 51 + 52), chưa có mẫu thì để trống và báo rõ. Biểu đồ chỉ để trực quan (vẽ bằng số làm tròn), số chính xác ở các thẻ và bảng. 1 test tích hợp mới.
- Màn **Việc cần làm** (ô theo chu trình kèm số việc chờ) **chuyển sang mục mới "Quản lý chu trình"** ở thanh bên, nội dung giữ nguyên như chị dặn.
- Chưa làm so với mẫu: thanh tìm kiếm thông minh, "Hay dùng" ghim chức năng, khối "AVA" gợi ý bằng AI.

### Bảng xếp các phân hệ vào khâu của chu trình (ghi / không ghi Nhật ký)
| Màn hình | Chu trình, khâu | Ghi Nhật ký |
|---|---|---|
| Đơn hàng (bán) | Doanh thu: nhận đơn + kiểm tra tín dụng, tồn kho | Không |
| Bán hàng → Thu tiền | Doanh thu: lệnh bán hàng (không); phiếu giao hàng → phiếu xuất kho (khi ghi sổ phiếu xuất: có); hóa đơn bán (có); thu tiền (có) | Theo khâu |
| Công nợ | Doanh thu / Cung ứng: theo dõi phải thu, phải trả; phân bổ; đối chiếu | Phân bổ không sinh bút toán |
| Đơn hàng (mua), Mua hàng → Thanh toán | Cung ứng: danh mục cần đặt, đơn mua (không); biên bản kiểm nghiệm (không); chấp nhận thanh toán (không); lệnh thanh toán (không); ngân hàng chi (có) | Theo khâu |
| Kho | Cung ứng / Doanh thu: phiếu nhập (có), phiếu xuất (có), kiểm kê (phiếu điều chỉnh: có), tính giá cuối tháng (có) | Có |
| Hóa đơn mua / bán | Cung ứng, Doanh thu: hóa đơn (có), hàng trả lại, giảm giá (có) | Có |
| Giá thành sản xuất | Chuyển đổi: nhập thành phẩm, tính giá thành (có) | Có |
| Tài sản / CCDC | Tài chính: đề nghị mua sắm (không), mua (có), bàn giao (không), khấu hao / phân bổ (có), kiểm kê (không), ghi giảm (có) — chi tiết ở mục 9h | Theo khâu |
| Lương | Nhân sự – lương: nhận bảng lương → chứng từ (có) | Có |
| Thu / Chi tiền, Ngân hàng, Ngoại tệ, Vay | Tài chính: phiếu thu/chi, lệnh thanh toán, sao kê, đánh giá lại, trích lãi (có, riêng lệnh thanh toán và sao kê chưa ghi) | Theo chứng từ |
| Thuế GTGT | Báo cáo (kết quả): đối chiếu 3 nguồn, bảng kê; không ghi Nhật ký | Không |
| Chứng từ kế toán, Sổ Nhật ký chung, Sổ chi tiết, Cân đối phát sinh, Báo cáo theo mẫu | Kế toán tổng hợp: Nhật ký (chuyển đổi) và sổ, báo cáo (kết quả) | — |

## 9l. Đã làm đợt 11 (19/10/2026): theo dõi 2 đơn vị tính

Chị chốt: dăm gỗ vẫn theo chu trình bình thường, chỉ khác là theo dõi **2 đơn vị tính**: (1) quy đổi **cố định**; (2) quy đổi **theo thời điểm** (dăm gỗ: hệ số = độ khô tại thời điểm). Tham chiếu cách quản lý của HAK_KHO_OFFLINE (chỉ đọc).
- Cố định: `Item.AltUnit` + `AltFactor` (1 ĐVT phụ = hệ số ĐVT chính); sổ kho vẫn giữ ĐVT chính, số lượng quy đổi hiển thị ở tồn kho/thẻ kho và nhập được ở dòng phiếu kho. Vật tư đo độ khô không đặt được đơn vị cố định (loại trừ nhau).
- Theo thời điểm: BDMT = MT × độ khô. Xuất/chuyển kho chưa đo được **ước tính theo độ khô bình quân thời điểm** (BDMT tồn ÷ MT tồn lúc xuất), báo riêng với phần đã đo.
- Chưa làm (chờ chị quyết): chính sách "Thực tế / Bình quân thời điểm" theo kho, phiếu chênh lệch CL, lô, mượn/trả như HAK_KHO_OFFLINE.

## 9m. Đã làm đợt 12 (19/10/2026): bảo trì & sửa chữa tài sản (theo tệp CMMS chị gửi) và Nhật ký tài sản

- **Yêu cầu sửa chữa / báo sự cố (YCSC{năm}-{số})**: người vận hành lập, có mức ưu tiên (Thấp/Trung bình/Cao/Khẩn cấp); trạng thái Mới → Đang khảo sát → Đã đề xuất → Đã duyệt → Đang sửa → Hoàn tất (hoặc Từ chối).
- **Phiếu thi công (SCTS{năm}-{số})** = work order: loại sửa chữa khẩn cấp / bảo trì phòng ngừa; nguyên nhân, phương án, kỹ thuật viên, đơn vị sửa ngoài; dự toán nhân công + vật tư (dòng vật tư, đơn giá). Luồng: Nháp → Chờ duyệt → Đã duyệt (người duyệt khác người lập, từ chối cần lý do) → Đang thi công (ghi giờ bắt đầu) → xuất vật tư (lập **phiếu xuất kho Nháp** đối ứng TK chi phí chọn lúc xuất, ghi sổ ở màn Kho) → Chờ nghiệm thu (nhân công thực tế, giờ kết thúc) → nghiệm thu đạt / không đạt (trả về thi công) → Đã đóng (cập nhật yêu cầu gốc và lịch bảo trì). Không ghi Nhật ký kế toán; không đổi nguyên giá, lịch khấu hao.
- **Lịch bảo trì phòng ngừa** (chu kỳ ngày, hạn kế tiếp, cảnh báo quá hạn): "Lập phiếu bảo trì" từ lịch đến hạn (mỗi lịch một phiếu đang mở); đóng phiếu thì dời hạn = ngày hoàn thành + chu kỳ.
- **KPI bảo trì** theo máy và kỳ: số sự cố, giờ sửa, MTTR = giờ sửa ÷ sự cố, MTBF = (giờ kế hoạch − giờ dừng) ÷ sự cố, tỷ lệ dừng máy %, chi phí nhân công + vật tư (phiếu đã nghiệm thu), kèm bộ phận đang giữ máy.
- **Nhật ký tài sản** (`AssetJournalService`, nút "Nhật ký" ở từng dòng sổ tài sản = Sổ lý lịch thiết bị): một dòng thời gian gộp mọi thông tin và biến động — lập thẻ, đề nghị mua sắm, nguồn nguyên giá, ghi tăng, bàn giao/điều chuyển, YCSC, phiếu thi công (lập/duyệt/bắt đầu/xuất vật tư/nghiệm thu/đóng/hủy), kiểm kê, khấu hao/phân bổ từng kỳ (dòng đã hủy gạch ngang), ghi giảm, hủy ghi giảm, hủy thẻ; tóm tắt nguyên giá, hao mòn, giá trị còn lại, số lần và chi phí sửa chữa; lọc theo nhóm.
- Chưa làm so với tệp CMMS: quét mã QR và app di động / thông báo đẩy, ảnh/video sự cố, danh mục loại thiết bị riêng (dùng thẻ tài sản làm danh mục máy), báo cáo tồn kho & dự báo vật tư bảo trì, tải công việc kỹ thuật viên (kỹ thuật viên đang là tên nhập tay, chưa gắn người dùng), tự cập nhật trạng thái máy BROKEN/MAINTENANCE (thẻ tài sản không có các trạng thái này), duyệt theo hạn mức ngân sách lớn.
- Sửa chữa lớn làm tăng nguyên giá: chờ chị xác nhận cách hạch toán.

## 9n. Đã làm đợt 13 (19/10/2026): quản lý hợp đồng mua bán (theo tệp phân tích chị gửi)

- **Hợp đồng** (`Contract`, màn "Hợp đồng" trong nhóm Mua hàng và Bán hàng): mã CTR-SAL-{năm}-{số 5 chữ số} / CTR-PUR-…; loại khung số lượng / khung giá trị / tiêu chuẩn; đối tác đúng vai trò (bán: khách hàng, mua: nhà cung cấp); dòng hàng cam kết (số lượng, đơn giá, chiết khấu %, thuế %); mốc thanh toán; điều khoản; đồng tiền, tỷ giá. Không ghi Nhật ký. Không xóa mềm (không xóa; hủy có lý do) vì phần mềm giữ nguyên tắc không xóa chứng từ.
- **Luồng 5 giai đoạn**: Nháp (sửa được, mỗi lần sửa tăng số phiên bản) → Chờ duyệt → Hiệu lực (người duyệt khác người lập; từ chối cần lý do, trả về Nháp) → Thanh lý (Completed) hoặc Hủy (có lý do; không hủy khi đã có đơn kế thừa). "Hết hạn" không lưu: hợp đồng hiệu lực quá hạn hiển thị "Hết hạn, chưa thanh lý" và có cảnh báo.
- **Đơn kế thừa (call-off) và trừ lùi**: đơn mua / đơn bán có thêm `ContractId` (ô "Theo hợp đồng" tùy chọn ở màn Đơn hàng, tự điền đối tác, số hợp đồng và đơn giá sau chiết khấu). Khi lập và khi xác nhận đơn: chặn nếu hợp đồng chưa hiệu lực, sai đối tác / loại, ngày đơn ngoài hiệu lực (CONTRACT_EXPIRED), vật tư ngoài danh mục cam kết, vượt số lượng (khung số lượng) hoặc giá trị (khung giá trị) cam kết (CONTRACT_LIMIT_EXCEEDED). Số đã thực hiện = tổng các đơn không hủy gắn với hợp đồng (một nguồn dữ liệu, không có bảng nhật ký trừ lùi riêng); hủy đơn tự trả lại số lượng. Đơn giá trên đơn vẫn sửa được để báo cáo chênh lệch giá.
- **Phụ lục** `{mã HĐ}-A{số 2 chữ số}` cho hợp đồng hiệu lực: tăng/giảm giá trị, đổi số lượng cam kết theo dòng (không thấp hơn số đã đặt), gia hạn. **Mốc thanh toán**: ghi nhận cộng dồn, tình trạng Chưa đến hạn / Một phần / Đã thanh toán / Quá hạn. **Tài liệu**: lưu tên và đường dẫn tệp (không lưu nội dung tệp).
- **Báo cáo, cảnh báo**: tiến độ thực hiện (cam kết / đã đặt / còn lại / %), lịch thanh toán theo hợp đồng, chênh lệch giá (giá cam kết sau chiết khấu so với giá bình quân, thấp nhất, cao nhất trên đơn); cảnh báo trước 60 / 30 ngày đến hạn, đã hết hạn, mốc quá hạn, dùng ≥ 90% hạn mức.
- Chưa làm so với tệp: duyệt nhiều cấp (Pháp chế → Ban giám đốc; hiện một cấp), mẫu hợp đồng, hợp đồng con liên kết hợp đồng mẹ, lưu nội dung tệp scan, ghi nhận thanh toán mốc tự động từ phiếu thu/chi (hiện nhập tay), đưa cảnh báo hợp đồng vào "Việc cần làm", hạn mức công nợ lấy từ hợp đồng.
- Lưu ý kỹ thuật: kiểm tra kiểu web phải chạy `npm --prefix web run build` (lệnh `tsc --noEmit -p .` bỏ qua vì tsconfig dạng tham chiếu). KPI/báo cáo truyền thời điểm cho PostgreSQL phải là UTC.

## 9o. Đã làm đợt 14 (20/10/2026): bộ lọc thông minh dùng chung và rà thiết kế

- **SmartFilter** (`web/src/SmartFilter.tsx` + `filter.ts`, có test): ô tìm không phân biệt hoa thường và dấu tiếng Việt, nhiều từ (và), "cụm từ" trong ngoặc kép, so sánh số tiền (`>500tr`, `<=2ty`, `>=1,5tr`, `=100k`; đơn vị k, tr, ty), danh sách chọn, nút lọc nhanh có số đếm (bấm lần nữa để bỏ), nút Xóa lọc, bộ đếm kết quả, nhớ lựa chọn theo từng màn hình trên máy (localStorage). Chỉ lọc hiển thị, không dùng tính sổ.
- Áp dụng: Sổ tài sản (đang dùng, đã khấu hao hết, chưa ghi tăng, đã ghi giảm), Hợp đồng (sắp hết hạn ≤ 60 ngày, đã hết hạn, dùng ≥ 90% hạn mức, chờ duyệt), Đơn hàng (chờ xác nhận, đang thực hiện, theo hợp đồng), Yêu cầu sửa chữa (ưu tiên cao/khẩn cấp, chưa có phương án), Phiếu thi công (chờ duyệt, đang thi công, chờ nghiệm thu, bảo trì định kỳ). Các bộ lọc có sẵn ở Chứng từ, Danh mục, Thiết lập tài khoản, Nhật ký kiểm toán giữ nguyên (chưa chuyển sang SmartFilter).
- **Thiết kế**: sửa lỗi điện thoại — quy tắc khung `.shell` 230px về sau đè quy tắc màn hình nhỏ nên trang tràn ngang ~1400px; nay menu thành thanh ngang cuộn được, bảng cuộn ngang trong khung, form xếp dòng. Kiểm tra tràn ngang ở 1500px và 390px, sáng và tối cho Tài sản, Hợp đồng, Đơn hàng: không còn tràn.
- Chưa làm: lưu bộ lọc đặt tên (bộ lọc của tôi), lọc theo khoảng ngày trong SmartFilter, chuyển các bộ lọc cũ sang SmartFilter, rà thiết kế điện thoại cho mọi màn còn lại.

## 9p. Đợt 15 (20/10/2026): định dạng số, lọc theo khoảng ngày, rà thiết kế toàn bộ màn hình

- **Định dạng số** (1.234.567,5: chấm ngăn nhóm, phẩy thập phân): giao diện dùng `formatMoney`. Đã quét mọi tab: sửa các chỗ còn hiện kiểu máy (5.56, 665.5, 95.5%) ở hợp đồng, KPI bảo trì, xếp hạng nhà cung cấp; ngày giờ ở Quản trị dùng vi-VN. Thông báo từ máy chủ (vượt hạn mức, chênh lệch, lịch vay…) trước đây ra "540,000,000" do InvariantGlobalization; nay dùng `VnNumber.Vn()` (Hak.Domain.Common) cho cùng kiểu với giao diện. **Ngày hiển thị 19/10/2026** (chị chốt 20/10): web dùng `formatDate` / `formatDateTime` / `formatMonth` (money.ts) cho mọi bảng và nhãn (đã quét tự động, chỉ còn dữ liệu JSON thô ở Nhật ký kiểm toán); máy chủ dùng `DateOnly.Vn()` trong thông báo (DateOnly.ToString() bất biến ra 10/19/2026, dễ đọc nhầm). Ô chọn ngày (type=date) theo ngôn ngữ trình duyệt; xuất Excel vẫn ghi ngày dạng yyyy-MM-dd để sắp xếp đúng.
- **Lọc theo khoảng ngày** trong SmartFilter: "Từ ngày — Đến ngày" mặc định là **ngày đầu tháng hiện hành đến ngày hiện hành** (tính lại mỗi lần mở màn, không lưu), mốc nhanh Hôm nay / Tuần này / Tháng này / Tháng trước / Quý này / Năm nay / Tất cả ngày, "Xóa lọc" trả về mặc định. Áp dụng cho Đơn hàng (ngày đơn), Hợp đồng (hiệu lực giao với khoảng), YCSC (ngày báo, đổi theo giờ địa phương), Phiếu thi công (ngày lập). Sổ tài sản là số dư tại ngày nên không lọc theo khoảng ngày.
- **Mặc định ngày ở mọi màn cũ** cũng đã thống nhất đầu tháng → hôm nay (Doanh thu, Kho, Lương, Nhật ký kiểm toán). **Riêng Báo cáo theo mẫu (B01, B02, B03…) giữ mặc định từ đầu năm đến hôm nay** (báo cáo tài chính xem lũy kế; chị chốt 20/10), kỳ so sánh là cùng kỳ năm trước. Đã quét tự động toàn bộ tab: không còn ô "Từ ngày" nào khác mặc định.
- **Thiết kế**: quét 45 tab ở 1500px và 390px: không còn tràn ngang. Sửa: nội dung rộng (Hóa đơn, Kho) cuộn trong khung; tab Quy trình xếp dọc trên điện thoại; thẻ Tổng quan và biểu đồ co theo bề rộng.

## 9q. Đợt 16 (20/10/2026): chia chi phí khấu hao, phân bổ trả trước cho nhiều bộ phận

- **Cách chia** (`AssetExpenseSplit`): mỗi thẻ (TSCĐ hoặc CCDC / chi phí trả trước như bảo hiểm, thuê nhà xưởng) có thể khai nhiều dòng "tài khoản chi phí + bộ phận + tỷ lệ %", tổng đúng 100% (tối đa 4 chữ số thập phân, không trùng tài khoản + bộ phận, tài khoản phải là chi tiết, không phải tiền / công nợ / bắt buộc đối tượng). Không khai thì dùng tài khoản chi phí của thẻ. Đặt được khi thẻ Nháp hoặc đang dùng; ghi nhật ký kiểm toán. Màn "Tài sản, CCDC" → nút "Chia chi phí" ở từng thẻ.
- **Khi chạy kỳ**: số khấu hao / phân bổ của thẻ được ghi Nợ vào từng tài khoản chi phí theo tỷ lệ (một dòng bút toán mỗi phần, diễn giải kèm tên bộ phận), Có vẫn một dòng vào tài khoản trả trước / hao mòn. Làm tròn từng phần theo số lẻ của công ty, **phần chênh dồn vào dòng có tỷ lệ lớn nhất** (bằng nhau thì dòng đầu) nên tổng luôn đúng. Phần phân bổ cuối khi ghi giảm cũng chia theo cách đang đặt. Đổi cách chia chỉ áp dụng cho các kỳ chạy sau; kỳ đã chạy giữ nguyên.
- **Bất biến**: mỗi dòng khấu hao có bản ghi chia (`DepreciationEntrySplit`) chỉ thêm, không sửa, không xóa (interceptor SQLite + trigger PostgreSQL); hủy kỳ thì bản ghi chia theo dòng cha bị loại khỏi báo cáo.
- **Báo cáo** "Chi phí khấu hao, phân bổ theo bộ phận": theo tài khoản và bộ phận, lọc khoảng ngày (mặc định đầu tháng → hôm nay; lấy các kỳ khấu hao giao với khoảng ngày).
- Chưa làm: chia theo đối tượng chi phí / sản phẩm (chiều phân tích đang hoãn), chia theo số lượng (vd diện tích, số người) thay vì tỷ lệ cố định, nhập tỷ lệ theo bộ phận ở danh mục để dùng chung nhiều thẻ.

## 9r. Đợt 17 (20/10/2026): phương pháp khấu hao, phân bổ tùy chọn theo từng thẻ + kỳ ghi sổ tháng, quý, năm

Mỗi thẻ chọn **phương pháp** (căn cứ pháp lý từng phương pháp vẫn [CẦN XÁC MINH]) và **kỳ ghi sổ**. Mọi phương pháp dùng chung cơ chế "số lũy kế theo kế hoạch trừ số đã ghi" nên làm tròn không dồn lại, kỳ trễ tự bù, hủy kỳ và ghi giảm chạy như nhau.
- **Đường thẳng theo tháng** (như trước; tháng đầu / cuối tròn tháng hoặc theo ngày).
- **Theo thời gian (số ngày thực tế)**: nhập ngày bắt đầu và "phân bổ đến ngày"; mỗi kỳ = số ngày của kỳ ÷ tổng số ngày (ví dụ bảo hiểm 15/01/2026–14/01/2027: kỳ 1 = 17 ngày). Ghi giảm: phân bổ đến hết ngày trước ngày ghi giảm.
- **Theo sản lượng**: nhập tổng sản lượng dự kiến và đơn vị; mỗi kỳ **nhập sản lượng thực tế** (bảng "Sản lượng thực tế"; nhập 0 nếu không phát sinh); số lũy kế = nguyên giá × sản lượng lũy kế ÷ tổng. Chưa nhập sản lượng kỳ thì không chạy được; kỳ đã chạy khóa (hủy lần chạy mới sửa lại); lũy kế không vượt tổng dự kiến. Ghi sổ hàng tháng.
- **Số dư giảm dần có điều chỉnh** (chỉ TSCĐ): mỗi năm = giá trị còn lại đầu năm × (hệ số ÷ số năm); khi mức này nhỏ hơn mức đường thẳng của phần còn lại thì các năm còn lại chia đều. Hệ số để trống = tự chọn theo thời gian sử dụng (≤ 4 năm 1,5; trên 4 đến 6 năm 2,0; trên 6 năm 2,5), sửa được từng thẻ. Ví dụ 100 triệu / 5 năm / hệ số 2: 40; 24; 14,4; 10,8; 10,8 triệu. Chưa hỗ trợ tài sản chuyển từ hệ thống cũ.
- **Kỳ ghi sổ hàng tháng / hàng quý / hàng năm** (theo năm tài chính): thẻ ghi theo quý chỉ phát sinh bút toán ở tháng cuối quý, theo năm ở tháng cuối năm, dồn đủ các tháng trước; kế hoạch hoàn tất giữa kỳ thì ghi nốt. **Đổi được sau khi ghi tăng** (nút chọn trong chi tiết thẻ): áp dụng từ lần chạy sau, không mất không trùng.
- Thông số phương pháp (phương pháp, ngày kết thúc, hệ số, tổng sản lượng) đóng băng khi ghi tăng (interceptor SQLite; trigger PostgreSQL cập nhật trong migration Phase9DepreciationMethods; kỳ ghi sổ không còn bị đóng băng). Chia chi phí theo bộ phận (9q) chạy như cũ trên số ghi mỗi kỳ.
- Chưa làm: số dư giảm dần cho tài sản chuyển từ hệ thống cũ; sản lượng nhập hàng loạt từ file / từ màn sản xuất; đổi phương pháp sau khi ghi tăng; hệ số số dư giảm dần cần đối chiếu văn bản hiện hành.

## 9s. Đợt 18 (20/10/2026): đối chiếu quy định khấu hao, độ khô theo ngày (đơn vị phụ BDMT như tỷ giá), Excel ngày dd/MM/yyyy
- Quy định khấu hao: căn cứ TT45/2013 (Điều 9, 13), TT147/2016, TT30/2025 ghi ở `legal/KHAU-HAO/CAN-CU-KHAU-HAO.md`; thẻ TSCĐ mới mặc định tính theo số ngày; cảnh báo điều kiện phương pháp số dư giảm dần.
- **Độ khô theo ngày** (`DrynessDailyRate`, migration Phase9DrynessDailyRate): mỗi vật tư theo dõi độ khô một số/ngày, dùng như tỷ giá — ngày không có số thì lấy ngày gần nhất trước đó. Thứ tự ưu tiên khi tính BDMT một dòng: **kết quả đo/chứng thư > độ khô theo ngày > bình quân thời điểm** (chỉ cho xuất/chuyển kho chưa đo). Dòng nhập chưa đo có độ khô ngày thì được tạm tính (cột "tạm tính"), không ghi sổ cái. Đơn vị quy đổi cố định (`Item.AltUnit`) vẫn dùng cho vật tư thường; hai cách không dùng chung một vật tư.
- Bộ lọc cũ đã chuyển sang SmartFilter: Chứng từ (lọc phía máy chủ, tìm có debounce 350 ms), Danh mục đối tượng, cây Hệ thống tài khoản, Nhật ký kiểm toán. Điện thoại: rà 45 màn/tab bằng Playwright 390×844 (không tràn trang; vùng chạm ≥ 36–40px, chữ ô nhập 16px — khối CSS cuối index.css).
- Xuất Excel: ngày dạng yyyy-MM-dd đổi thành dd/MM/yyyy (`exportText` trong web/src/xlsx.ts).

## 10. Sổ việc chưa làm (lưu 19/10/2026 để tiếp tục ở các phiên sau)
**Chu trình doanh thu** (mục 2): biên bản và phê duyệt hàng trả lại/giảm giá; xóa nợ có phê duyệt (thông báo nợ quá hạn đã có, chưa có gửi email); đơn ngoại tệ lập hóa đơn từ phiếu giao hàng (cần tỷ giá); liên kết số phiếu giao hàng hiện trên phiếu xuất kho ở màn Kho; đơn giao trễ (backorder) thành lệnh bán hàng tiếp theo tự động.
**Chu trình cung ứng** (mục 3): chiết khấu thanh toán theo hạn (kế hoạch tiền đã có); hóa đơn mua ngoại tệ vào hàng chờ; biên bản kiểm nghiệm cho hàng nhận ngoài phiếu nhập (dịch vụ).
**Thanh toán / tài chính**: số tài khoản và tên ngân hàng trong danh mục (tài khoản ngân hàng của công ty và STK nhận của đối tượng, nhân viên) + file UNC; vốn góp và cổ tức; khoản vay sắp giải ngân và chi lương vào kế hoạch tiền; TK công nợ riêng theo đối tác, TK giá vốn riêng theo vật tư, loại vật tư, cột phân hệ nguồn trên Sổ Nhật ký chung và loại giao dịch trên thẻ kho (mục 9j); thu tiền khách hàng (`RECEIPT_EXPECTED`), xuất file UNC theo mẫu ngân hàng, hạn mức duyệt theo vai trò.
**Chuyển đổi**: đã có định mức, tổ xưởng, lệnh sản xuất, báo công, giá thành lệnh (mục 9j theo tài liệu thiết kế chị gửi); còn kế hoạch sản xuất theo đơn bán / dự báo (MRP tự động), sản phẩm dở dang cuối kỳ theo lệnh, kiểm tra chất lượng trước khi nhập kho, phân bổ chi phí chung theo định mức, chu trình riêng của dăm gỗ (đo độ khô khi sản xuất) — chờ chị mô tả. Đường dẫn Scribd, Studocu phiên làm việc không truy cập được.
**Nền**: cột lũy kế từ đầu năm trên bảng cân đối phát sinh; chiều phân tích (hợp đồng/lô tàu, bộ phận, khoản mục phí, đối tượng tập hợp chi phí) kế thừa dọc chuỗi; quy tắc bộ mã và mã tự kiểm tra; khóa màn hình khi rời máy.
**Các phân hệ làm trước khi theo chu trình** đã xếp vào menu theo chu trình và có bảng khâu ở mục 9d; còn thiếu màn hình dòng chảy cho Chuyển đổi (Nhân sự – lương và Tài chính đã có dải khâu ở mục 9g).
**Còn dang dở từ các đợt trước:** in biên bản kiểm kê / bàn giao tài sản, đề nghị sửa chữa – nâng cấp tài sản, tạm ứng/hoàn ứng, kiểm kê quỹ, đơn vị tính quy đổi và lô, bộ đọc XML hóa đơn chưa thử trên tệp thật, 2FA, DPAPI cho khóa trao đổi, sao lưu tự động, bộ cài, đối chiếu dữ liệu thật.

