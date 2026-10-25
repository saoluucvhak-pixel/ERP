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
1. (**Cố ý chưa làm**, xem ghi chú dưới) **Ổ khóa ghi toàn hệ thống.** `WriterLockKey` là một khóa duy nhất cho mọi công ty: nhiều công ty trên cùng máy chủ LAN xếp hàng chung (~30 chứng từ/giây tổng). Có thể khóa theo công ty cho nghiệp vụ chỉ chạm dữ liệu một công ty; dữ liệu dùng chung (người dùng, vai trò, loại tiền) vẫn cần khóa toàn cục.
2. (**Đã làm 20/10**, chờ CI) **Nhập hóa đơn điện tử (VatService):** mỗi hóa đơn một truy vấn kiểm tra trùng (N+1). Nên tải trước tập khóa (hướng, MST, ký hiệu, số) của lô.
3. (**Đã làm 20/10**, chờ CI; cả báo cáo đối chiếu không còn tải toàn bộ số chứng từ của công ty) **Tự khớp sao kê:** mỗi dòng sao kê một truy vấn ứng viên. Có chỉ mục (công ty, TK, ngày) nên chấp nhận được tới vài nghìn dòng; nên tải trước theo khoảng ngày nếu sao kê lớn.
4. (**Đã làm 20/10**, chờ CI) **Chỉ mục thiếu cột đầu:** `StockMeasurement` (CompanyId, DocumentId, LineNo) và `CommercialInvoiceLine` (CompanyId, StockDocumentId, StockLineNo) bị truy vấn không kèm CompanyId ở vài chỗ (OrderService, InvoiceService) nên không dùng được chỉ mục trên SQLite. Thêm điều kiện CompanyId vào truy vấn (không cần migration).
5. (**Đã làm 20/10**, chờ CI: `GET /reports/aging-all`) **Giao diện Tổng quan:** gọi báo cáo tuổi nợ riêng cho từng tài khoản công nợ đang mở; nên có một API trả gộp.

Ghi chú về khóa ghi theo công ty: khóa hiện tại bảo vệ đồng thời số chứng từ, chuỗi băm nhật ký, số thứ tự sổ kho (đều theo công ty) **và** các bảng dùng chung (người dùng, vai trò, loại tiền, công ty). Chia khóa theo công ty chỉ an toàn khi mọi thao tác ghi biết chắc nó chỉ chạm dữ liệu của một công ty; nếu không, hai công ty ghi cùng lúc vào bảng dùng chung có thể ghi trùng/lệch. Việc này cần khảo sát từng điểm gọi `InTransactionAsync` (hàng trăm chỗ) và đo trên nhiều công ty thật trước khi làm; chưa có dữ liệu cho thấy nghẽn.

## Đo bổ sung phía kho và công nợ — 21/10/2026 (đợt kiểm toán)

**Môi trường:** runner GitHub Actions (ubuntu, 2 CPU) — số tham khảo, KHÔNG phải máy khách hàng. Lệnh: `dotnet run -c Release --project tools/Hak.Bench -- --vouchers 6000 --partners 2000 --stock 1500 [--postgres]` (chạy bằng workflow `bench.yml` khi thông điệp commit có `[bench]`). Khối lượng: 12.001 chứng từ, 2.000 đối tượng, mỗi kho đo 1.500 nhập + 1.500 xuất (3.000 biến động / vật tư).

**Phát hiện:** thời gian ghi sổ một phiếu xuất tăng tuyến tính theo độ dài lịch sử của (kho, vật tư) vì mỗi dòng xuất nạp lại toàn bộ lịch sử và tra giá trị bằng một truy vấn cho mỗi 500 biến động. Sổ cái, báo cáo kế toán, tuổi nợ và đối chiếu công nợ đều dưới 0,3 giây ở khối lượng này.

**Sửa:** tra giá trị lịch sử bằng MỘT truy vấn nối (ghi sổ phiếu xuất, nhập–xuất–tồn, thẻ kho, đối chiếu sổ kho). Không đổi cách tính, không cần di chuyển dữ liệu.

| Phép đo | Trước sửa | Sau sửa |
|---|---|---|
| SQLite — ghi sổ 1 phiếu xuất, kho bình quân (sau 750 / 1.500 / 2.250 / 3.000 biến động) | 15 / 23 / 31 / 39 ms | **8 / 12 / 13 / 16 ms** |
| SQLite — kho FIFO | 16 / 24 / 31 / 39 ms | **8 / 10 / 12 / 15 ms** |
| SQLite — 1.500 nhập + 1.500 xuất (kho bình quân) | 49,6 s | **25,8 s** |
| SQLite — nhập–xuất–tồn cả năm / đối chiếu sổ kho ↔ sổ cái | 64 / 75 ms | **23 / 24 ms** |
| PostgreSQL — ghi sổ 1 phiếu xuất, kho bình quân | 30 / 35 / 40 / 43 ms | 35 / 35 / 38 / 40 ms |
| PostgreSQL — kho FIFO | 32 / 34 / 40 / 43 ms | 32 / 36 / 38 / 41 ms |
| PostgreSQL — nhập–xuất–tồn / đối chiếu sổ kho | 39 / 41 ms | 28 / 29 ms |

