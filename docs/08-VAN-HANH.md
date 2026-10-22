# 08 — Hướng dẫn vận hành (cài đặt, LAN, sao lưu, nâng cấp) — v0.1

> Bộ cài MSI/EXE bằng Tauri (GĐ8) chưa có — cần bộ C++ của Visual Studio Build Tools, phải cài bằng quyền quản trị. Trong lúc chờ, dùng **gói chạy độc lập** (mục 1a).
> Nội dung pháp lý (hệ thống TK, biểu mẫu TT 99/2025) **[CẦN XÁC MINH]** — xem docs/07.

## 1. Mô hình triển khai

| Mô hình | Lưu trữ | Khi nào dùng |
|---|---|---|
| Một máy | SQLite (một tệp `.db`) | 1 người dùng / 1 máy |
| Mạng nội bộ (LAN) | PostgreSQL trên máy chủ; mỗi máy trạm chạy API riêng ở `127.0.0.1` kết nối tới máy chủ CSDL | Nhiều người cùng làm |

Ở chế độ LAN, các lệnh ghi được xếp lượt bằng khóa `pg_advisory_xact_lock` (đọc vẫn song song); xung đột deadlock/serialization tự chạy lại tối đa 5 lần.

## 1a. Gói chạy độc lập (không cần cài .NET / Node)

Tạo gói (máy phát triển): `.\tools\package\package.ps1` → thư mục `artifacts\HAK` gồm:
- `HAK.exe` — API kèm .NET, một tệp, khoảng 120 MB;
- `wwwroot` — giao diện; API phục vụ cùng địa chỉ, có header bảo mật CSP, nosniff, chống nhúng khung;
- `Chay HAK.cmd` — môi trường Production, cổng 5080;
- `Chay HAK - thu nghiem.cmd` — môi trường Test, cổng 5081, CSDL riêng;
- `Khoi phuc mat khau quan tri.cmd`;
- `DOC-TRUOC.txt`;
- `SHA256SUMS.txt`.

Chép cả thư mục sang máy dùng rồi chạy tệp `.cmd`. Các tệp `.cmd` phải là ASCII (chữ không dấu) và xuống dòng CRLF — cmd.exe đọc lệch tệp UTF-8 / LF (lỗi gặp thật 17/10/2026; `.gitattributes` khóa `*.cmd eol=crlf`).

Dữ liệu nằm ở `%LOCALAPPDATA%\HAK\<môi trường>\`, không nằm trong thư mục chương trình. Vì vậy thay bản mới chỉ cần thay thư mục; nhớ sao lưu trước, xem mục 6.

Đã thử: gói chạy ở môi trường Test với CSDL tạm. Màn hình lần chạy đầu hiển thị đúng. Chuyển trang và gọi API đúng (đường `/api` không tồn tại trả 404), trình duyệt không báo lỗi CSP.

Gói **chưa ký số** — Windows SmartScreen có thể cảnh báo. Ký mã bằng chứng thư của doanh nghiệp thuộc GĐ8.

## 2. Cấu hình

Đặt trong `appsettings.json` cạnh `Hak.Api.exe`, hoặc bằng biến môi trường (dấu `:` thay bằng `__`, ví dụ `Hak__Provider`).

| Khóa | Giá trị | Mặc định |
|---|---|---|
| `Hak:Environment` | `Development` / `Test` / `Production` | `Production` (máy phát triển phải ghi rõ `--Hak:Environment=Development`) |
| `Hak:Provider` | `Sqlite` / `PostgreSQL` | `Sqlite` |
| `Hak:DatabasePath` | Đường dẫn tệp SQLite | `%LOCALAPPDATA%\HAK\<môi trường>\hak.db` |
| `ConnectionStrings:Hak` | Chuỗi kết nối PostgreSQL — **chỉ đặt bằng biến môi trường `ConnectionStrings__Hak`**, không ghi vào tệp trong kho mã | — |
| `Hak:BackupPath` | Thư mục bản sao lưu (SQLite) | `%LOCALAPPDATA%\HAK\<môi trường>\backups` |
| `AllowedHosts` | Tên máy được phép trong header Host, ngăn cách bằng `;` | `localhost;127.0.0.1;[::1]` (nếu cho máy khác truy cập API/giao diện, thêm tên máy: `AllowedHosts=localhost;may-chu`) |
| `Hak:LoginRateLimitPerMinute` | Số lần gọi đăng nhập / tạo quản trị đầu tiên tối đa mỗi phút trên một IP (quá thì trả 429); `0` = tắt | `30` |
| `Hak:SecretKey` | Khóa chủ (base64 của 32 byte) mã hóa khóa trao đổi lương/chứng từ trong CSDL; nên đặt bằng biến môi trường `Hak__SecretKey` | Không đặt → tự tạo tệp `secret.key` (`Hak:SecretKeyFile`, mặc định `%LOCALAPPDATA%\HAK\<môi trường>\secret.key`). Môi trường `Test` không mã hóa |
| `--urls` | Địa chỉ API | Nên giữ `http://127.0.0.1:<cổng>` (chỉ máy này truy cập) |

