; Compile through build-installer.ps1: payload entries are explicitly generated.
#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef InstallerId
  #define InstallerId "YourselfCopilot.A72C22C0-AE30-4B53-9A11-B248A18D7937"
#endif
#ifndef ShortcutName
  #define ShortcutName "Yourself Copilot"
#endif
#ifndef ApplicationMutex
  #define ApplicationMutex "Local\LocalDesktopNote_01a14f34"
#endif
#ifndef StartupValueName
  #define StartupValueName "YourselfCopilot"
#endif

[Setup]
AppId={#InstallerId}
AppName=Yourself Copilot
AppVersion={#AppVersion}
AppPublisher=IsaacPu
AppPublisherURL=https://github.com/IsaacPu/Yourself-Copilot
AppSupportURL=https://github.com/IsaacPu/Yourself-Copilot/issues
AppUpdatesURL=https://github.com/IsaacPu/Yourself-Copilot/releases/latest
VersionInfoVersion={#AppVersion}.0
DefaultDirName={localappdata}\Programs\Yourself Copilot
DefaultGroupName={#ShortcutName}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
UsePreviousAppDir=yes
UsePreviousTasks=yes
DisableDirPage=no
DisableProgramGroupPage=yes
DisableWelcomePage=no
UninstallDisplayIcon={app}\Yourself Copilot.exe
UninstallDisplayName=Yourself Copilot
AppMutex={#ApplicationMutex}
CloseApplications=no
RestartApplications=no
SetupIconFile={#BrandIcon}
WizardStyle=modern
WizardSizePercent=115
WizardImageFile={#WizardImage}
WizardSmallImageFile={#WizardSmallImage}
WizardImageStretch=yes
WizardImageBackColor=$F3F7FA
WizardBackColor=$F3F7FA
OutputDir={#OutputPath}
OutputBaseFilename=Yourself-Copilot-Setup-{#AppVersion}-x64
Compression=lzma2
SolidCompression=yes
SetupLogging=yes
UninstallLogging=yes

[Languages]
Name: "zhcn"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[LangOptions]
DialogFontName=Microsoft YaHei UI
DialogFontSize=10
WelcomeFontName=Microsoft YaHei UI
WelcomeFontSize=20

[Messages]
WelcomeLabel1=让任务看得见，%n让想法有处落笔。
WelcomeLabel2=欢迎安装 Yourself Copilot {#AppVersion}。%n%n把待办、随手记和参考图片留在桌面上，随时记录，慢慢完成。%n%n所有内容保存在本机，无需账号。安装只对当前 Windows 用户生效。%n%n如果你正在使用旧版，请先从托盘退出程序。
SelectDirDesc=把它安放在哪里？
SelectDirLabel3=程序与个人内容保存在这个文件夹。升级时沿用原位置；卸载后个人内容仍会保留。
SelectTasksDesc=选择你需要的入口。
SelectTasksLabel2=开始菜单入口会自动创建。桌面快捷方式可选；登录自动运行可稍后在应用设置中开启。
ReadyLabel1=准备好了，开始安装。
ReadyLabel2a=请确认安装位置。安装只更新程序与资源，已有任务、随手记、图片和历史会保留。
FinishedHeadingLabel=已为你准备好。
FinishedLabel=Yourself Copilot 已安装完成。%n%n从一个任务，或一句随手记开始。关闭工作台后会收起到托盘；完整退出请使用托盘菜单。%n%n卸载可前往 Windows“已安装的应用”，个人内容仍保留在安装文件夹。
ConfirmUninstall=要卸载 Yourself Copilot 吗？%n%n程序和快捷方式会移除，任务、随手记、图片和历史将保留在原文件夹。
UninstalledAll=Yourself Copilot 已卸载。%n%n个人内容仍保留在原安装文件夹。重新安装到同一位置即可继续使用。
SetupAppRunningError=Yourself Copilot 仍在运行。%n%n请在托盘菜单中选择“退出 Yourself Copilot”，待内容保存完毕后重试。安装器不会强制关闭程序。
UninstallAppRunningError=Yourself Copilot 仍在运行。%n%n请先从托盘菜单完整退出，再继续卸载。

[Tasks]
Name: "desktopicon"; Description: "在桌面创建快捷方式"; Flags: unchecked

[Files]
#include FilesInclude

[Icons]
Name: "{autoprograms}\{#ShortcutName}"; Filename: "{app}\Yourself Copilot.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\{#ShortcutName}"; Filename: "{app}\Yourself Copilot.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Yourself Copilot.exe"; Description: "打开 Yourself Copilot"; Flags: nowait postinstall skipifsilent

[Code]
var
  InstallKind: TInputOptionWizardPage;
  ExistingFolder: TInputDirWizardPage;

function HasPreviousInstall: Boolean;
var
  PreviousDirectory: String;
begin
  Result := RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#InstallerId}_is1',
    'InstallLocation', PreviousDirectory);
end;

function HasFramework48: Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM32, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release);
  if Result then Result := Release >= 528040;
end;

function InitializeSetup: Boolean;
begin
  Result := HasFramework48;
  if not Result then
    SuppressibleMsgBox('需要先安装 Microsoft .NET Framework 4.8 或更高版本。' + #13#10#13#10 +
      '请通过 Windows 更新或微软官网安装，再重新打开此安装包。' + #13#10 +
      'https://dotnet.microsoft.com/download/dotnet-framework/net48', mbError, MB_OK, IDOK);
end;

procedure InitializeWizard;
begin
  InstallKind := CreateInputOptionPage(wpWelcome, '第一次见，还是接着用？',
    '选择适合你的安装方式。',
    '已有安装版会自动沿用原位置。如果以前用的是 ZIP 免安装版，可以直接在原文件夹升级。', True, False);
  InstallKind.Add('全新安装');
  InstallKind.Add('升级免安装版，保留原文件夹里的内容');
  InstallKind.SelectedValueIndex := 0;
  ExistingFolder := CreateInputDirPage(InstallKind.ID, '继续使用原来的内容',
    '选择旧版 Yourself Copilot 所在的文件夹。',
    '请选择包含 Yourself Copilot.exe 的文件夹。安装只更新程序与资源，不搬动或覆盖你的个人内容。建议先备份整个旧文件夹。',
    False, '');
  ExistingFolder.Add('旧版程序文件夹：');
  ExistingFolder.Values[0] := '';
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if PageID = InstallKind.ID then Result := HasPreviousInstall;
  if PageID = ExistingFolder.ID then
    Result := HasPreviousInstall or (InstallKind.SelectedValueIndex <> 1);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = ExistingFolder.ID then begin
    Result := FileExists(AddBackslash(ExistingFolder.Values[0]) + 'Yourself Copilot.exe');
    if not Result then
      MsgBox('这个文件夹里没有 Yourself Copilot.exe。请选择原来解压并运行程序的文件夹。', mbError, MB_OK)
    else
      WizardForm.DirEdit.Text := RemoveBackslashUnlessRoot(ExistingFolder.Values[0]);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  InstalledMS, InstalledLS, PackageMS, PackageLS: Cardinal;
  ProbeFile: String;
begin
  Result := '';
  ExtractTemporaryFile('VERSION.txt');
  ExtractTemporaryFile('Yourself Copilot.exe');
  if GetVersionNumbers(ExpandConstant('{app}\Yourself Copilot.exe'), InstalledMS, InstalledLS) and
     GetVersionNumbers(ExpandConstant('{tmp}\Yourself Copilot.exe'), PackageMS, PackageLS) then
    if (InstalledMS > PackageMS) or ((InstalledMS = PackageMS) and (InstalledLS > PackageLS)) then begin
      Result := '这里已安装更新版本。请使用最新安装包，避免旧程序读取新版本数据。';
      Exit;
    end;
  if not ForceDirectories(ExpandConstant('{app}')) then begin
    Result := '无法创建安装文件夹。请选择当前用户有写入权限的位置。';
    Exit;
  end;
  ProbeFile := AddBackslash(ExpandConstant('{app}')) + '{#InstallerId}.write-test';
  if not SaveStringToFile(ProbeFile, '', False) then
    Result := '此文件夹不可写。程序需要在这里保存个人内容，请选择有写入权限的位置。'
  else
    DeleteFile(ProbeFile);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  StartupCommand: String;
begin
  if CurUninstallStep = usPostUninstall then begin
    Log('Checking login startup value: {#StartupValueName}');
    if RegQueryStringValue(HKCU64, 'Software\Microsoft\Windows\CurrentVersion\Run', '{#StartupValueName}', StartupCommand) then begin
      if CompareText(StartupCommand, '"' + ExpandConstant('{app}\Yourself Copilot.exe') + '" --startup') = 0 then begin
        if RegDeleteValue(HKCU64, 'Software\Microsoft\Windows\CurrentVersion\Run', '{#StartupValueName}') then
          Log('Removed matching login startup entry.');
      end else Log('Preserved login startup entry pointing elsewhere.');
    end else Log('No application login startup entry.');
  end;
end;
