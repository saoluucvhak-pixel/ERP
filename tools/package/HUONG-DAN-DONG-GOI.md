# Đóng gói bộ cài HAK (Windows x64)

Có hai cách. Cả hai đều tự chứa .NET, máy người dùng không cần cài thêm gì.

## 1. Gói chạy độc lập (zip) — làm được trên mọi hệ điều hành
```
npm --prefix web ci && npm --prefix web run build
dotnet publish src/Hak.Api -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/HAK
```
Rồi đổi `Hak.Api.exe` → `HAK.exe`, chép `web/dist` thành `artifacts/HAK/wwwroot`, chép các tệp `*.cmd` và `DOC-TRUOC.txt` trong `tools/package`, xóa `*.pdb`, tạo `SHA256SUMS.txt`, nén thư mục `HAK` thành `HAK-Windows-x64.zip`. Trên Windows chỉ cần chạy `tools\package\package.ps1` (làm đúng các bước này).

Người dùng giải nén, chạy `Chay HAK.cmd` (sổ chính thức) hoặc `Chay HAK - thu nghiem.cmd` (CSDL thử). Dữ liệu nằm ở `%LOCALAPPDATA%\HAK\<Production|Test>`, không nằm trong thư mục chương trình.

## 2. Bộ cài `HAK-Setup.exe` (Inno Setup 6) — cần Windows
Sau bước 1 (có thư mục `artifacts\HAK`): `iscc /DAppVersion=1.0.0 tools\package\HAK.iss` → `artifacts\HAK-Setup.exe`. Cài theo người dùng, không cần quyền quản trị, có biểu tượng Start Menu / Desktop và gỡ cài đặt không xóa dữ liệu.

## Chưa có
Bộ cài Tauri/MSI (cần Visual Studio C++ và quyền quản trị) và ký số bộ cài (cần chứng thư số của công ty). Gói zip và HAK-Setup.exe chưa ký nên Windows SmartScreen sẽ cảnh báo "nhà phát hành không xác định".
