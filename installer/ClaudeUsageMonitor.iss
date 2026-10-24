; Inno Setup script for Claude Usage Monitor.
; Build with installer\build-installer.ps1, which publishes the app and passes the version from the project file.
; Installs per user (no administrator rights), the same scope the app's own "start with Windows" setting uses.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish\win-x64"
#endif

#define AppName "Claude Usage Monitor"
#define AppExe "ClaudeUsageMonitor.exe"
#define AppPublisher "Akbar Goldoust"
#define AppUrl "https://github.com/agoldust/Claude-Monitor-Usage-Statics"
; Run-key value name used by Services\StartupRegistration.cs. Keep the two in step.
#define RunValueName "ClaudeUsageMonitor"
; WebView2 Runtime client id, used to detect an existing install.
#define WebView2Guid "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"

[Setup]
; Never change AppId: it links upgrades and the uninstaller to earlier installs.
AppId={{7C1F4E2A-5B8D-4E36-9A1C-3D2B6F8E0A47}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
AppCopyright=Copyright (c) 2026 {#AppPublisher}
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
LicenseFile=..\LICENSE
SetupIconFile=..\ClaudeUsageMonitor\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir=..\publish\installer
OutputBaseFilename=ClaudeUsageMonitor-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Closes a running copy before files are replaced. Matches InstanceName in App.xaml.cs.
AppMutex=ClaudeUsageMonitor.SingleInstance
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "Start with Windows"; GroupDescription: "Startup:"; Flags: unchecked
Name: "startup\widget"; Description: "Open only the widget at sign-in"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[InstallDelete]
; Clear the old app files on upgrade so removed DLLs don't linger.
Type: filesandordirs; Name: "{app}\*"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autoprograms}\{#AppName} Widget"; Filename: "{app}\{#AppExe}"; Parameters: "--widget"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Same command format as StartupRegistration.Command, so the app's Settings shows the option as on.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#RunValueName}"; ValueData: """{app}\{#AppExe}"""; Tasks: startup and not startup\widget
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#RunValueName}"; ValueData: """{app}\{#AppExe}"" --widget"; Tasks: startup\widget

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#AppExe}"; Flags: runhidden; RunOnceId: "StopApp"

[Code]
var
  DownloadPage: TDownloadWizardPage;

function WebView2Installed: Boolean;
var
  Version: String;
begin
  Result :=
    (RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{#WebView2Guid}', 'pv', Version) or
     RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{#WebView2Guid}', 'pv', Version) or
     RegQueryStringValue(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{#WebView2Guid}', 'pv', Version)) and
    (Version <> '') and (Version <> '0.0.0.0');
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage('Microsoft Edge WebView2 Runtime',
    'Sign-in and live plan limits need the WebView2 Runtime. Downloading it now...', nil);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if (CurPageID <> wpReady) or WebView2Installed then
    Exit;

  DownloadPage.Clear;
  DownloadPage.Add('https://go.microsoft.com/fwlink/p/?LinkId=2124703', 'MicrosoftEdgeWebview2Setup.exe', '');
  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
      DownloadPage.SetText('Installing the WebView2 Runtime...', '');
      if not Exec(ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe'), '/silent /install', '',
                  SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
        SuppressibleMsgBox('The WebView2 Runtime could not be installed (code ' + IntToStr(ResultCode) + '). ' +
          'Local usage still works; install WebView2 later from Microsoft to use sign-in and live limits.',
          mbInformation, MB_OK, IDOK);
    except
      SuppressibleMsgBox('The WebView2 Runtime could not be downloaded: ' + GetExceptionMessage + #13#10 +
        'Local usage still works; install WebView2 later from Microsoft to use sign-in and live limits.',
        mbInformation, MB_OK, IDOK);
    end;
  finally
    DownloadPage.Hide;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command: String;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  // Remove the startup entry only when it points at this install; it may also have been set from the app.
  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', '{#RunValueName}', Command) and
     (Pos(Lowercase(ExpandConstant('{app}\{#AppExe}')), Lowercase(Command)) > 0) then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', '{#RunValueName}');
end;
