#define MyAppName "图吧工具箱winui3"
#define MyAppVersion "1.0.2"
#define MyAppPublisher "罗澜嘎嘎"
#define MyAppExeName "TubaWinUi3.exe"
#define MyAppCopyright "Copyright (C) 2025 罗澜嘎嘎"

[Setup]
AppId={{DA3D64F4-winui3-Tuba-2025}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://github.com/example/tubawinui3
AppSupportURL=https://github.com/example/tubawinui3
AppCopyright={#MyAppCopyright}
DefaultDirName={sd}\TubaWinUi3
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=
InfoBeforeFile=
InfoAfterFile=
OutputDir=SetupOutput
OutputBaseFilename=TubaWinUi3_Setup_{#MyAppVersion}
SetupIconFile=Assets\AppIcon.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible arm64
ArchitecturesInstallIn64BitMode=x64compatible arm64
LanguageDetectionMethod=locale
ShowLanguageDialog=no
UpdateUninstallLogAppName=yes
UsePreviousAppDir=yes
UsePreviousGroup=yes
UsePreviousSetupType=yes
UsePreviousTasks=yes
DirExistsWarning=no
AppendDefaultDirName=yes

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[Files]
Source: "publish_x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: IsX64
Source: "publish_arm64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: IsARM64

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
function IsX64: Boolean;
begin
  Result := Is64BitInstallMode and (ProcessorArchitecture = paX64);
end;

function IsARM64: Boolean;
begin
  Result := Is64BitInstallMode and (ProcessorArchitecture = paARM64);
end;