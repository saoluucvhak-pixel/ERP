; HAK Accounting - bo cai dat Windows (Inno Setup 6). Dong goi thu muc artifacts\HAK do tools\package\package.ps1 tao ra.
; Bien dich: iscc /DAppVersion=1.0.0 tools\package\HAK.iss  ->  artifacts\HAK-Setup.exe
; Cai theo nguoi dung (khong can quyen quan tri). Du lieu nam o %LOCALAPPDATA%\HAK\<Production|Test> nen go chuong trinh khong mat du lieu.
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{FB0C342C-8351-4596-B623-5B80118D1B32}
AppName=HAK Accounting
AppVersion={#AppVersion}
AppPublisher=HAK Group
DefaultDirName={localappdata}\Programs\HAK
DefaultGroupName=HAK Accounting
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
OutputDir=..\..\artifacts
OutputBaseFilename=HAK-Setup
Compression=lzma2/max
SolidCompression=yes
DisableProgramGroupPage=yes
WizardStyle=modern
UninstallDisplayName=HAK Accounting

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Tao bieu tuong ngoai man hinh (Desktop)"; GroupDescription: "Bieu tuong:"

[Files]
Source: "..\..\artifacts\HAK\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\HAK Accounting"; Filename: "{app}\Chay HAK.cmd"; WorkingDir: "{app}"; IconFilename: "{app}\HAK.exe"
Name: "{autoprograms}\HAK Accounting - thu nghiem"; Filename: "{app}\Chay HAK - thu nghiem.cmd"; WorkingDir: "{app}"; IconFilename: "{app}\HAK.exe"
Name: "{autoprograms}\HAK - Khoi phuc mat khau quan tri"; Filename: "{app}\Khoi phuc mat khau quan tri.cmd"; WorkingDir: "{app}"; IconFilename: "{app}\HAK.exe"
Name: "{autodesktop}\HAK Accounting"; Filename: "{app}\Chay HAK.cmd"; WorkingDir: "{app}"; IconFilename: "{app}\HAK.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Chay HAK.cmd"; WorkingDir: "{app}"; Description: "Mo HAK Accounting ngay"; Flags: postinstall nowait skipifsilent shellexec

[UninstallRun]
Filename: "{cmd}"; Parameters: "/c taskkill /im HAK.exe /f"; Flags: runhidden; RunOnceId: "StopHak"
