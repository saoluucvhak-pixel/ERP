# 09 — Đo hiệu năng — v0.1 (2026-10-15)

Công cụ: `tools/Hak.Bench`. Công cụ tạo một công ty giả lập trong **CSDL tạm** rồi đo:
- SQLite: tệp mới trong `%TEMP%\hak-bench`, tự xóa sau khi đo.
- PostgreSQL: CSDL `hak_bench_<guid>` trên máy chủ `HAK_TEST_PG`, tự xóa sau khi đo.

Công cụ không bao giờ trỏ vào CSDL thật. Mã tài khoản dùng mã thử, không phải hệ thống TK pháp lý.

```
dotnet run -c Release --project tools/Hak.Bench -- --vouchers 20000 --partners 500 [--postgres] [--profile] [--keep]
```

Mỗi chứng từ đi đủ 4 bước như người dùng thật: lập → gửi duyệt → duyệt → ghi sổ. Đó là 4 giao dịch, mỗi bước một DbContext mới, giống mỗi request HTTP. Dữ liệu gồm 1/3 bán chịu (theo đối tượng), 1/3 thu tiền, 1/3 chi phí, rải đều cả năm. Mỗi báo cáo đo 3 lần trên DbContext mới, lấy trung vị.

**Máy đo:** máy phát triển, 4 CPU, Windows 10, ổ đĩa cục bộ. **Số liệu:** 20.000 chứng từ, 500 đối tượng; ~40.000 dòng sổ cái, ~80.000 dòng nhật ký kiểm toán.

| Phép đo | SQLite 3.x (WAL, synchronous=FULL) | PostgreSQL 18.6 (cùng máy) |
|---|---|---|
| Ghi sổ đủ 4 bước | **36 chứng từ/giây**; trung vị 25 ms, p95 50 ms, p99 57 ms | **30 chứng từ/giây**; trung vị 32 ms, p95 43 ms, p99 54 ms |
| Tốc độ theo khối lượng | ổn định 35–38/giây từ 0 đến 20.000 | ổn định 29–34/giây |
| Bảng cân đối số phát sinh cả năm | 446 ms | 53 ms |
| BCĐPS cộng dồn TK cha | 349 ms | 63 ms |
| Sổ chi tiết TK phải thu cả năm (13.334 dòng) | 456 ms | 185 ms * |
| Sổ chi tiết 1 đối tượng | 41 ms | 4 ms |
| Sổ Nhật ký chung 1 tháng (3.332 dòng) | 195 ms | 20 ms |
| Khoản mở phải thu 1 đối tượng | 29 ms | 2 ms |
| Số dư 1 TK cuối năm | 38 ms | 10 ms |
| Kiểm tra chuỗi nhật ký kiểm toán (~80.000 bản ghi) | 1,9 s | 1,3 s |
| Dung lượng CSDL | 136,5 MB (~6,8 KB/chứng từ) | — |
| Sao lưu mã hóa / kiểm tra bản sao lưu | 3,1 s / 5,0 s | dùng `pg_dump` |

\* Đo trước bản sửa truy vấn TK đối ứng (mục 2); trên SQLite bản sửa giảm thời gian từ 2,4 s xuống 0,46 s.

## Cải thiện đã làm từ kết quả đo

1. **Bật connection pooling cho SQLite.** Trước đó đặt `Pooling=false`, nên mỗi lần EF đóng kết nối, SQLite phải checkpoint WAL.
   - Ghi sổ: 212 ms → 37 ms/chứng từ.
   - Bộ test tích hợp: 2 phút 19 giây → 1 phút 21 giây.
   - Đo riêng: commit có fsync chỉ tốn 1,7–3,4 ms. Độ bền dữ liệu giữ nguyên `synchronous=FULL`.
