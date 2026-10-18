@echo off
rem HAK Accounting - chay tren may nay (SQLite), moi truong Production.
rem Du lieu: %LOCALAPPDATA%\HAK\Production\hak.db ; sao luu: %LOCALAPPDATA%\HAK\Production\backups
rem Production chi cho ghi so khi che do ke toan cua cong ty da duoc xac minh (docs/08).
rem Chi dung chu khong dau va xuong dong CRLF: cmd.exe doc sai tep UTF-8 / LF.
cd /d "%~dp0"
set URL=http://127.0.0.1:5080/
rem Dang chay san thi chi mo trinh duyet, khong chay them ban thu hai.
curl.exe -s -o nul --max-time 2 %URL%api/setup/status && goto open
set Hak__Environment=Production
start "HAK" /min "%~dp0HAK.exe" --urls http://127.0.0.1:5080
echo Dang khoi dong HAK, vui long cho (lan dau co the mat 20-60 giay)...
set /a n=0
:wait
timeout /t 1 /nobreak >nul
curl.exe -s -o nul --max-time 2 %URL%api/setup/status && goto open
set /a n+=1
if %n% lss 90 goto wait
echo.
echo KHONG KHOI DONG DUOC HAK sau 90 giay.
echo Hay chup man hinh cua so "HAK" (dang thu nho tren thanh tac vu) va gui cho bo phan ho tro.
pause
exit /b 1
:open
start "" %URL%
echo.
echo HAK dang chay tai %URL%
echo De tat: dong cua so "HAK" dang thu nho tren thanh tac vu.
timeout /t 8 >nul
