# 10 — Thiết kế theo chu trình, nhật ký chu trình và bàn giao giữa các chu trình

Trạng thái: **ĐỀ XUẤT, chờ chị xác nhận** (19/10/2026). Nguồn định hướng: chị (kế toán trưởng) và giáo trình "Phân tích thiết kế hệ thống ERP" (mô hình 5 chu trình). Giáo trình chỉ là mô hình quy trình và kiểm soát, không phải căn cứ pháp lý.

## 1. Nguyên tắc chị đã chốt
1. Xây dựng **theo chu trình**, đi từ chi tiết đến tổng hợp. Mỗi chu trình có **nhật ký riêng** ghi từng sự kiện nghiệp vụ.
2. Mỗi sự kiện **phát sinh hoặc không phát sinh** nghiệp vụ kế toán. Ví dụ: thêm hợp đồng, đặt hàng — không; nhập hàng — có.
3. Đi theo **chuỗi và luồng**; viết riêng từng chu trình, quản lý dòng thông tin chi tiết.
4. Phân biệt rõ **dữ liệu phát sinh, dữ liệu chuyển đổi, dữ liệu kết quả**.
5. Cuối chu trình này thì **chu trình sau tự nhận thông tin** và tiến hành. Ví dụ: chu trình mua hàng bấm "chấp nhận thanh toán" thì chu trình thanh toán tự nhận và xử lý.

## 2. Ba lớp dữ liệu
| Lớp | Là gì | Trong hệ thống hiện có | Quy tắc |
|---|---|---|---|
| **Phát sinh** (nguồn) | Chứng từ, hồ sơ do người dùng hoặc hệ thống ngoài tạo: hợp đồng, đơn hàng, phiếu kho, hóa đơn, lệnh thanh toán, sao kê, bảng lương nhập | `orders`, `stock_documents`, `invoices`, `payment_orders`, `bank_statements`, `payroll_runs`, `vouchers` ở trạng thái Nháp | Sửa được khi còn Nháp; sau khi xác nhận thì bất biến, thay đổi bằng chứng từ điều chỉnh/đảo |
| **Chuyển đổi** | Xử lý biến dữ liệu nguồn thành kết quả, có phiên bản và truy vết | Posting Rule Engine, tính giá xuất kho, khấu hao, giá thành, đánh giá lại ngoại tệ, kết chuyển, phân bổ công nợ | Ghi lại: đầu vào nào, quy tắc/phiên bản nào, ai chạy, lúc nào, kết quả nào. Chạy lại cho cùng đầu vào phải cho cùng kết quả |
| **Kết quả** | Sổ và báo cáo dẫn xuất | Sổ cái, sổ kho, phân bổ công nợ, BCĐPS, BCTC, ảnh chụp báo cáo | Bất biến hoặc dựng lại được từ nguồn + chuyển đổi; không nhập tay |

## 3. Nhật ký chu trình (cycle journal)
Bảng chỉ thêm (append-only, trigger chặn sửa/xóa như sổ cái), mỗi dòng là một sự kiện:
`cycle`, `event_type`, `occurred_at`, `actor`, `document_type/id` (chứng từ nguồn), `generates_accounting` (có/không), `voucher_id` (nếu có), `data_layer` (phát sinh / chuyển đổi / kết quả), `payload_hash`, `causation_id` (sự kiện trước dẫn tới nó).

Mỗi loại sự kiện khai báo trong **danh mục sự kiện** (dữ liệu, có phiên bản) với cờ `generates_accounting`. Không cho ghi sự kiện chưa khai báo.

Ví dụ chu trình Cung ứng:
| Sự kiện | Phát sinh kế toán? |
|---|---|
| HỢP ĐỒNG_TẠO / HỢP ĐỒNG_KÝ | Không |
| ĐƠN MUA_TẠO / ĐƠN MUA_XÁC NHẬN | Không |
| PHIẾU NHẬP_GHI SỔ | **Có** (Nợ hàng tồn / Có GRNI hoặc phải trả) |
| HÓA ĐƠN MUA_GHI SỔ | **Có** (Nợ GRNI + thuế / Có phải trả) |
| ĐỐI CHIẾU 3 BÊN_ĐẠT | Không |
| **CHẤP NHẬN THANH TOÁN** | Không — **bàn giao sang chu trình Thanh toán** |
| TRẢ HÀNG / GIẢM GIÁ | **Có** |

## 4. Bàn giao giữa các chu trình (handoff)
- Bảng `cycle_handoffs`: `from_cycle`, `to_cycle`, `handoff_type`, `source_ref`, `payload` (JSON, số tiền là chuỗi), `status` (Chờ nhận → Đã nhận → Hoàn tất / Từ chối), `created_event_id`, `accepted_by_event_id`.
- Ghi bàn giao **trong cùng giao dịch cơ sở dữ liệu** với sự kiện nguồn (ACID): hoặc cả hai cùng có, hoặc cả hai cùng không (kiểu outbox).
- Chu trình nhận có **hộp thư đến**: tự động tạo chứng từ **Nháp** từ bàn giao, báo người có quyền. Mỗi bàn giao chỉ xử lý **một lần** (khóa idempotent theo `source_ref`).
- Bàn giao có thể bị từ chối kèm lý do, trả ngược về chu trình gửi.

