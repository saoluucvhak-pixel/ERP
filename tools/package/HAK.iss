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

[Code]
{ Thu muc du lieu: nguoi dung chon luc cai (mac dinh %LOCALAPPDATA%\HAK hoac noi da chon truoc do). Luu vao %LOCALAPPDATA%\HAK\data-location.txt,
  tep nay nam NGOAI thu muc chuong trinh nen go chuong trinh hoac cai lai khong lam mat thu muc du lieu cung nhu viec chon noi luu. }
var
  DataPage: TInputDirWizardPage;

function LocationFile(): String;
begin
  Result := ExpandConstant('{localappdata}\HAK\data-location.txt');
end;

function CurrentDataRoot(): String;
var
  Lines: TArrayOfString;
begin
  Result := ExpandConstant('{localappdata}\HAK');
  if LoadStringsFromFile(LocationFile(), Lines) and (GetArrayLength(Lines) > 0) and (Trim(Lines[0]) <> '') then
    Result := Trim(Lines[0]);
end;

procedure InitializeWizard();
begin
  DataPage := CreateInputDirPage(wpSelectDir, 'Thu muc luu du lieu', 'Chon noi luu so sach cua HAK',
    'Du lieu (co so du lieu, anh dinh kem, ban sao luu, khoa chu) se nam o thu muc nay va se KHONG bi xoa khi go chuong trinh. ' +
    'Nen chon o dia khac o Windows hoac o co sao luu (vi du D:\HAK-DuLieu). Co the doi sau bang "Doi thu muc du lieu.cmd".', False, '');
  DataPage.Add('');
  DataPage.Values[0] := CurrentDataRoot();
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    ForceDirectories(ExpandConstant('{localappdata}\HAK'));
    ForceDirectories(DataPage.Values[0]);
    SaveStringToFile(LocationFile(), DataPage.Values[0] + #13#10, False);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usDone then
    MsgBox('Da go chuong trinh HAK.' + #13#10 + #13#10 + 'DU LIEU DUOC GIU NGUYEN tai:' + #13#10 + CurrentDataRoot() + #13#10 + #13#10 +
      'Chuong trinh khong xoa du lieu. Muon xoa, hay tu xoa thu muc do sau khi da sao luu.', mbInformation, MB_OK);
end;
