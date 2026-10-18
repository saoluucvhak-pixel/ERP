; HAK Accounting - bo cai dat Windows (NSIS). Cai o muc nguoi dung, KHONG can quyen quan tri.
; Dung: makensis -DAPPDIR=<thu muc da publish> -DVERSION=<phien ban> -DOUTFILE=<ten file> tools/package/installer.nsi
; APPDIR phai co HAK.exe, wwwroot\, cac tep .cmd va DOC-TRUOC.txt (xem tools/package/build-installer.sh).
Unicode true
!ifndef VERSION
  !define VERSION "0.0.0"
!endif
!ifndef OUTFILE
  !define OUTFILE "HAK-Accounting-Setup.exe"
!endif
!define APP "HAK Accounting"
!define UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\HAKAccounting"

Name "${APP}"
OutFile "${OUTFILE}"
InstallDir "$LOCALAPPDATA\Programs\HAK"
RequestExecutionLevel user
SetCompressor /SOLID lzma
VIProductVersion "0.0.0.0"
VIAddVersionKey "ProductName" "${APP}"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "FileDescription" "${APP} - phan mem ke toan offline"

!include "MUI2.nsh"
!define MUI_ABORTWARNING
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\Chay HAK.cmd"
!define MUI_FINISHPAGE_RUN_TEXT "Chay HAK Accounting ngay"
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Section "Install"
  SetOutPath "$INSTDIR"
  ; Cai de len ban cu: giu nguyen du lieu (nam o %LOCALAPPDATA%\HAK\<Production|Test>, ngoai thu muc chuong trinh).
  File "${APPDIR}\HAK.exe"
  File "${APPDIR}\*.cmd"
  File "${APPDIR}\DOC-TRUOC.txt"
  SetOutPath "$INSTDIR\wwwroot"
  File /r "${APPDIR}\wwwroot\*.*"
  SetOutPath "$INSTDIR"
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  CreateDirectory "$SMPROGRAMS\${APP}"
  CreateShortcut "$SMPROGRAMS\${APP}\${APP}.lnk" "$INSTDIR\Chay HAK.cmd" "" "$INSTDIR\HAK.exe" 0 SW_SHOWMINIMIZED
  CreateShortcut "$SMPROGRAMS\${APP}\${APP} (thu nghiem).lnk" "$INSTDIR\Chay HAK - thu nghiem.cmd" "" "$INSTDIR\HAK.exe" 0 SW_SHOWMINIMIZED
  CreateShortcut "$SMPROGRAMS\${APP}\Doc truoc.lnk" "$INSTDIR\DOC-TRUOC.txt"
  CreateShortcut "$SMPROGRAMS\${APP}\Go cai dat.lnk" "$INSTDIR\Uninstall.exe"
  CreateShortcut "$DESKTOP\${APP}.lnk" "$INSTDIR\Chay HAK.cmd" "" "$INSTDIR\HAK.exe" 0 SW_SHOWMINIMIZED

  WriteRegStr HKCU "${UNINST_KEY}" "DisplayName" "${APP}"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINST_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoRepair" 1
SectionEnd

Section "Uninstall"
  ; Chi xoa chuong trinh. Du lieu ke toan o %LOCALAPPDATA%\HAK\ duoc GIU LAI.
  Delete "$DESKTOP\${APP}.lnk"
  RMDir /r "$SMPROGRAMS\${APP}"
  RMDir /r "$INSTDIR\wwwroot"
  Delete "$INSTDIR\HAK.exe"
  Delete "$INSTDIR\*.cmd"
  Delete "$INSTDIR\DOC-TRUOC.txt"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  DeleteRegKey HKCU "${UNINST_KEY}"
  MessageBox MB_OK "Da go chuong trinh. Du lieu ke toan van con o %LOCALAPPDATA%\HAK\ (khong bi xoa)."
SectionEnd
