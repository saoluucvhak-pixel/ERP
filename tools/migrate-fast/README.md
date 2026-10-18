# Chuyển dữ liệu từ FAST Accounting (Visual FoxPro) sang HAK

Đọc **chỉ đọc** thư mục dữ liệu FAST (`COD\DMTK`, `COD\DMKH`, `DBF\CT00`, `DBF\cdtk`, `DBF\cdkh`); văn bản TCVN3 (ABC/.VnTime) được đổi sang Unicode; dòng đã xóa của FoxPro (`*`) bị bỏ qua. Không đọc bảng người dùng / mật khẩu của FAST (`SYS\dmuser`).

## 1. Xuất (ra ngoài thư mục mã nguồn — dữ liệu thật không đưa vào git)

```bash
python -X utf8 tools/migrate-fast/fast_export.py <thư mục dữ liệu FAST> %LOCALAPPDATA%\HAK\chuyen-du-lieu\fast-2026 2026 VP
```

| Tệp | Nội dung | Nhập ở |
|---|---|---|
| `he-thong-tk.csv` | Hệ thống TK theo mã FAST (TK có TK con → TK tổng hợp; số liệu ghi thẳng trên TK mẹ → TK `<mã>.00`) | Thiết lập → Nhập → Hệ thống tài khoản |
| `doi-tuong.csv` | Danh mục khách hàng / NCC / nhân viên | … → Đối tượng |
| `so-du-dau-ky-<năm>.csv` | Số dư 01/01 (chi tiết theo khách khi FAST có `cdkh` và cộng khớp) | … → Số dư đầu kỳ (ngày 01/01) |
| `chung-tu-<năm>.csv` | Mọi dòng sổ cái của năm, mỗi `STT_REC` một chứng từ | … → Chứng từ (chuyển dữ liệu) |
| `can-doi-<năm>.csv` | BCĐPS từng tháng tính từ dữ liệu FAST — để đối chiếu | (so sánh) |
| `bao-cao.txt` | Kiểm tra: chứng từ không cân, mã khách thiếu, TK mẹ có số liệu… | |

## 2. Nhập và đối chiếu tự động (qua API)

```bash
node tools/migrate-fast/hak_import.mjs %LOCALAPPDATA%\HAK\chuyen-du-lieu\fast-2026 2026 <tệp tài khoản.json>
```

Tệp tài khoản: `{ "api": ".../api", "company": "<mã hoặc id công ty>", "preparer": {userName, password}, "approver": {userName, password} }` — người lập và người duyệt **phải khác nhau**. Script nhập 4 tệp, ghi sổ số dư đầu kỳ, gửi duyệt + duyệt/ghi sổ toàn bộ chứng từ, rồi so BCĐPS của HAK với `can-doi-<năm>.csv` từng tháng, từng TK (dư đầu, PS Nợ, PS Có, dư cuối).

## Kết quả đã chạy (16/10/2026, CSDL thử)

Dữ liệu FAST thật của doanh nghiệp (đơn vị VP): sổ cái 2020 → 31/07/2026, số dư chuyển năm khớp từng TK mọi năm. Năm 2026: 356 TK, 133 đối tượng, 16 dòng số dư đầu năm, 126 chứng từ / 404 dòng → **BCĐPS HAK khớp FAST cả 7 tháng, từng TK**.

**So với báo cáo in từ FAST** (bảng cân đối phát sinh S04-DNN một tháng, xuất Excel từ FAST): mọi dòng TK (TK chi tiết, TK cấp trên cộng dồn, TK lưỡng tính 141 / 331 / 333) **khớp cả 6 cột**; dòng Tổng cộng khớp.

```bash
powershell -File tools/migrate-fast/xls-to-tsv.ps1 bcdps-fast.xls fast-bcdps.tsv
node tools/migrate-fast/hak_import.mjs <thư mục xuất> 2026 <tài khoản.json> --fast-report fast-bcdps.tsv 2026-06-01 2026-06-30
```

TK FAST theo dõi công nợ (TK_CN) được trình bày **lưỡng tính** theo đối tượng trên HAK, giống FAST.

Chưa đối chiếu: các năm trước 2026, công nợ theo hóa đơn, kho, TSCĐ (dữ liệu FAST 2026 không có phát sinh kho/công nợ).

## 3. Chuyển sang hệ thống tài khoản TT 99/2025 (chị quyết định 16/10/2026)

`map-tt200-tt99.csv` — bảng ánh xạ TK cũ → TK TT99 (+ TK chi tiết doanh nghiệp mở theo Điều 11 k2, nhãn BC, căn cứ Điều 29). Chờ KTT duyệt.

```bash
python -X utf8 tools/migrate-fast/tt99_convert.py <thư mục xuất FAST> 2026 tools/migrate-fast/map-tt200-tt99.csv legal/TT99-2025/nhap-he-thong-tk-de-xuat.csv <thư mục ra>
node tools/migrate-fast/hak_import.mjs <thư mục ra> 2026 <tài khoản.json>
```

Ra: `bang-doi-chieu-tk-tt99.csv` (bảng duyệt) và bộ tệp nhập cùng tên chuẩn trên hệ thống TK TT99 (TK cha có chi tiết → tổng hợp; 41111/41112 bỏ cho công ty TNHH, 4111 hạch toán trực tiếp; TK FAST không theo dõi theo đối tượng thì không bắt buộc đối tượng). Chạy thử 16/10/2026: 31 TK cũ → 31 TK, số dư cân, **BCĐPS khớp 7/7 tháng**.

### Nhiều năm + BCTC trên số thật (16/10/2026)

```bash
python -X utf8 tools/migrate-fast/tt99_convert.py <xuất 2025>,<xuất 2026> 2025,2026 tools/migrate-fast/map-tt200-tt99.csv legal/TT99-2025/nhap-he-thong-tk-de-xuat.csv <ra>
node tools/migrate-fast/hak_import.mjs <ra> 2025,2026 <tài khoản.json>
node tools/migrate-fast/report_check.mjs <tài khoản.json> 2025-01-01 2025-12-31
```

- Bút toán có TK 911 (FAST kết chuyển hằng tháng, phiếu PK3) nhập là chứng từ kết chuyển **CLS** — nếu không B02 bị bù trừ về 0.
- Trong một ngày, chứng từ làm tăng tiền xếp trước; ghi sổ nhiều vòng (phiếu chi chờ phiếu thu cùng ngày) — giữ chính sách quỹ không âm.
- Kết quả: 2025 + 2026 trên TK TT99 khớp 19/19 tháng; B01 31/12/2025 cân (Mã 280 = Mã 440); B02 2025 lợi nhuận sau thuế = số dư TK 4212 đầu năm 2026 trên FAST; B03 trực tiếp = gián tiếp.
