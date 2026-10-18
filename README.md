# HAK Accounting Offline ERP

Phần mềm kế toán offline cho Windows (SQLite một máy / PostgreSQL LAN). Đã xong lõi Giai đoạn 1 và phần lớn Giai đoạn 2 (công nợ, quy tắc định khoản, thu/chi tiền, kết chuyển, giao diện web đầu tiên). Xem [STATUS.md](STATUS.md) để biết chính xác cái gì đã làm và chưa làm.

## Cấu trúc

```
docs/        SRS & BRM, kiến trúc & ERD, đặc tả engine, JSON, báo cáo, kiểm thử, sổ pháp lý
schemas/     JSON Schema chính thức + ví dụ hợp lệ/lỗi
src/
  Hak.Domain        thực thể, Money, validator, máy trạng thái (không I/O)
  Hak.Data          EF Core, migration SQLite, interceptor + trigger bất biến sổ cái và phân bổ
  Hak.Application   Accounting Engine, công nợ (SettlementAllocation), Posting Rule Engine,
                    chứng từ thu/chi, kết chuyển, RBAC, audit chuỗi băm, JSON exchange, sổ & báo cáo
  Hak.Api           ASP.NET Core API cục bộ (sidecar cho Tauri)
web/         React + TypeScript (Vite): chứng từ, thu/chi, công nợ, BCĐPS, sổ chi tiết/sổ quỹ, kỳ kế toán
tests/
  Hak.Domain.Tests       unit test
  Hak.Integration.Tests  integration test trên SQLite thật + API qua HTTP
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
