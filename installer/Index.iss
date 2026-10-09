#define MyAppName "Index"
#define MyAppPublisher "Index"
#define MyAppExeName "Index.exe"

#ifndef AppVersion
  #define AppVersion "0.0.14"
#endif
#ifndef SourceDir
  #error SourceDir must point to the self-contained publish directory.
#endif
#ifndef OutputDir
  #error OutputDir must point to the installer output directory.
#endif

[Setup]
AppId={{7E74AC57-9A61-4CE8-A2A8-9D9B60D80393}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppVerName={#MyAppName} {#AppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\Index
DefaultGroupName=Index
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=Index-Setup-{#AppVersion}-win-x64
SetupIconFile=..\src\Index\Assets\Index.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
ChangesAssociations=no
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=Index screenshot and local image library installer
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#AppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加快捷方式："; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Index"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\Index"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Code]
function IsDotNet9DesktopInstalled: Boolean;
var
  ResultCode: Integer;
begin
  // .NET installer writes version info to WOW6432Node (32-bit registry view).
  // {sys} resolves to SysWOW64 in the 32-bit setup.exe, so {sys}\reg.exe
  // is the 32-bit reg.exe that reads WOW6432Node.
  Exec(ExpandConstant('{sys}\reg.exe'),
       'query "HKLM\SOFTWARE\dotnet\setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App"',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := (ResultCode = 0);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if not IsDotNet9DesktopInstalled then
    begin
      MsgBox(
        'Index requires the .NET 9 Desktop Runtime.' + #13#10 +
        'Please download and install it from:' + #13#10 +
        'https://dotnet.microsoft.com/download/dotnet/9.0' + #13#10 +
        '(select "Desktop Runtime" x64)',
        mbInformation, MB_OK);
    end;
  end;
end;

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 Index"; Flags: nowait postinstall skipifsilent
