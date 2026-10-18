@echo off
rem Dat lai mat khau quan tri he thong (docs/08 muc 8). Tat HAK truoc khi chay. Hanh dong duoc ghi nhat ky kiem toan.
cd /d "%~dp0"
set Hak__Environment=Production
set /p HAKUSER=Ten dang nhap quan tri: 
"%~dp0HAK.exe" --reset-admin-password %HAKUSER%
pause
