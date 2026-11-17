@echo off
rem Dat lai mat khau quan tri he thong (docs/08 muc 8). Tat HAK truoc khi chay. Hanh dong duoc ghi nhat ky kiem toan.
cd /d "%~dp0"
rem Thu muc du lieu do nguoi dung chon (file nam NGOAI thu muc chuong trinh nen go chuong trinh khong mat). Khong co file thi dung %LOCALAPPDATA%\HAK.
if exist "%LOCALAPPDATA%\HAK\data-location.txt" set /p Hak__DataRoot=<"%LOCALAPPDATA%\HAK\data-location.txt"
set Hak__Environment=Production
set /p HAKUSER=Ten dang nhap quan tri: 
"%~dp0HAK.exe" --reset-admin-password %HAKUSER%
pause