Mọi phản hồi kèm header bảo mật (CSP, X-Frame-Options, nosniff, Referrer-Policy); ở Production có thêm HSTS khi truy cập qua HTTPS.

Production + PostgreSQL: chương trình **từ chối khởi động** nếu `SslMode=Disable`.

## 3. Lần chạy đầu (trình tự bắt buộc)

1. Khởi động API. CSDL được tạo/nâng cấp tự động (migration) và nạp dữ liệu tham chiếu (vai trò, quyền, loại tiền).
2. Mở giao diện: khi CSDL chưa có người dùng, màn hình đăng nhập chuyển sang **Lần chạy đầu** → tạo quản trị hệ thống (mật khẩu ≥ 10 ký tự). Chỉ làm được một lần.
3. **Quản trị → Chế độ kế toán**: khai báo phiên bản (mã, hiệu lực, ít nhất một căn cứ pháp lý). Phiên bản mới luôn **CHƯA xác minh**; chỉ bấm *Xác minh* sau khi đã đối chiếu văn bản gốc và ghi rõ người ký.
4. **Quản trị → Công ty**: tạo công ty (đồng tiền hạch toán, chế độ kế toán).
5. **Quản trị → Người dùng**: tạo người dùng, cấp vai trò theo công ty. Nguyên tắc: người lập chứng từ khác người duyệt/ghi sổ.
6. Đăng nhập vào công ty → **Thiết lập**:
   - Năm tài chính (tạo kỳ tháng);
   - Hệ thống tài khoản — nhập hàng loạt ở tab *Nhập từ Excel/CSV* (tải tệp mẫu, *Kiểm tra*, rồi *Nhập*); sau đó **mở TK chi tiết trước khi hạch toán** ở tab *Hệ thống tài khoản* (dạng cây): nút **+ TK con** trên TK cần chi tiết (vd 331 → 3311 phải trả người bán, 3318 hàng về chưa có hóa đơn; 112 → 1121 theo từng ngân hàng). TK cha tự chuyển thành TK tổng hợp; khi TK cha đã có bút toán hoặc đã gắn vai trò thì không chuyển được — phần mềm báo lý do;
   - Vai trò tài khoản (ánh xạ mọi vai trò bắt buộc; thiếu thì phân hệ từ chối ghi sổ `RULE_ROLE_UNMAPPED`);
   - Đối tượng, kho, vật tư, phương pháp tính giá theo kho.