Độ dốc (thời gian tăng thêm cho mỗi 1.000 biến động lịch sử): SQLite ~10 → ~3,5 ms; PostgreSQL ~6 → ~2 ms. Ngoại suy tuyến tính: ở 100.000 biến động trên một vật tư–kho, mỗi dòng xuất khoảng 0,4 s (SQLite) / 0,2 s (PostgreSQL) thay vì ~1 s / ~0,6 s. Trên PostgreSQL cùng máy, phần lớn thời gian là chi phí cố định mỗi phiếu nên cải thiện tổng thể không rõ.

**Còn lại (chưa làm):** vẫn nạp toàn bộ lịch sử (không phải chỉ tổng) của (kho, vật tư) cho mỗi dòng xuất; muốn phẳng hẳn cần bảng số dư lũy kế (cần migration). Chưa đo: BCĐPS với hàng chục nghìn nhóm (đối tượng × tài khoản), Tổng quan, nhập danh mục 20.000 dòng, mở rộng lên 100.000 dòng sổ cái / 100.000 dòng kho.

## Đo bổ sung 25/10/2026 (SQLite, Release, máy 4 CPU của phiên làm việc; số tham khảo, không so với máy Windows)

Chạy riêng một mình (không cạnh tranh với bộ test): `--vouchers 50000 --partners 1000 --stock 3000 --profile`, cuối đo có 50.000 chứng từ, 100.000 dòng sổ cái, CSDL 440 MB.

| Phép đo | 10.000 chứng từ | 50.000 chứng từ |
|---|---:|---:|
| Ghi sổ đủ 4 bước | 75/giây, p95 18,7 ms | 73/giây, p95 20,0 ms |
| BCĐPS cả năm | 138 ms | 683 ms |
| Sổ chi tiết TK phải thu cả năm | 98 ms (6.667 dòng) | 642 ms (33.334 dòng) |
| Sổ chi tiết 1 đối tượng | 9 ms | 35 ms |
| Sổ Nhật ký chung 1 tháng | 23 ms | 156 ms |
| Tuổi nợ mọi TK công nợ | 49 ms | 224 ms |
| Đối chiếu sổ chi tiết công nợ ↔ sổ cái | 180 ms | 787 ms |
| Kiểm tra chuỗi nhật ký kiểm toán | 0,56 s | 2,8 s |

Kho (phiếu xuất sau N biến động, trung vị 20 phiếu gần nhất): bình quân tức thời 21 ms @1.500, 31 ms @3.000, 42 ms @4.500, 51 ms @6.000; FIFO 21 / 33 / 42 / 53 ms. **Tăng tuyến tính khoảng 7 ms mỗi 1.000 biến động** của cùng kho–vật tư vì `IssueValueAsync` đọc lại toàn bộ lịch sử.

Nhận định: ghi sổ không chậm đi theo khối lượng; báo cáo tăng tuyến tính theo số dòng và vẫn dưới 1 giây ở 50.000 chứng từ. Điểm cần canh là kho: nếu một cặp kho–vật tư tích lũy trên khoảng 20.000 biến động trong cùng kỳ tính giá thì phiếu xuất vượt 0,15 giây và tới 100.000 biến động ước tính ~0,7 giây — lúc đó mới đáng làm checkpoint giá bình quân / lớp FIFO (mốc sequence, replay phần sau, vô hiệu khi backdate / đảo / điều chỉnh giá). Chưa làm vì chưa nghẽn ở quy mô doanh nghiệp vừa và nhỏ; chưa đo nhiều người dùng đồng thời và PostgreSQL ở 50.000.

## Đo nhiều người dùng đồng thời — 25/10/2026 (đợt "làm tiếp")
Công cụ: `dotnet run -c Release --project tools/Hak.Bench -- --postgres --vouchers 10000 --partners 300 --users 16 --user-vouchers 100` (tùy chọn mới `--users`, `--user-vouchers`; bộ đo SQL tắt trong pha đồng thời vì không an toàn đa luồng). PostgreSQL 16 cục bộ, máy 4 CPU của phiên làm việc, số tham khảo.

| Phép đo | Kết quả |
|---|---|
| Ghi sổ 1 người (lập→gửi→duyệt→ghi sổ), nền 10.000 chứng từ | 24 chứng từ/giây; trung vị 40,5 ms, p95 54 ms |
| 16 người ghi sổ đồng thời × 100 chứng từ | 1600/1600 thành công, không lỗi; 26 chứng từ/giây; trung vị 608 ms, p95 742 ms |
| SQLite, 8 người × 50 chứng từ | 400/400 thành công, không lỗi; 79 chứng từ/giây |

Nhận định: không mất chứng từ, không lỗi xung đột khi nhiều người ghi cùng lúc; tổng thông lượng PostgreSQL không tăng theo số người (ghi sổ tuần tự theo công ty bằng khóa tư vấn — đúng thiết kế để sổ cái luôn nhất quán), nên độ trễ mỗi chứng từ tăng theo số người đang chờ (16 người ≈ 0,6 giây). Với 5–10 kế toán ghi sổ cùng lúc thì độ trễ dưới ~0,4 giây. Chưa đo 50k–100k chứng từ trên PostgreSQL.

