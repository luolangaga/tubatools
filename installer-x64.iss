#define MyAppName "图吧工具箱CE"
#define MyAppVersion "1.0.2"
#define MyAppPublisher "罗澜嘎嘎"
#define MyAppExeName "TubaWinUi3.exe"
#define MyAppCopyright "Copyright (C) 2025 罗澜嘎嘎"

[Setup]
AppId={{DA3D64F4-winui3-Tuba-2025}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}_x64
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://github.com/luolangaga/tubatool
AppSupportURL=https://github.com/luolangaga/tubatool
AppCopyright={#MyAppCopyright}
DefaultDirName={autopf}\TubaWinUi3
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=License.txt
OutputDir=SetupOutput
OutputBaseFilename=TubaWinUi3_Setup_{#MyAppVersion}_x64
SetupIconFile=TubaWinUi3.WinUI3\Assets\AppIcon.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName} (x64)
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
LanguageDetectionMethod=locale
ShowLanguageDialog=no
UpdateUninstallLogAppName=yes
UsePreviousAppDir=yes
UsePreviousGroup=yes
UsePreviousSetupType=yes
UsePreviousTasks=yes
DisableDirPage=no
DirExistsWarning=no
AppendDefaultDirName=yes

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Messages]
SetupAppTitle=安装 - {#MyAppName}
SetupWindowTitle=安装 - {#MyAppName}
WelcomeLabel2=此向导将引导您完成 [name/ver] 的安装过程。%n%n建议在继续之前关闭所有其他应用程序，以便安装程序更新相关的系统文件，无需重新启动计算机。
SelectDirBrowseLabel=如需安装到其他位置，请单击"浏览"选择目标文件夹。%n%n点击"安装"开始安装。
DiskSpaceWarning=至少需要 %1 KB 的可用空间才能安装，但所选驱动器只有 %2 KB 可用。%n%n是否仍要继续？
SelectStartMenuFolderBrowseLabel=如需选择其他文件夹，请单击"浏览"。%n%n点击"安装"开始安装。
ReadyLabel2a=单击"安装"开始安装，或单击"上一步"修改设置。
ReadyLabel2b=单击"安装"开始安装。
FinishedLabel=[name] 已成功安装到您的计算机中。
FinishedLabelNoIcons=[name] 已成功安装到您的计算机中。

ButtonBack=< 上一步(&B)
ButtonNext=下一步(&N) >
ButtonInstall=安装(&I)
ButtonFinish=完成(&F)
ButtonBrowse=浏览(&R)...
ButtonWizardBrowse=浏览(&R)...
ButtonNewFolder=新建文件夹(&M)

SelectLanguageTitle=选择安装语言
SelectLanguageLabel=选择安装过程中使用的语言：

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[Files]
Source: "publish_x64_installer\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: dirifempty; Name: "{app}"

[Code]
#include "installer-user-tools.iss"
var
  CustomPrevPath: String;

function InitializeSetup: Boolean;
begin
  Result := True;
  CustomPrevPath := '';
  if not RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{DA3D64F4-winui3-Tuba-2025}_is1',
    'InstallLocation', CustomPrevPath) then
    RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{DA3D64F4-winui3-Tuba-2025}_is1',
    'Inno Setup: App Path', CustomPrevPath);
end;

procedure CurWizardChanged(CurPageID: Integer);
begin
  if (CurPageID = wpSelectDir) and (CustomPrevPath <> '') then
  begin
    WizardForm.DirEdit.Text := CustomPrevPath;
    CustomPrevPath := '';
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    SaveStringToFile(ExpandConstant('{app}\.installed'), 'installed', False);
end;

function IsWindowsVersionOk: Boolean;
var
  Version: TWindowsVersion;
