; Inno Setup script for Orchard. Built by CI (.github/workflows/build.yml):
;   ISCC.exe /DSourceExe=..\publish\Orchard.exe /DAppVersion=x.y.z installer\Orchard.iss
; Per-user install: no admin rights needed.

#define AppName "Orchard"
#ifndef AppVersion
  #define AppVersion "0.3.5"
#endif
#ifndef SourceExe
  #define SourceExe "..\publish\Orchard.exe"
#endif

[Setup]
AppId={{8C1B3E0A-6C2E-4C8E-9C59-0B1D9A7E2F31}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Orchard
AppPublisherURL=https://github.com/rayan-afkkk/macos-27
DefaultDirName={localappdata}\Programs\Orchard
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=Output
OutputBaseFilename=OrchardSetup
SetupIconFile=..\src\Orchard\Assets\orchard.ico
UninstallDisplayIcon={app}\Orchard.exe
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Tasks]
Name: "startup"; Description: "Open Orchard when I sign in"
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Orchard"; Filename: "{app}\Orchard.exe"
Name: "{autoprograms}\Orchard (try it in a window)"; Filename: "{app}\Orchard.exe"; Parameters: "--preview"
Name: "{autoprograms}\Restore Windows taskbar"; Filename: "{app}\Orchard.exe"; Parameters: "--restore"
Name: "{autodesktop}\Orchard"; Filename: "{app}\Orchard.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Orchard"; \
  ValueData: """{app}\Orchard.exe"""; Tasks: startup

[Run]
Filename: "{app}\Orchard.exe"; Description: "Start Orchard now"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Stop Orchard (and its watchdog), then put the taskbar and animations back before files are removed.
Filename: "{cmd}"; Parameters: "/C taskkill /IM Orchard.exe /F"; Flags: runhidden; RunOnceId: "StopOrchard"
Filename: "{app}\Orchard.exe"; Parameters: "--restore"; Flags: runhidden waituntilterminated; RunOnceId: "RestoreTaskbar"
Filename: "{cmd}"; Parameters: "/C reg delete HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v Orchard /f"; Flags: runhidden; RunOnceId: "RemoveStartup"

[Code]
// Updating while Orchard runs: stop it first. The next launch finds Orchard's restore file and repairs the taskbar
// state before taking over again, so nothing is left hidden.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{cmd}'), '/C taskkill /IM Orchard.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;
