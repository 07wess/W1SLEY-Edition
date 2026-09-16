#ifndef MyAppVersion
  #define MyAppVersion "0.1.2"
#endif

#define MyAppName "W1SLEY Edition"
#define MyAppPublisher "W1SLEY Edition contributors"
#define MyAppExeName "W1SLEYEdition.exe"

[Setup]
AppId={{B50C1B4B-3837-471B-90AF-9184434B0D72}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\W1SLEY Edition
DefaultGroupName=W1SLEY Edition
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\artifacts\installer
OutputBaseFilename=W1SLEY-Edition-Setup-x64
SetupIconFile=..\Assets\w1sley.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Masaüstü kısayolu oluştur"; GroupDescription: "Ek görevler:"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\W1SLEY Edition"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\W1SLEY Edition"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "W1SLEY Edition'ı başlat"; Flags: nowait postinstall skipifsilent runascurrentuser