2. **Sổ chi tiết tài khoản** (13.334 dòng: 4,2 s → 0,46 s). Có hai chỗ sửa:
   - Tìm TK đối ứng: trước đây với mỗi dòng sổ lại quét toàn bộ danh sách dòng cùng bút toán (O(n²)); nay tra theo khóa (bút toán, bên Nợ/Có).
   - Truy vấn dòng cùng bút toán: thay danh sách `IN` hàng chục nghìn mã bằng phép join theo cùng điều kiện lọc, vì SQLite lập kế hoạch kém cho `IN` lớn.

## Nhận định

- Với doanh nghiệp vừa và nhỏ (vài chục nghìn chứng từ/năm), thời gian phản hồi đều dưới 0,5 giây, trừ kiểm tra chuỗi kiểm toán (~2 giây, chạy theo yêu cầu). Tốc độ ghi sổ không giảm khi dữ liệu tăng.
- Khi nhiều người dùng, nên dùng PostgreSQL: báo cáo nhanh hơn 3–10 lần. Các lệnh ghi được xếp lượt (khóa advisory), nên tổng tốc độ ghi của cả mạng vào khoảng 30 chứng từ/giây, vẫn dư cho nhập liệu thủ công.
- **Dung lượng.** Khóa GUID lưu dạng chuỗi 36 ký tự trên nhiều bảng và chỉ mục. Mỗi chứng từ có 4 dòng lịch sử trạng thái và 4 bản ghi kiểm toán. Kết quả là 100.000 chứng từ/năm tốn khoảng 0,7 GB SQLite.
  - Có thể giảm bằng cách lưu GUID dạng BLOB 16 byte, nhưng cần migration chuyển đổi dữ liệu. Chưa làm; xem lại khi có dữ liệu thực (GĐ7).
- **Chưa đo:**
  - nhiều máy trạm ghi đồng thời qua LAN thật (độ trễ mạng);
  - số liệu 100.000+ chứng từ;
  - các phân hệ kho/giá thành, vì tính giá xuất cuối tháng phụ thuộc số dòng sổ kho.

  Các phép đo này cần làm trên máy chủ và mạng của doanh nghiệp.

## Rà soát tĩnh 20/10/2026 (đọc mã, chưa đo)

Đã làm: `AuditService.AppendAsync` không còn truy vấn bản ghi cuối khi đã có bản ghi chờ lưu cùng giao dịch (bớt 1 truy vấn cho mỗi lần ghi nhật ký thứ hai trở đi).

Chưa làm (cần đo hoặc cần migration, xếp theo lợi ích):
1. **Ổ khóa ghi toàn hệ thống.** `WriterLockKey` là một khóa duy nhất cho mọi công ty: nhiều công ty trên cùng máy chủ LAN xếp hàng chung (~30 chứng từ/giây tổng). Có thể khóa theo công ty cho nghiệp vụ chỉ chạm dữ liệu một công ty; dữ liệu dùng chung (người dùng, vai trò, loại tiền) vẫn cần khóa toàn cục.
2. **Nhập hóa đơn điện tử (VatService):** mỗi hóa đơn một truy vấn kiểm tra trùng (N+1). Nên tải trước tập khóa (hướng, MST, ký hiệu, số) của lô.
3. **Tự khớp sao kê:** mỗi dòng sao kê một truy vấn ứng viên. Có chỉ mục (công ty, TK, ngày) nên chấp nhận được tới vài nghìn dòng; nên tải trước theo khoảng ngày nếu sao kê lớn.
4. **Chỉ mục thiếu cột đầu:** `StockMeasurement` (CompanyId, DocumentId, LineNo) và `CommercialInvoiceLine` (CompanyId, StockDocumentId, StockLineNo) bị truy vấn không kèm CompanyId ở vài chỗ (OrderService, InvoiceService) nên không dùng được chỉ mục trên SQLite. Thêm điều kiện CompanyId vào truy vấn (không cần migration).
5. **Giao diện Tổng quan:** gọi báo cáo tuổi nợ riêng cho từng tài khoản công nợ đang mở; nên có một API trả gộp.