7. Số dư đầu kỳ: tab *Nhập từ Excel/CSV* → *Số dư đầu kỳ* → tạo chứng từ OPB (Nháp) → **Chứng từ**: gửi duyệt → duyệt → ghi sổ. Công nợ nhập chi tiết từng hóa đơn còn nợ (có hạn thanh toán) để theo dõi thanh toán sau này.
   Tồn kho đầu kỳ: nhập danh mục vật tư, rồi chọn loại *Tồn kho đầu kỳ* với một **TK trung gian**. Mỗi kho được tạo một phiếu nhập NHÁP, ghi Nợ TK kho / Có TK trung gian. Trong chứng từ OPB, ghi **Nợ TK trung gian** thay cho các TK hàng tồn kho, để TK trung gian về 0. Kho phải đã có phương pháp tính giá trước khi ghi sổ phiếu. Sau khi ghi sổ, dùng đối chiếu kho – sổ cái để kiểm tra.
   TSCĐ/CCDC đầu kỳ: làm sau khi chứng từ OPB đã ghi sổ. Chọn loại *TSCĐ / CCDC đầu kỳ* và nhập số chứng từ OPB.
   - Nguyên giá từng thẻ được lấy từ dòng Nợ TK tài sản trên OPB.
   - Hao mòn lũy kế và số tháng còn lại khai trên tệp; khấu hao tiếp từ ngày số dư, không trích bù kỳ cũ.
   - Với CCDC, nhập giá trị còn lại vào cột Nguyên giá.
   - Thẻ ở trạng thái nháp; người thứ hai ghi tăng.
8. Đối chiếu: Bảng cân đối số phát sinh tại ngày số dư phải khớp số liệu phần mềm cũ trước khi bắt đầu ghi nghiệp vụ mới.

## 4. Chế độ LAN với PostgreSQL

Máy chủ CSDL (đã thử với PostgreSQL 18.6):

```sql
CREATE ROLE hak_app LOGIN PASSWORD '<mật khẩu mạnh>';
CREATE DATABASE hak OWNER hak_app ENCODING 'UTF8';
```

- `postgresql.conf`: `listen_addresses = '<IP LAN của máy chủ>'`, `ssl = on` (chứng chỉ máy chủ).
- `pg_hba.conf`: chỉ cho dải mạng nội bộ, bắt buộc SSL và scram:
  `hostssl  hak  hak_app  192.168.1.0/24  scram-sha-256`
- Tường lửa Windows: mở cổng PostgreSQL **chỉ** cho dải LAN.

Mỗi máy trạm:

```
setx ConnectionStrings__Hak "Host=<máy chủ>;Port=5432;Database=hak;Username=hak_app;Password=<...>;SslMode=VerifyFull"
setx Hak__Provider PostgreSQL
setx Hak__Environment Production
```

API trên máy trạm chỉ nghe `127.0.0.1`. Không mở cổng API ra mạng.

## 5. Sao lưu và khôi phục

### Khóa chủ (secret.key)
Khóa trao đổi (HMAC) trong CSDL được mã hóa AES-256-GCM bằng khóa chủ nằm **ngoài** CSDL và ngoài bản sao lưu `.hakbk`. Lưu `secret.key` (hoặc giá trị `Hak__SecretKey`) ở nơi an toàn, tách khỏi bản sao lưu CSDL. Mất khóa chủ thì khôi phục CSDL xong phải cấp lại các khóa trao đổi (Lương → Khóa trao đổi). Khi nâng cấp, khóa đang lưu dạng thường tự được mã hóa ở lần khởi động đầu. Chế độ LAN: mỗi máy chạy API cần cùng khóa chủ.