### Ví dụ: Mua hàng → Thanh toán
1. Cung ứng: người có quyền bấm **Chấp nhận thanh toán** trên hóa đơn mua đã ghi sổ và đã đối chiếu 3 bên. Sự kiện `CHẤP NHẬN THANH TOÁN` (không phát sinh KT) + bàn giao `PAYMENT_REQUEST` (nhà cung cấp, số còn phải trả, hạn thanh toán, dòng công nợ cần tất toán).
2. Thanh toán: hộp thư đến nhận bàn giao, **tự tạo Lệnh thanh toán (UNC) trạng thái Tạo mới**, đã gắn `SettleJournalLineId`.
3. Người khác (tách nhiệm vụ) duyệt → gửi ngân hàng → ngân hàng thực hiện → hệ thống sinh bút toán chi và tất toán công nợ (đã có sẵn trong `PaymentOrderService`).
4. Kết quả quay ngược: sự kiện `ĐÃ THANH TOÁN` trong nhật ký Cung ứng, hóa đơn chuyển trạng thái đã trả.

**Điểm cần chị quyết định:** "tự động" dừng ở bước tạo lệnh Nháp và chờ duyệt (đề xuất), hay tự duyệt luôn khi số tiền dưới một hạn mức? Chuyển tiền thật không nên chạy hoàn toàn tự động nếu chưa có người thứ hai duyệt.

## 5. Các chu trình và bàn giao dự kiến
| Chu trình | Phần có sẵn | Bàn giao đi | Bàn giao đến |
|---|---|---|---|
| **Doanh thu** | đơn bán, phiếu xuất, hóa đơn bán, công nợ phải thu, thu tiền | `RECEIPT_EXPECTED` → Thanh toán/Thu; `SHIPMENT_REQUEST` → Kho | `PAYMENT_RECEIVED` |
| **Cung ứng** | đơn mua, phiếu nhập, hóa đơn mua, GRNI, trả hàng | `PAYMENT_REQUEST` → Thanh toán | `PAYMENT_DONE` |
| **Chuyển đổi (sản xuất)** | đối tượng giá thành, giá thành | nhập thành phẩm → Kho | xuất nguyên liệu từ Kho |
| **Nhân sự – lương** | nhận bảng lương JSON | `PAYROLL_PAYMENT` → Thanh toán | — |
| **Tài chính / Thanh toán** | lệnh thanh toán, sao kê, vay, ngoại tệ | `PAYMENT_DONE` → chu trình nguồn | `PAYMENT_REQUEST`, `PAYROLL_PAYMENT`, `RECEIPT_EXPECTED` |
| **Kế toán tổng hợp** | chứng từ, sổ, kỳ, BCTC | — | mọi sự kiện `generates_accounting` |

## 6. Cách thực hiện (tăng dần, không viết lại)
- Giữ nguyên các dịch vụ và 264 test hiện có. Thêm lớp **Cycles** (`src/Hak.Application/Cycles/…`) điều phối: mỗi chu trình có thư mục riêng, gọi dịch vụ chức năng sẵn có và ghi nhật ký + bàn giao.
- Thêm bảng `cycle_event_types`, `cycle_events`, `cycle_handoffs` (migration cho cả SQLite và PostgreSQL; trigger bất biến cho `cycle_events`).
- Thứ tự: (1) nền nhật ký + bàn giao + test; (2) Cung ứng → Thanh toán (mẫu đầu tiên, vì lệnh thanh toán đã có); (3) Doanh thu → Thu tiền; (4) Hợp đồng làm chứng từ nguồn không phát sinh KT; (5) Chuyển đổi; (6) Nhân sự – lương.
- Giao diện: mỗi chu trình có màn hình **dòng chảy** (các khâu, số chứng từ đang ở từng khâu), **nhật ký** (lọc theo "phát sinh kế toán / không"), **hộp thư đến**.

## 7. Liên hệ với chiều phân tích
Hợp đồng là chứng từ nguồn không phát sinh kế toán. Các chiều (hợp đồng/lô tàu, bộ phận…) **kế thừa dọc chuỗi** (hợp đồng → đơn → phiếu → hóa đơn → thanh toán) nên người nhập không phải gõ lại; xem câu hỏi về chiều ở cuộc trao đổi ngày 19/10/2026.

## 8. Việc chưa quyết định
1. Mức "tự động" của bàn giao thanh toán (mục 4).
2. Quyền và hạn mức của "Chấp nhận thanh toán".
3. Danh sách chiều phân tích của HAK.
4. Phạm vi đợt đầu: nền + Cung ứng → Thanh toán.
