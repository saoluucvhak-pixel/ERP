# 10 — Thiết kế theo chu trình (bám tài liệu "Phân tích thiết kế hệ thống ERP", 20/12/2016)

Trạng thái: **ĐỀ XUẤT, chờ chị xác nhận** (19/10/2026). Bản này bám tệp chị gửi; số mục (I–V) và tên tập tin lấy từ tệp. Tệp là mô hình quy trình và kiểm soát, **không phải căn cứ pháp lý**: pháp lý vẫn theo `legal/` (TT99). Tệp chỉ mô tả chi tiết 3 chu trình (doanh thu, cung ứng, chuyển đổi — chuyển đổi mới đến phần giới thiệu và danh sách chức năng); chu trình nhân sự – tiền lương và tài chính chỉ được giới thiệu ở mục IV.2.

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

## 8. Chờ chị quyết định
1. "Tự động" của bước thanh toán: tự tạo lệnh **Nháp** rồi chờ duyệt (đề xuất), hay tự duyệt dưới một hạn mức?
2. Ai được bấm "Chấp nhận thanh toán", có hạn mức theo vai trò?
3. Vượt hạn mức tín dụng: **chặn** hay **trình người có thẩm quyền**? Hay HAK không dùng hạn mức?
4. Chiều phân tích của HAK.
5. Quy trình sản xuất dăm (cho chu trình chuyển đổi).
