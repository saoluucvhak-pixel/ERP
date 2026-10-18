# HAK Accounting Offline ERP

Phần mềm kế toán offline cho Windows (SQLite một máy / PostgreSQL LAN). Đã xong lõi Giai đoạn 1 và phần lớn Giai đoạn 2 (công nợ, quy tắc định khoản, thu/chi tiền, kết chuyển, giao diện web đầu tiên). Xem [STATUS.md](STATUS.md) để biết chính xác cái gì đã làm và chưa làm.

## Cấu trúc

```
docs/        SRS & BRM, kiến trúc & ERD, đặc tả engine, JSON, báo cáo, kiểm thử, sổ pháp lý, vận hành, hiệu năng
schemas/     JSON Schema chính thức (journal-voucher, import-result, e-invoice-import, payroll) + ví dụ
legal/       Văn bản pháp lý đã nhập và mẫu báo cáo (TT99-2025: danh mục TK, mẫu B01/B02/B03/B09)
src/
  Hak.Domain         thực thể, Money, validator, máy trạng thái, Report Engine (không I/O)
  Hak.Data           EF Core, migration SQLite, interceptor + trigger bất biến
  Hak.Data.Postgres  migration và trigger PL/pgSQL cho chế độ LAN
  Hak.Application    các service nghiệp vụ: kế toán, công nợ, kho, mua/bán, thuế, TSCĐ, ngoại tệ, vay,
                     lương, giá thành, ngân hàng, báo cáo, sao lưu, RBAC, audit chuỗi băm
  Hak.Api            ASP.NET Core API (cũng phục vụ giao diện web khi đóng gói)
web/         React + TypeScript (Vite): giao diện theo phân hệ, mỗi phân hệ có tab "Quy trình"
tests/
  Hak.Domain.Tests       unit test
  Hak.Integration.Tests  integration test trên SQLite thật (và PostgreSQL khi đặt HAK_TEST_PG) + API qua HTTP
tools/
  Hak.Bench      đo hiệu năng
  migrate-fast   chuyển dữ liệu từ FAST (chỉ đọc) và đối chiếu BCĐPS
  package        đóng gói bản chạy độc lập
.github/       CI (backend + frontend)
```

## Build & test

Yêu cầu .NET SDK 10 và Node.js 24. Trên máy này .NET SDK nằm tại `%LOCALAPPDATA%\Microsoft\dotnet` (chưa thêm vào PATH):

```powershell
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test
```

```powershell
npm --prefix web test; npm --prefix web run build
```

## Chạy thử trên máy phát triển

1. API (môi trường Development, DB riêng tại `%LOCALAPPDATA%\HAK\Development\hak.db`):

```powershell
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" run --project src/Hak.Api --urls http://127.0.0.1:5080 --Hak:Environment=Development
```

2. Giao diện (Vite proxy `/api` sang cổng 5080), mở http://127.0.0.1:5173:

```powershell
npm --prefix web run dev
```

3. Dữ liệu thử (chỉ chạy khi API báo Development; mật khẩu sinh ngẫu nhiên, ghi vào `web/.dev-credentials.json`, không commit):

```powershell
npm --prefix web run seed:dev
```

Cấu hình API: `Hak:Environment` (Development/Test/Production), `Hak:DatabasePath`. Với DB trống, gọi `POST /api/setup/bootstrap` để tạo quản trị viên.

## Nguyên tắc không thương lượng

Ghi sổ ACID; sổ cái bất biến (sửa bằng đảo/điều chỉnh); decimal, không float (giao diện xử lý tiền bằng chuỗi/BigInt); JSON không phải DB kế toán; không seed tài khoản/biểu mẫu pháp lý chưa xác minh; test không bao giờ chạm DB thật; quyền kiểm tra ở backend.
