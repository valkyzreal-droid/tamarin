; Tamarin installer (Inno Setup 6). Per-user install: no administrator rights needed.
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

[Setup]
AppId={{7C1B5F0E-3D52-4B8A-9E4A-5A6B7D2C9F10}
AppName=Tamarin
AppVersion={#AppVersion}
AppPublisher=Tamarin contributors
DefaultDirName={localappdata}\Programs\Tamarin
DefaultGroupName=Tamarin
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\out
OutputBaseFilename=Tamarin-Setup
SetupIconFile=..\src\Tamarin.App\Assets\tamarin.ico
UninstallDisplayIcon={app}\Tamarin.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
CloseApplications=yes
LicenseFile=..\LICENSE

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startup"; Description: "Iniciar com o Windows / Start with Windows"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\out\Tamarin.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Tamarin"; Filename: "{app}\Tamarin.exe"
Name: "{autodesktop}\Tamarin"; Filename: "{app}\Tamarin.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Tamarin"; ValueData: """{app}\Tamarin.exe"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\Tamarin.exe"; Description: "{cm:LaunchProgram,Tamarin}"; Flags: nowait postinstall skipifsilent
; Notes in %APPDATA%\Tamarin are never touched by the uninstaller.
