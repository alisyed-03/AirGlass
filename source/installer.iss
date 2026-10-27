; AirGlass Windows installer. GPL-3.0-or-later.
; Compile with Inno Setup 6.7+: ISCC.exe installer.iss
#ifndef AppRoot
  #define AppRoot AddBackslash(SourcePath) + ".."
#endif
#ifndef InstallerOutput
  #define InstallerOutput AddBackslash(AppRoot) + ".."
#endif

[Setup]
AppId={{C24FAED9-F1B9-46A7-905D-45D6C2AD58CC}
AppName=AirGlass
AppVersion=2.7.2
AppVerName=AirGlass 2.7.2
AppPublisher=Ali Syed
DefaultDirName={localappdata}\Programs\AirGlass
DefaultGroupName=AirGlass
PrivilegesRequired=lowest
; x64compatible includes Windows 11 ARM64 with x64 emulation.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#InstallerOutput}
OutputBaseFilename=AirGlass-Setup-2.7.2
SetupIconFile={#AppRoot}\assets\AirGlass.ico
UninstallDisplayIcon={app}\AirGlass.exe
LicenseFile={#AppRoot}\LICENSE.txt
InfoBeforeFile=installer-info.txt
WizardStyle=modern
DisableWelcomePage=no
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
AppMutex=Local\AirGlass.Receiver
UninstallDisplayName=AirGlass
VersionInfoVersion=2.7.2.0
VersionInfoDescription=AirGlass installer
DisableProgramGroupPage=yes
ChangesAssociations=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#AppRoot}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.log,receiver.pem,settings.xml,gstreamer-registry.bin,unins*.exe,unins*.dat"

[InstallDelete]
Type: files; Name: "{app}\engine\lib\gstreamer-1.0\libgstnvcodec.dll"

[Icons]
Name: "{group}\AirGlass"; Filename: "{app}\AirGlass.exe"; WorkingDir: "{app}"
Name: "{group}\AirGlass README"; Filename: "{app}\README.md"
Name: "{autodesktop}\AirGlass"; Filename: "{app}\AirGlass.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "https://developer.apple.com/bonjour/"; Description: "Open Apple's Bonjour download page (required for iPhone discovery)"; Flags: postinstall shellexec skipifsilent; Check: not BonjourInstalled
Filename: "{app}\AirGlass.exe"; Description: "Open AirGlass"; WorkingDir: "{app}"; Flags: postinstall nowait skipifsilent; Check: BonjourInstalled

[Code]
function BonjourInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\Bonjour Service');
end;

function InitializeSetup: Boolean;
var
  Release: Cardinal;
  ErrorCode: Integer;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) and (Release >= 528040);
  if not Result then begin
    Log('Required .NET Framework 4.8 is missing; installation has not changed the system.');
    if not WizardSilent then
      if MsgBox('AirGlass requires Microsoft .NET Framework 4.8. Install it, then run this installer again.' + #13#10 + #13#10 + 'Open the official Microsoft download page now?', mbInformation, MB_YESNO) = IDYES then
        ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet-framework/net48', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
  end;
end;

procedure InitializeWizard;
begin
  if not BonjourInstalled then
    WizardForm.WelcomeLabel2.Caption := 'This installs AirGlass and its private media runtime for your Windows account.' + #13#10 + #13#10 + 'Apple Bonjour is not installed. Bonjour is required for your iPhone to discover this PC. Setup can open Apple''s official download page when it finishes; install Bonjour and then open AirGlass.';
end;
