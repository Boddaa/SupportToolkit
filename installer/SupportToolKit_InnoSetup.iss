; Script generated for Inno Setup Compiler
#define MyAppName "SupportToolKit"
#define MyAppVersion "2.5"
#define MyAppPublisher "Network & IT Infrastructure Operations"
#define MyAppExeName "SupportToolKit.exe"

[Setup]
AppId={{D8287F86-3B01-44FE-B375-927A9DE15003}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
OutputDir=g:\Bodda\NetworkDiscoveryTool\publish
OutputBaseFilename=SupportToolKit_Setup_v2.5
SetupIconFile=g:\Bodda\NetworkDiscoveryTool\NetworkDiscoveryTool.UI\Assets\app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "g:\Bodda\NetworkDiscoveryTool\publish\SupportToolKit.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "g:\Bodda\NetworkDiscoveryTool\publish\telegram.enc"; DestDir: "{app}"; Flags: ignoreversion
Source: "g:\Bodda\NetworkDiscoveryTool\NetworkDiscoveryTool.UI\Assets\app.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