### SQLite (một máy)
- **Quản trị → Sao lưu**: tạo bản `.hakbk` (ảnh chụp trực tuyến, không cần dừng phần mềm), mã hóa AES-256-GCM bằng mật khẩu sao lưu ≥ 12 ký tự. **Mất mật khẩu sao lưu là mất bản sao lưu** — lưu mật khẩu tách biệt (két / quản lý mật khẩu).
- Nguyên tắc 3-2-1: 3 bản, 2 loại thiết bị, 1 bản ngoài văn phòng. Tối thiểu sao lưu cuối mỗi ngày làm việc và trước khi khóa sổ / nâng cấp.
- **Kiểm tra** định kỳ (hằng tháng): giải mã, kiểm SHA-256, `integrity_check`, chuỗi nhật ký kiểm toán của mọi công ty.
- **Khôi phục**: tạo tệp `restored-*.db` bên cạnh bản sao lưu (không ghi đè CSDL đang chạy). Sau đó: dừng phần mềm → đặt `Hak:DatabasePath` tới tệp khôi phục → khởi động → kiểm tra số liệu → giữ tệp cũ ít nhất đến khi xác nhận.

### PostgreSQL (LAN)
Chức năng sao lưu trong phần mềm trả `NOT_SUPPORTED`; dùng công cụ của PostgreSQL trên máy chủ (Task Scheduler):

```
pg_dump -h 127.0.0.1 -U hak_app -Fc -f D:\hak-backup\hak-%DATE%.dump hak
```

- Mã hóa tệp dump trước khi đưa ra ngoài (BitLocker / 7-Zip AES-256).
- Thử khôi phục định kỳ vào **CSDL mới**: `createdb hak_restore_test` rồi `pg_restore -d hak_restore_test <tệp>`; không khôi phục đè lên `hak` đang dùng.

## 6. Nâng cấp phiên bản

1. Sao lưu (mục 5) và kiểm tra bản sao lưu.
2. LAN: **dừng phần mềm trên tất cả máy trạm** (máy cũ chạy trên lược đồ mới có thể lỗi).
3. Thay chương trình, khởi động một máy trước: migration chạy tự động, quyền mặc định mới được cấp bổ sung cho vai trò có sẵn (ghi nhật ký `role.sync_defaults`).
4. Kiểm tra đăng nhập, bảng cân đối số phát sinh, rồi mới cập nhật các máy còn lại.

## 7. Bảo mật vận hành

- Sai mật khẩu 5 lần liên tiếp → khóa 15 phút (quản trị có thể đặt lại mật khẩu). Phiên đăng nhập tối đa 10 giờ.
- Ngừng dùng người dùng → mọi phiên của người đó bị đóng ngay.
- Sổ cái, sổ kho, phân bổ, khấu hao… **không sửa/xóa được** (trigger CSDL). Sai sót xử lý bằng chứng từ đảo/điều chỉnh. Tuyệt đối không sửa trực tiếp trong CSDL.
- Nhật ký kiểm toán là chuỗi băm; xem tại **Quản trị → Nhật ký**; bản sao lưu kiểm tra lại toàn chuỗi.

### 7a. Sự cố SEC-01: bí mật trong bảng chống gửi trùng — dọn dữ liệu và cấp lại khóa trao đổi
Phiên bản trước bản sửa SEC-01 lưu vào bảng `idempotency_records` (giữ 7 ngày, **đi theo bản sao lưu**): nguyên văn phản hồi khi **cấp khóa trao đổi** (có bí mật HMAC) và băm nội dung các yêu cầu đổi / đặt lại mật khẩu, tạo người dùng, mật khẩu sao lưu.

