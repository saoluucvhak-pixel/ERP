@echo off
rem HAK Accounting - moi truong THU NGHIEM (Test): CSDL rieng %LOCALAPPDATA%\HAK\Test\hak.db, cho phep ghi so khi che do ke toan
rem chua xac minh. Chi dung de lam quen / doi chieu thu; so lieu o day khong phai so sach chinh thuc.
rem Chi dung chu khong dau va xuong dong CRLF: cmd.exe doc sai tep UTF-8 / LF.
cd /d "%~dp0"
rem Thu muc du lieu do nguoi dung chon (file nam NGOAI thu muc chuong trinh nen go chuong trinh khong mat). Khong co file thi dung %LOCALAPPDATA%\HAK.
if exist "%LOCALAPPDATA%\HAK\data-location.txt" set /p Hak__DataRoot=<"%LOCALAPPDATA%\HAK\data-location.txt"
set URL=http://127.0.0.1:5081/
rem Dang chay san thi chi mo trinh duyet, khong chay them ban thu hai.
curl.exe -s -o nul --max-time 2 %URL%api/setup/status && goto open
set Hak__Environment=Test
start "HAK thu nghiem" /min "%~dp0HAK.exe" --urls http://127.0.0.1:5081
echo Dang khoi dong HAK (thu nghiem), vui long cho (lan dau co the mat 20-60 giay)...
set /a n=0
:wait
timeout /t 1 /nobreak >nul
curl.exe -s -o nul --max-time 2 %URL%api/setup/status && goto open
set /a n+=1
if %n% lss 90 goto wait
echo.
echo KHONG KHOI DONG DUOC HAK (thu nghiem) sau 90 giay.
echo Hay chup man hinh cua so "HAK thu nghiem" (dang thu nho tren thanh tac vu) va gui cho bo phan ho tro.
pause
exit /b 1
:open
start "" %URL%
echo.
echo HAK (thu nghiem) dang chay tai %URL%
echo De tat: dong cua so "HAK thu nghiem" dang thu nho tren thanh tac vu.
timeout /t 8 >nul
