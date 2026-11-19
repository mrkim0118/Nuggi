; Nuggi installer (Inno Setup 6). Built by publish.bat from publish\Nuggi-win-x64:
;   ISCC.exe /DAppVersion=1.3.1 installer\Nuggi.iss
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
; Length of the longest file path inside the package (publish.bat measures it).
#ifndef MaxRelPath
  #define MaxRelPath "125"
#endif

[Setup]
AppId={{6F2B9C1E-4D7A-4E3B-9B6F-2C8E5A1D7F40}
AppName=Nuggi
AppVersion={#AppVersion}
AppVerName=Nuggi {#AppVersion}
AppPublisher=Manggdy
AppCopyright=Copyright © Manggdy
AppPublisherURL=https://github.com/mrkim0118/Nuggi
AppSupportURL=https://github.com/mrkim0118/Nuggi/issues
VersionInfoVersion={#AppVersion}
VersionInfoCompany=Manggdy
VersionInfoCopyright=Copyright © Manggdy
VersionInfoDescription=Nuggi Setup
; Per-user install by default (no admin rights); the start dialog also offers an all-users install.
; {autopf} is %LOCALAPPDATA%\Programs for one user and Program Files for all users.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\Nuggi
; Always let the user pick the install folder, also when updating an existing install.
DisableDirPage=no
DefaultGroupName=Nuggi
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\Assets\icon.ico
UninstallDisplayIcon={app}\Nuggi.exe
UninstallDisplayName=Nuggi
OutputDir=..\publish
OutputBaseFilename=Nuggi-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
LZMANumBlockThreads=4
WizardStyle=modern

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
korean.DotNetMissing=Nuggi를 실행하려면 Microsoft .NET 8 Desktop Runtime (x64)이 필요합니다.%n%n지금 다운로드 페이지를 열까요? 런타임을 설치한 뒤 Nuggi를 실행하세요.
english.DotNetMissing=Nuggi needs the Microsoft .NET 8 Desktop Runtime (x64).%n%nOpen the download page now? Install the runtime, then start Nuggi.
korean.PathTooLong=설치 경로가 너무 깁니다(%1자). Windows 경로 길이 제한 때문에 %2자 이하의 더 짧은 폴더를 골라 주세요.
english.PathTooLong=The install path is too long (%1 characters). Because of the Windows path length limit, choose a shorter folder (%2 characters or fewer).

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\publish\Nuggi-win-x64\*"; DestDir: "{app}"; Excludes: "__pycache__"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Nuggi"; Filename: "{app}\Nuggi.exe"
Name: "{autodesktop}\Nuggi"; Filename: "{app}\Nuggi.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Nuggi.exe"; Description: "{cm:LaunchProgram,Nuggi}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Caches Python and numba write at run time. The output folder (saved results) is left alone.
Type: filesandordirs; Name: "{app}\python"
Type: filesandordirs; Name: "{app}\nuggi\__pycache__"
Type: filesandordirs; Name: "{app}\scripts\__pycache__"

[Code]
// Windows paths are limited to 259 characters; the deepest package file must still fit under the chosen folder.
function MaxInstallPathLength(): Integer;
begin
  Result := 259 - 1 - {#MaxRelPath} - 4;
end;

function InstallPathError(Dir: String): String;
begin
  Result := '';
  if Length(RemoveBackslash(Dir)) > MaxInstallPathLength() then
    Result := FmtMessage(CustomMessage('PathTooLong'), [IntToStr(Length(RemoveBackslash(Dir))), IntToStr(MaxInstallPathLength())]);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Error: String;
begin
  Result := True;
  if CurPageID = wpSelectDir then
  begin
    Error := InstallPathError(WizardDirValue);
    if Error <> '' then
    begin
      MsgBox(Error, mbError, MB_OK);
      Result := False;
    end;
  end;
end;

// Also guards silent installs (/DIR=...), which skip the folder page.
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := InstallPathError(WizardDirValue);
end;

function IsDotNet8DesktopInstalled(): Boolean;
var
  FindRec: TFindRec;
begin
  Result := FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\8.*'), FindRec);
  if Result then
    FindClose(FindRec);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ErrorCode: Integer;
begin
  if (CurStep = ssPostInstall) and (not WizardSilent) and (not IsDotNet8DesktopInstalled()) then
    if MsgBox(CustomMessage('DotNetMissing'), mbConfirmation, MB_YESNO) = IDYES then
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/8.0', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
end;