1. **Nâng cấp** theo mục 6. Ở lần khởi động đầu, phần mềm tự xóa mọi bản ghi như vậy (chạy lại mỗi lần khởi động, không có gì để xóa thì bỏ qua); log báo `Đã xóa N bản ghi chống gửi trùng chứa bí mật; K khóa trao đổi cần cấp lại`, nhật ký kiểm toán hệ thống có `idempotency.purge_secrets`.
2. Khóa trao đổi có bí mật nằm trong các bản ghi đó được ghi nhật ký `exchange_key.exposed` và hiện **"Đang dùng — đã lộ, cần cấp lại"** ở **Lương → Khóa trao đổi dữ liệu**.
3. **Cấp lại từng khóa bị đánh dấu**: bấm **Cấp lại** → nhập mã khóa mới (khác mã cũ, ví dụ `2026-02`) và lý do. Trong một bước, khóa cũ ngừng dùng và khóa mới được cấp; bí mật mới **chỉ hiện một lần** — cấu hình ngay vào hệ thống gửi (phần mềm lương…). Từ lúc này gói ký bằng khóa cũ bị từ chối (`UNKNOWN_SOURCE`), nên hẹn trước với bên gửi. Nhật ký: `exchange_key.rotate` (không bao giờ chứa bí mật).
4. **Nên cấp lại cả khóa không bị đánh dấu** nếu được cấp trước bản sửa: bảng chỉ giữ 7 ngày nên khóa cấp sớm hơn không còn dấu vết để đánh dấu, nhưng có thể đã nằm trong **bản sao lưu** lập trong 7 ngày sau khi cấp.
5. **Bản sao lưu `.hakbk` lập trước khi nâng cấp** vẫn chứa các bản ghi cũ (đã mã hóa bằng mật khẩu sao lưu). Sau bước 3 bí mật cũ vô hiệu; vẫn giữ các tệp này như tài liệu mật và không chia sẻ mật khẩu sao lưu. Người dùng đã đổi / được đặt lại mật khẩu trong thời gian dùng bản cũ: nếu mật khẩu yếu thì nên đổi lại (bảng chỉ lưu băm SHA-256 của nội dung, đoán được mật khẩu ngắn).

## 8. Sự cố thường gặp

| Hiện tượng | Nguyên nhân / cách xử lý |
|---|---|
| API không khởi động: cổng đang dùng | Phiên bản khác đang chạy — tắt `Hak.Api.exe` cũ hoặc đổi `--urls` |
| `Hak:Provider=PostgreSQL cần chuỗi kết nối…` | Chưa đặt biến `ConnectionStrings__Hak` cho đúng tài khoản Windows |
| `CONCURRENCY_CONFLICT` | Người khác vừa sửa cùng dữ liệu — tải lại rồi làm lại |
| `LEDGER_IMMUTABLE` | Cố sửa dữ liệu đã ghi sổ — dùng đảo/điều chỉnh |
| `RULE_ROLE_UNMAPPED` | Thiếu ánh xạ vai trò TK — Thiết lập → Vai trò tài khoản |
| Tài khoản bị khóa | Chờ 15 phút hoặc quản trị đặt lại mật khẩu |
| Quên mật khẩu quản trị duy nhất | Trên máy chạy API (cùng cấu hình CSDL), mở cửa sổ dòng lệnh: `Hak.Api.exe --reset-admin-password <tên đăng nhập>` → nhập mật khẩu mới 2 lần (không hiện ký tự). Chỉ áp dụng cho quản trị hệ thống đang hoạt động; tài khoản được mở khóa, mọi phiên bị đóng, ghi nhật ký `user.password_recover_local` (tên máy, tài khoản Windows). Vẫn nên duy trì ít nhất 2 quản trị hệ thống |

## Ảnh đính kèm và điện thoại
- Ảnh sự cố / nghiệm thu nằm ở thư mục `attachments` cạnh tệp cơ sở dữ liệu (đổi bằng cấu hình `Hak:AttachmentsPath`). **Bản sao lưu mã hóa chỉ gồm cơ sở dữ liệu — sao chép thư mục `attachments` cùng lúc khi sao lưu** (mỗi tệp có mã SHA-256 trong cơ sở dữ liệu để kiểm tra).
- Điện thoại truy cập bằng địa chỉ IP của máy chủ trong mạng Wi-Fi nội bộ (ví dụ `http://192.168.1.10:5080`); mở cổng đó trên tường lửa Windows cho mạng riêng. Quét QR bằng ứng dụng Camera của điện thoại. Quét ngay trong trang và cài ứng dụng offline cần HTTPS (đặt sau một proxy có chứng chỉ).
