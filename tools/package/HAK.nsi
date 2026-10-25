; HAK Accounting - bo cai Windows (NSIS 3, Unicode). Dong goi thu muc artifacts/HAK (package.ps1 hoac cac buoc trong HUONG-DAN-DONG-GOI.md).
; Bien dich (Linux hoac Windows):  makensis -DAPPVERSION=1.0.0 tools/package/HAK.nsi   ->  artifacts/HAK-Setup.exe
; Cai theo nguoi dung (khong can quyen quan tri). Du lieu nam o THU MUC DO NGUOI DUNG CHON (mac dinh %LOCALAPPDATA%\HAK), tach rieng khoi chuong trinh:
; go chuong trinh KHONG BAO GIO xoa du lieu. Lua chon luu o %LOCALAPPDATA%\HAK\data-location.txt (cac tep .cmd doc tep nay).
Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"

!ifndef APPVERSION
  !define APPVERSION "1.0.0"
!endif
!define UNINSTKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\HAKAccounting"

Name "HAK Accounting ${APPVERSION}"
OutFile "../../artifacts/HAK-Setup.exe"
RequestExecutionLevel user
InstallDir "$LOCALAPPDATA\Programs\HAK"
InstallDirRegKey HKCU "Software\HAK" "InstallDir"
BrandingText "HAK Accounting ${APPVERSION}"
SetCompressor /SOLID lzma
Var DataDir

!define MUI_ABORTWARNING
!insertmacro MUI_PAGE_WELCOME

!define MUI_PAGE_HEADER_TEXT "Thư mục chương trình"
!define MUI_PAGE_HEADER_SUBTEXT "Nơi cài các tệp chương trình HAK."
!insertmacro MUI_PAGE_DIRECTORY

!define MUI_PAGE_HEADER_TEXT "Thư mục lưu dữ liệu"
!define MUI_PAGE_HEADER_SUBTEXT "Chọn nơi lưu sổ sách của HAK."
!define MUI_DIRECTORYPAGE_TEXT_TOP "Cơ sở dữ liệu, ảnh đính kèm, bản sao lưu và khóa chủ sẽ nằm ở thư mục này và KHÔNG bị xóa khi gỡ chương trình. Nên chọn ổ khác ổ Windows hoặc ổ có sao lưu (ví dụ D:\HAK-DuLieu). Nên dùng tên thư mục không dấu. Đổi sau này bằng 'Doi thu muc du lieu.cmd'."
!define MUI_DIRECTORYPAGE_TEXT_DESTINATION "Thư mục dữ liệu"
!define MUI_DIRECTORYPAGE_VARIABLE $DataDir
!insertmacro MUI_PAGE_DIRECTORY

!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\Chay HAK.cmd"
!define MUI_FINISHPAGE_RUN_TEXT "Mở HAK Accounting ngay"
!define MUI_FINISHPAGE_RUN_NOTCHECKED
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Vietnamese"

; Doc thu muc du lieu da luu (mac dinh %LOCALAPPDATA%\HAK); bo ky tu xuong dong cuoi dong.
!macro ReadDataDirFn un
Function ${un}ReadDataDir
  StrCpy $DataDir "$LOCALAPPDATA\HAK"
  ClearErrors
  FileOpen $0 "$LOCALAPPDATA\HAK\data-location.txt" r
  ${If} ${Errors}
    Return
  ${EndIf}
  FileRead $0 $1
  FileClose $0
  trim:
  StrCpy $2 $1 1 -1
  ${If} $2 == "$\r"
  ${OrIf} $2 == "$\n"
    StrCpy $1 $1 -1
    Goto trim
  ${EndIf}
  ${If} $1 != ""
    StrCpy $DataDir $1
  ${EndIf}
FunctionEnd
!macroend
!insertmacro ReadDataDirFn ""
!insertmacro ReadDataDirFn "un."

Function .onInit
  Call ReadDataDir
FunctionEnd

; Khong cho cai vao goc o dia / thu muc he thong de go chuong trinh khong the dung nham.
Function .onVerifyInstDir
  StrLen $0 $INSTDIR
  ${If} $0 < 6
    Abort
  ${EndIf}
FunctionEnd

