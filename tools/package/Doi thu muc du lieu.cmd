@echo off
rem HAK - doi thu muc luu du lieu (chi dung chu khong dau; CRLF). Du lieu CU duoc CHEP sang noi moi va GIU NGUYEN o cho cu den khi ban tu xoa.
setlocal
cd /d "%~dp0"
set "LOC=%LOCALAPPDATA%\HAK\data-location.txt"
set "OLD=%LOCALAPPDATA%\HAK"
if exist "%LOC%" set /p OLD=<"%LOC%"
echo Thu muc du lieu hien tai: %OLD%
echo TAT HAK truoc khi tiep tuc (dong cua so "HAK" tren thanh tac vu).
set "NEW="
set /p NEW=Thu muc du lieu MOI (vi du D:\HAK-DuLieu): 
if "%NEW%"=="" (echo Khong doi gi. & pause & exit /b 1)
if /i "%NEW%"=="%OLD%" (echo Trung thu muc cu. Khong doi gi. & pause & exit /b 1)
if not exist "%NEW%" mkdir "%NEW%"
if not exist "%NEW%" (echo Khong tao duoc thu muc %NEW%. & pause & exit /b 1)
for %%e in (Production Test) do if exist "%OLD%\%%e" (
  echo Dang chep %%e ...
  robocopy "%OLD%\%%e" "%NEW%\%%e" /E /COPY:DAT /R:2 /W:2 /NFL /NDL /NJH /NJS >nul
  if errorlevel 8 (echo LOI khi chep %%e. Khong doi gi. & pause & exit /b 1)
)
>"%LOC%" echo %NEW%
echo.
echo Da chep du lieu sang %NEW% va ghi nho thu muc moi.
echo Mo HAK bang "Chay HAK.cmd", kiem tra du lieu dung, roi tu xoa thu muc cu %OLD% neu muon.
pause