begin
  GetWindowsVersionEx(Version);
  // Windows 10 2004/20H1 = Build 19041 (与 MSIX 商店版门禁一致)
  Result := (Version.Major > 10) or
            ((Version.Major = 10) and (Version.Build >= 19041));
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Msg: String;
begin
  if not IsWindowsVersionOk then
  begin
    Msg := '本程序需要 Windows 10 2004 (Build 19041) 或更高版本。' + #13#10 +
           '您当前的系统版本过低，无法运行本程序。' + #13#10#13#10 +
           '请先更新 Windows 系统后再安装。';
    MsgBox(Msg, mbCriticalError, MB_OK);
    Result := '系统版本不满足要求，安装已取消。';
    Exit;
  end;

  Result := TubaBackupLegacyToolMetadata;
end;

// === 卸载：询问是否删除用户数据（默认保留；静默卸载自动取默认"否"） ===
var
  UninstallDeleteData: Boolean;
  UninstallCustomDataDir: String;

function IsUninstallAbsolutePath(const S: String): Boolean;
begin
  // 仅把 "X:\..." 视为安装目录外的绝对路径；相对路径都在 {app} 内，会随程序一起删除
  Result := (Length(S) >= 3) and (S[2] = ':') and (S[3] = '\');
end;

procedure TubaAskDeleteUserData;
var
  MarkerPath: String;
  MarkerContent: AnsiString;
  CustomPos: Integer;
begin
  UninstallDeleteData := False;
  UninstallCustomDataDir := '';

  if DirExists(ExpandConstant('{app}\Data')) or
     DirExists(ExpandConstant('{localappdata}\TubaWinUi3')) or
     FileExists(ExpandConstant('{app}\Data\.config_location')) then
  begin
    if SuppressibleMsgBox('是否同时删除本软件的用户数据？' + #13#10 + #13#10 +
              '删除内容包括：' + #13#10 +
              '· 设置与配置' + #13#10 +
              '· 收藏、自定义与社区工具（含文件）' + #13#10 +
              '· AI 助手聊天记录与记忆' + #13#10 +
              '· 图标缓存、WebView2 缓存等' + #13#10 + #13#10 +
              '默认数据位于 ' + ExpandConstant('{localappdata}\TubaWinUi3') + '；' + #13#10 +
              '若您曾把数据迁移到自定义目录，对应目录也会一并删除。' + #13#10 +
              '此操作不可恢复。' + #13#10 + #13#10 +
              '选择"是"删除用户数据，选择"否"仅卸载程序、保留数据。',
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
    begin
      UninstallDeleteData := True;

      // 数据位置标记：AppRoot=安装目录内（仅同意删除用户数据时清理）；Custom:=外部自定义目录
      MarkerPath := ExpandConstant('{app}\Data\.config_location');
      if FileExists(MarkerPath) and LoadStringFromFile(MarkerPath, MarkerContent) then
      begin
        MarkerContent := Trim(Utf8Decode(MarkerContent));
        CustomPos := Pos('Custom:', MarkerContent);
        if CustomPos > 0 then
        begin
          UninstallCustomDataDir := Copy(MarkerContent, CustomPos + 7, MaxInt);
          if not IsUninstallAbsolutePath(UninstallCustomDataDir) then
            UninstallCustomDataDir := ''; // 相对路径位于 {app} 内，无需单独删除
        end;
      end;
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    TubaAskDeleteUserData
  else if CurUninstallStep = usPostUninstall then
  begin
    if UninstallDeleteData then
    begin
      DelTree(ExpandConstant('{localappdata}\TubaWinUi3'), True, True, True);
      DelTree(ExpandConstant('{app}\Data'), True, True, True);
      DelTree(ExpandConstant('{app}\Tools'), True, True, True);
      if (UninstallCustomDataDir <> '') and
         (LowerCase(UninstallCustomDataDir) <>
          LowerCase(ExpandConstant('{localappdata}\TubaWinUi3'))) then
        DelTree(UninstallCustomDataDir, True, True, True);
    end;
  end;
end;