Section "HAK Accounting" SecMain
  SetOutPath "$INSTDIR"
  ; Cai de len ban cu: tat HAK neu dang chay (tep dang mo khong ghi de duoc).
  nsExec::Exec 'taskkill /im HAK.exe /f'
  Sleep 800
  File /r "../../artifacts/HAK/*.*"

  ; Ghi nho thu muc du lieu (NGOAI thu muc chuong trinh) va tao thu muc.
  CreateDirectory "$LOCALAPPDATA\HAK"
  CreateDirectory "$DataDir"
  FileOpen $0 "$LOCALAPPDATA\HAK\data-location.txt" w
  FileWrite $0 "$DataDir$\r$\n"
  FileClose $0

  CreateDirectory "$SMPROGRAMS\HAK Accounting"
  CreateShortcut "$SMPROGRAMS\HAK Accounting\HAK Accounting.lnk" "$INSTDIR\Chay HAK.cmd" "" "$INSTDIR\HAK.exe" 0 SW_SHOWMINIMIZED
  CreateShortcut "$SMPROGRAMS\HAK Accounting\HAK Accounting - thu nghiem.lnk" "$INSTDIR\Chay HAK - thu nghiem.cmd" "" "$INSTDIR\HAK.exe" 0 SW_SHOWMINIMIZED
  CreateShortcut "$SMPROGRAMS\HAK Accounting\HAK - Doi thu muc du lieu.lnk" "$INSTDIR\Doi thu muc du lieu.cmd" "" "$INSTDIR\HAK.exe" 0
  CreateShortcut "$SMPROGRAMS\HAK Accounting\HAK - Cai dat sao luu tu dong.lnk" "$INSTDIR\Cai dat sao luu tu dong.cmd" "" "$INSTDIR\HAK.exe" 0
  CreateShortcut "$SMPROGRAMS\HAK Accounting\HAK - Khoi phuc mat khau quan tri.lnk" "$INSTDIR\Khoi phuc mat khau quan tri.cmd" "" "$INSTDIR\HAK.exe" 0
  CreateShortcut "$SMPROGRAMS\HAK Accounting\Go cai dat HAK.lnk" "$INSTDIR\Uninstall.exe"
  CreateShortcut "$DESKTOP\HAK Accounting.lnk" "$INSTDIR\Chay HAK.cmd" "" "$INSTDIR\HAK.exe" 0 SW_SHOWMINIMIZED

  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\HAK" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayName" "HAK Accounting"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayVersion" "${APPVERSION}"
  WriteRegStr HKCU "${UNINSTKEY}" "Publisher" "HAK Group"
  WriteRegStr HKCU "${UNINSTKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayIcon" "$INSTDIR\HAK.exe"
  WriteRegStr HKCU "${UNINSTKEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoRepair" 1
SectionEnd

; Go cai dat: CHI xoa tep chuong trinh do bo cai tao ra. KHONG dong vao thu muc du lieu.
Section "Uninstall"
  nsExec::Exec 'taskkill /im HAK.exe /f'
  Sleep 800
  RMDir /r "$INSTDIR\wwwroot"
  Delete "$INSTDIR\HAK.exe"
  Delete "$INSTDIR\Chay HAK.cmd"
  Delete "$INSTDIR\Chay HAK - thu nghiem.cmd"
  Delete "$INSTDIR\Doi thu muc du lieu.cmd"
  Delete "$INSTDIR\Cai dat sao luu tu dong.cmd"
  Delete "$INSTDIR\Khoi phuc mat khau quan tri.cmd"
  Delete "$INSTDIR\DOC-TRUOC.txt"
  Delete "$INSTDIR\SHA256SUMS.txt"
  Delete "$INSTDIR\appsettings.json"
  Delete "$INSTDIR\appsettings.Development.json"
  Delete "$INSTDIR\web.config"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  RMDir /r "$SMPROGRAMS\HAK Accounting"
  Delete "$DESKTOP\HAK Accounting.lnk"
  DeleteRegKey HKCU "${UNINSTKEY}"
  DeleteRegKey HKCU "Software\HAK"
  ; Cho nguoi dung biet du lieu con o dau.
  Call un.ReadDataDir
  MessageBox MB_OK|MB_ICONINFORMATION "Đã gỡ chương trình HAK.$\r$\n$\r$\nDỮ LIỆU ĐƯỢC GIỮ NGUYÊN tại:$\r$\n$DataDir$\r$\n$\r$\nChương trình không xóa dữ liệu. Muốn xóa, hãy tự xóa thư mục đó sau khi đã sao lưu."
SectionEnd
