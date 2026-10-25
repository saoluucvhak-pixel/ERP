@echo off
rem HAK - cai dat sao luu tu dong hang ngay (CHI dung chu khong dau; tep nay luu CRLF).
rem Hoi: mat khau sao luu (>= 12 ky tu), gio sao luu, thu muc chep them ra NGOAI may nay (o mang, USB, thu muc dong bo).
rem Luu vao bien moi truong CUA TAI KHOAN WINDOWS NAY (setx). Mat khau sao luu: cat rieng, mat la mat ban sao luu.
setlocal
echo.
echo === CAI DAT SAO LUU TU DONG HAK ===
set "PASS="
for /f "usebackq delims=" %%p in (`powershell -NoProfile -Command "$s=Read-Host \"Mat khau sao luu (>=12 ky tu)\" -AsSecureString; [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($s))"`) do set "PASS=%%p"
if "%PASS:~11,1%"=="" (echo Mat khau phai tu 12 ky tu. Khong thay doi gi. & pause & exit /b 1)
set "BTIME=22:00"
set /p BTIME=Gio sao luu hang ngay HH:mm [22:00]: 
set "COPY="
set /p COPY=Thu muc chep them ra ngoai may nay (vd D:\SaoLuuHAK hoac \\may-chu\sao-luu), de trong neu chua co: 
setx HAK_BACKUP_PASSPHRASE "%PASS%" >nul
setx Hak__AutoBackup__Enabled true >nul
setx Hak__AutoBackup__Time "%BTIME%" >nul
if defined COPY setx Hak__AutoBackup__CopyPath "%COPY%" >nul
echo.
echo Da cai dat. Hay TAT HAK va mo lai bang "Chay HAK.cmd" de ap dung.
echo Kiem tra: HAK ^> Quan tri ^> Sao luu (muc "Sao luu tu dong").
pause
