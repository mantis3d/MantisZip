; MantisZip Installer Script
; Requires Inno Setup 6

#define MyAppName "MantisZip"
#ifndef MyAppVersion
#define MyAppVersion "0.5.4"
#endif
#define MyAppPublisher "MantisZip Contributors"
#define MyAppURL "https://github.com/mantis3d/MantisZip"
#define MyAppExeName "MantisZip.UI.Avalonia.exe"

[Setup]
; ONE product, two packagings (issue #45): both installer scripts share this AppId, so
; installing one variant removes the other instead of writing on top of it.
AppId={{F7A3C8E1-2D4B-4A9F-9C6E-8B5D7A3F2E1C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=LICENSE
OutputDir=installer
OutputBaseFilename=MantisZip-{#MyAppVersion}-Setup-WebSetup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
ChangesEnvironment=yes
CloseApplications=yes
; Prevent two setup instances (either variant) from writing {app} at the same time.
SetupMutex=MantisZipSetupMutex
SetupIconFile=src\MantisZip.UI.Avalonia\Resources\App.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinese"; MessagesFile: "setup\Languages\ChineseSimplified.isl"

[CustomMessages]
; English
english.ConfigPageTitle=Installation Options
english.ConfigDesc=Choose your preferred appearance and system integration settings
english.ThemeGroup=Appearance
english.ThemeLight=Light theme
english.ThemeDark=Dark theme
english.ShellGroup=System Integration
english.InstallShell=Add to Windows context menu
english.AssocGroup=File type associations

; Download confirmation dialog (shown when dependencies are missing)
english.DownloadConfirmMsg=MantisZip requires .NET 10 Runtime, which is not installed on your system.%n%nDo you want to download and install it automatically?%n%n• Yes — Download dependency and continue setup%n• No — Skip download and continue setup without the dependency%n• Cancel — Exit the installer

; Download page (shown after user confirms download)
english.DownloadPageCaption=Download required component
english.DownloadPageDescription=MantisZip requires .NET 10 Runtime to run. It will be downloaded automatically.

; Second confirmation when skipping the runtime (issue #45: "installed but won't start")
english.SkipRuntimeConfirmMsg=If you skip the .NET 10 Runtime, MantisZip will not start until you install it yourself.%n%nTip: the Offline installer (Setup-Offline.exe) already includes the runtime.%n%nContinue without the runtime?
; Legacy migration bridge (the old self-contained installer used a different product identity)
english.LegacyAppRunningMsg=MantisZip is running.%n%nSetup must close it before removing the previous installation. Close it now and continue?
english.LegacyUninstallFailedMsg=The previous MantisZip installation could not be removed automatically.%n%nPlease uninstall "MantisZip" from Settings → Apps, then run this installer again.
; Uninstall: optional removal of per-user data
english.RemoveUserSettingsMsg=Also delete your personal MantisZip settings and recent-file history?%n%nChoose No to keep them (useful if you plan to reinstall).

; Chinese (Simplified)
chinese.ConfigPageTitle=安装配置
chinese.ConfigDesc=选择偏好的外观和系统集成设置
chinese.ThemeGroup=外观
chinese.ThemeLight=浅色主题
chinese.ThemeDark=深色主题
chinese.ShellGroup=系统集成
chinese.InstallShell=添加到 Windows 右键菜单
chinese.AssocGroup=文件关联

; Download confirmation dialog (shown when dependencies are missing)
chinese.DownloadConfirmMsg=MantisZip 需要 .NET 10 运行时，但您的系统尚未安装。%n%n是否要自动下载并安装该依赖？%n%n• 是 — 下载依赖并继续安装%n• 否 — 跳过下载，继续安装（不安装依赖）%n• 取消 — 退出安装程序

; Download page (shown after user confirms download)
chinese.DownloadPageCaption=正在下载必要组件
chinese.DownloadPageDescription=MantisZip 需要 .NET 10 运行时才能运行。正在自动下载中。

; 跳过运行时的二次确认（issue #45：避免“装成功但打不开”）
chinese.SkipRuntimeConfirmMsg=如果跳过 .NET 10 运行时，MantisZip 在你手动安装运行时之前将无法启动。%n%n提示：离线安装包（Setup-Offline.exe）已自带运行时。%n%n确定不安装运行时继续吗？
; 旧版产品身份迁移桥（旧的自包含安装包用了另一个产品身份）
chinese.LegacyAppRunningMsg=MantisZip 正在运行。%n%n安装程序需要先关闭它，才能移除旧版本。现在关闭并继续吗？
chinese.LegacyUninstallFailedMsg=无法自动移除上一版 MantisZip。%n%n请先从「设置 → 应用」中卸载 MantisZip，然后重新运行本安装程序。
; 卸载：是否同时删除个人数据
chinese.RemoveUserSettingsMsg=是否同时删除 MantisZip 的个人设置与最近文件记录？%n%n选择「否」将保留它们（以后重装可继续使用）。

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; === All DLLs (wildcard — automatically includes new dependencies) ===
Source: "publish_output\*.dll"; DestDir: "{app}"; Flags: ignoreversion

; === Executables ===
Source: "publish_output\MantisZip.UI.Avalonia.exe"; DestDir: "{app}"; Flags: ignoreversion

; === Runtime config (required for .NET assembly resolution) ===
Source: "publish_output\MantisZip.UI.Avalonia.deps.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish_output\MantisZip.UI.Avalonia.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion

; === ShellExt COM host (dynamic context menu) — runtimeconfig.json required for COM activation ===
Source: "publish_output\MantisZip.ShellExt.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion

; === 7z.dll (SharpSevenZip): x64 only (app is win-x64) ===
Source: "publish_output\x64\7z.dll"; DestDir: "{app}\x64"; Flags: ignoreversion

; === Resources (file type icons, context menu icons, drag cursors, localization) ===
Source: "publish_output\Resources\Icons\*.ico"; DestDir: "{app}\Resources\Icons"; Flags: ignoreversion
Source: "publish_output\Resources\MenuIcons\*.ico"; DestDir: "{app}\Resources\MenuIcons"; Flags: ignoreversion
Source: "publish_output\Resources\Cursors\*.cur"; DestDir: "{app}\Resources\Cursors"; Flags: ignoreversion
Source: "publish_output\Localization\strings.en.json"; DestDir: "{app}\Localization"; Flags: ignoreversion
Source: "publish_output\Localization\strings.zh-CN.json"; DestDir: "{app}\Localization"; Flags: ignoreversion
Source: "publish_output\Localization\strings.zh-TW.json"; DestDir: "{app}\Localization"; Flags: ignoreversion
Source: "publish_output\Resources\languages.json"; DestDir: "{app}\Resources"; Flags: ignoreversion

; === Contributor CSV files (generated by GitHub Actions) ===
Source: "publish_output\contributors-technical.csv"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish_output\contributors-financial.csv"; DestDir: "{app}"; Flags: ignoreversion

; === License (7z.dll is distributed under GNU Lesser General Public License) ===
Source: "lgpl.txt"; DestDir: "{app}"; Flags: ignoreversion

; === Prebuilt user settings (copied to %LOCALAPPDATA% on fresh install) ===
; Replace files in installer\prebuilt\ with your own settings from %LOCALAPPDATA%\MantisZip\
Source: "installer\prebuilt\settings.json"; DestDir: "{app}\prebuilt"; Flags: ignoreversion
Source: "installer\prebuilt\window.json"; DestDir: "{app}\prebuilt"; Flags: ignoreversion

[InstallDelete]
; === Cross-variant leftovers (issue #45) ===
; Installing this framework-dependent build over a previous self-contained (Offline)
; install leaves the bundled .NET runtime files behind, so {app} ends up with a
; runtimeconfig.json that no longer matches the files on disk — apphost then shows
; "You must install or update .NET to run this application." Only this known list is
; removed; user-created files are never touched.
Type: files; Name: "{app}\hostfxr.dll"
Type: files; Name: "{app}\hostpolicy.dll"
Type: files; Name: "{app}\coreclr.dll"
Type: files; Name: "{app}\clrjit.dll"
Type: files; Name: "{app}\mscordaccore.dll"
Type: files; Name: "{app}\mscordaccore_amd64_amd64_*.dll"
Type: files; Name: "{app}\mscordbi.dll"
Type: files; Name: "{app}\System.Private.CoreLib.dll"
Type: files; Name: "{app}\createdump.exe"
; Stale uninstaller data from a previous product identity (now unified)
Type: files; Name: "{app}\unins*.exe"
Type: files; Name: "{app}\unins*.dat"

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent; WorkingDir: "{app}"

[UninstallRun]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--uninstall-shell"; Flags: runhidden; WorkingDir: "{app}"; RunOnceId: "MnzUninstallShell"
Filename: "{app}\{#MyAppExeName}"; Parameters: "--uninstall-assoc"; Flags: runhidden; WorkingDir: "{app}"; RunOnceId: "MnzUninstallAssoc"

[Code]
const
  DotNet10RegKey = 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.NETCore.App';
  DotNet10RuntimeUrl = 'https://aka.ms/dotnet/10.0/dotnet-runtime-win-x64.exe';
  // Product identity — must stay identical to installer-selfcontained.iss so Windows
  // treats both packagings as ONE product (issue #45).
  MyAppId = '{F7A3C8E1-2D4B-4A9F-9C6E-8B5D7A3F2E1C}';
  // Identity used by the old self-contained installer. Kept only so the migration
  // bridge below can find and remove that leftover install.
  LegacySelfContainedKey = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{963001F3-5748-4834-9489-97D6D00E3917}_is1';

var
  // Custom wizard page controls
  WPConfigPage: TWizardPage;
  ThemeLightRadio: TNewRadioButton;
  ThemeDarkRadio: TNewRadioButton;
  InstallShellCheck: TNewCheckBox;
  // Per-format association checkboxes
  AssocCheckZip: TNewCheckBox;
  AssocCheck7z: TNewCheckBox;
  AssocCheckRar: TNewCheckBox;
  AssocCheckTar: TNewCheckBox;
  AssocCheckTarGz: TNewCheckBox;
  AssocCheckGz: TNewCheckBox;
  AssocCheckIso: TNewCheckBox;
  // Download progress page
  DownloadPage: TDownloadWizardPage;

// Import Sleep from kernel32 for post-install delay (let .NET registration settle)
procedure Sleep(Milliseconds: DWord); external 'Sleep@kernel32.dll stdcall';

// Create the custom configuration wizard page (theme + system integration)
procedure CreateConfigPage;
var
  ThemeGroupLabel: TNewStaticText;
  ShellGroupLabel: TNewStaticText;
  AssocGroupLabel: TNewStaticText;
  RowTop: Integer;
  RowTop2: Integer;
begin
  WPConfigPage := CreateCustomPage(wpLicense,
    CustomMessage('ConfigPageTitle'),
    CustomMessage('ConfigDesc'));

  // --- Appearance section ---
  ThemeGroupLabel := TNewStaticText.Create(WPConfigPage);
  ThemeGroupLabel.Parent := WPConfigPage.Surface;
  ThemeGroupLabel.Caption := CustomMessage('ThemeGroup');
  ThemeGroupLabel.Font.Style := [fsBold];
  ThemeGroupLabel.Top := 8;
  ThemeGroupLabel.Left := 0;

  ThemeLightRadio := TNewRadioButton.Create(WPConfigPage);
  ThemeLightRadio.Parent := WPConfigPage.Surface;
  ThemeLightRadio.Caption := CustomMessage('ThemeLight');
  ThemeLightRadio.Top := ThemeGroupLabel.Top + ScaleY(20);
  ThemeLightRadio.Left := 16;
  ThemeLightRadio.Checked := True;

  ThemeDarkRadio := TNewRadioButton.Create(WPConfigPage);
  ThemeDarkRadio.Parent := WPConfigPage.Surface;
  ThemeDarkRadio.Caption := CustomMessage('ThemeDark');
  ThemeDarkRadio.Top := ThemeLightRadio.Top + ScaleY(24);
  ThemeDarkRadio.Left := 16;

  // --- System Integration section ---
  ShellGroupLabel := TNewStaticText.Create(WPConfigPage);
  ShellGroupLabel.Parent := WPConfigPage.Surface;
  ShellGroupLabel.Caption := CustomMessage('ShellGroup');
  ShellGroupLabel.Font.Style := [fsBold];
  ShellGroupLabel.Top := ThemeDarkRadio.Top + ScaleY(28);
  ShellGroupLabel.Left := 0;

  InstallShellCheck := TNewCheckBox.Create(WPConfigPage);
  InstallShellCheck.Parent := WPConfigPage.Surface;
  InstallShellCheck.Caption := CustomMessage('InstallShell');
  InstallShellCheck.Top := ShellGroupLabel.Top + ScaleY(20);
  InstallShellCheck.Left := 16;
  InstallShellCheck.Width := WPConfigPage.SurfaceWidth - ScaleX(32);
  InstallShellCheck.Checked := True;

  // --- File type associations (per-format checkboxes) ---
  AssocGroupLabel := TNewStaticText.Create(WPConfigPage);
  AssocGroupLabel.Parent := WPConfigPage.Surface;
  AssocGroupLabel.Caption := CustomMessage('AssocGroup');
  AssocGroupLabel.Font.Style := [fsBold];
  AssocGroupLabel.Top := InstallShellCheck.Top + ScaleY(28);
  AssocGroupLabel.Left := 0;

  RowTop := AssocGroupLabel.Top + ScaleY(20);

  AssocCheckZip := TNewCheckBox.Create(WPConfigPage);
  AssocCheckZip.Parent := WPConfigPage.Surface;
  AssocCheckZip.Caption := '.zip';
  AssocCheckZip.Top := RowTop;
  AssocCheckZip.Left := 16;
  AssocCheckZip.Width := ScaleX(64);
  AssocCheckZip.Checked := True;

  AssocCheck7z := TNewCheckBox.Create(WPConfigPage);
  AssocCheck7z.Parent := WPConfigPage.Surface;
  AssocCheck7z.Caption := '.7z';
  AssocCheck7z.Top := RowTop;
  AssocCheck7z.Left := ScaleX(96);
  AssocCheck7z.Width := ScaleX(64);
  AssocCheck7z.Checked := True;

  AssocCheckRar := TNewCheckBox.Create(WPConfigPage);
  AssocCheckRar.Parent := WPConfigPage.Surface;
  AssocCheckRar.Caption := '.rar';
  AssocCheckRar.Top := RowTop;
  AssocCheckRar.Left := ScaleX(176);
  AssocCheckRar.Width := ScaleX(64);
  AssocCheckRar.Checked := True;

  AssocCheckTar := TNewCheckBox.Create(WPConfigPage);
  AssocCheckTar.Parent := WPConfigPage.Surface;
  AssocCheckTar.Caption := '.tar';
  AssocCheckTar.Top := RowTop;
  AssocCheckTar.Left := ScaleX(256);
  AssocCheckTar.Width := ScaleX(64);
  AssocCheckTar.Checked := True;

  // Row 2
  RowTop2 := RowTop + ScaleY(24);

  AssocCheckTarGz := TNewCheckBox.Create(WPConfigPage);
  AssocCheckTarGz.Parent := WPConfigPage.Surface;
  AssocCheckTarGz.Caption := '.tar.gz';
  AssocCheckTarGz.Top := RowTop2;
  AssocCheckTarGz.Left := 16;
  AssocCheckTarGz.Width := ScaleX(80);
  AssocCheckTarGz.Checked := True;

  AssocCheckGz := TNewCheckBox.Create(WPConfigPage);
  AssocCheckGz.Parent := WPConfigPage.Surface;
  AssocCheckGz.Caption := '.gz';
  AssocCheckGz.Top := RowTop2;
  AssocCheckGz.Left := ScaleX(112);
  AssocCheckGz.Width := ScaleX(64);
  AssocCheckGz.Checked := True;

  AssocCheckIso := TNewCheckBox.Create(WPConfigPage);
  AssocCheckIso.Parent := WPConfigPage.Surface;
  AssocCheckIso.Caption := '.iso';
  AssocCheckIso.Top := RowTop2;
  AssocCheckIso.Left := ScaleX(192);
  AssocCheckIso.Width := ScaleX(64);
  AssocCheckIso.Checked := True;
end;

// Map Inno Setup language code to MantisZip app language code
function GetAppLanguageCode: string;
var
  lang: string;
begin
  lang := ExpandConstant('{language}');
  if lang = 'english' then
    Result := 'en'
  else if lang = 'chinese' then
    Result := 'zh'
  else
    Result := 'en';
end;

// Get selected theme value from custom page
function GetSelectedTheme: string;
begin
  if ThemeDarkRadio.Checked then
    Result := 'Dark'
  else
    Result := 'Light';
end;

// Check functions for conditional [Run] entries
function IsShellInstallChecked: Boolean;
begin
  Result := InstallShellCheck.Checked;
end;

// Returns true if at least one format checkbox is checked
function IsAnyAssocChecked: Boolean;
begin
  Result := AssocCheckZip.Checked or AssocCheck7z.Checked or AssocCheckRar.Checked
         or AssocCheckTar.Checked or AssocCheckTarGz.Checked or AssocCheckGz.Checked
         or AssocCheckIso.Checked;
end;

// Builds comma-separated list of checked extensions for the --install-assoc parameter
function GetAssocParams(Param: string): string;
var
  parts: TStringList;
begin
  parts := TStringList.Create;
  try
    if AssocCheckZip.Checked then parts.Add('.zip');
    if AssocCheck7z.Checked then parts.Add('.7z');
    if AssocCheckRar.Checked then parts.Add('.rar');
    if AssocCheckTar.Checked then parts.Add('.tar');
    if AssocCheckTarGz.Checked then parts.Add('.tar.gz');
    if AssocCheckGz.Checked then parts.Add('.gz');
    if AssocCheckIso.Checked then parts.Add('.iso');
    Result := parts.CommaText;
  finally
    parts.Free;
  end;
end;

// Replace placeholder tokens in the copied settings.json with actual wizard selections.
procedure PatchSettingsThemeAndLanguage(const FileName: String);
var
  Content: AnsiString;
  ContentStr: String;
begin
  if LoadStringFromFile(FileName, Content) then
  begin
    ContentStr := Content;
    StringChange(ContentStr, '__LANG__', GetAppLanguageCode);
    StringChange(ContentStr, '__THEME__', GetSelectedTheme);
    Content := ContentStr;
    SaveStringToFile(FileName, Content, False);
    Log('Settings patched with wizard theme/language selection.');
  end
  else
    Log('Failed to load settings.json for theme/language patching.');
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(
    CustomMessage('DownloadPageCaption'),
    CustomMessage('DownloadPageDescription'),
    nil);
  CreateConfigPage;
end;

// Check if .NET 10 Runtime is already installed.
// Checks both registry (subkey-based) and filesystem (runtime directory).
// NOTE: .NET 10 stores version info as registry *value names* (DWORD) rather
// than subkeys, so RegGetSubkeyNames is unreliable. We fall back to checking
// whether a "10.*" subdirectory exists under the dotnet shared runtime folder.
function IsDotNet10Installed: Boolean;
var
  subkeys: TArrayOfString;
  i: Integer;
  TmpFile: String;
  Content: AnsiString;
  ResultCode: Integer;
begin
  Result := False;

  // Check 64-bit registry view first (native HKLM64)
  if RegGetSubkeyNames(HKLM64, DotNet10RegKey, subkeys) then
  begin
    for i := 0 to GetArrayLength(subkeys) - 1 do
    begin
      if Copy(subkeys[i], 1, 3) = '10.' then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;

  // Fallback: check 32-bit view (WOW6432Node)
  if not Result then
  begin
    if RegGetSubkeyNames(HKLM, DotNet10RegKey, subkeys) then
    begin
      for i := 0 to GetArrayLength(subkeys) - 1 do
      begin
        if Copy(subkeys[i], 1, 3) = '10.' then
        begin
          Result := True;
          Exit;
        end;
      end;
    end;
  end;

  // Final fallback: check filesystem for a 10.x runtime directory.
  // .NET 10 stores version as value names (DWORD) under the sharedfx key,
  // which RegGetSubkeyNames cannot enumerate. The runtime DLLs are always
  // present on disk under {commonpf64}\dotnet\shared\Microsoft.NETCore.App\10.*\.
  if not Result then
  begin
    TmpFile := ExpandConstant('{tmp}\dotnet_ver_check.txt');
    if Exec(ExpandConstant('{cmd}'), '/c dir /b /ad "' +
        ExpandConstant('{commonpf64}') + '\dotnet\shared\Microsoft.NETCore.App\10.*" 2>nul > "' +
        TmpFile + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0) then
    begin
      if LoadStringFromFile(TmpFile, Content) and (Trim(Content) <> '') then
        Result := True;
    end;
  end;
end;

// ============================================================================
// Legacy product identity migration (issue #45)
// Before v0.5.4 the self-contained (Offline) installer declared its own AppId, so
// Windows treated it as a separate product: installing the other variant did not
// remove it, and both ended up sharing {app} — two uninstall logs for the same files,
// which then deleted each other's runtimeconfig.json / runtime DLLs. This bridge
// removes such a leftover installation BEFORE any file is written, so the new install
// always starts from a clean folder. It is idempotent: with no legacy key present it
// does nothing.
// ============================================================================

function LegacyKeyExists: Boolean;
begin
  Result := RegKeyExists(HKLM64, LegacySelfContainedKey)
         or RegKeyExists(HKLM32, LegacySelfContainedKey)
         or RegKeyExists(HKCU, LegacySelfContainedKey);
end;

function ReadLegacyUninstallString: String;
begin
  if RegQueryStringValue(HKLM64, LegacySelfContainedKey, 'UninstallString', Result) then
    Exit;
  if RegQueryStringValue(HKLM32, LegacySelfContainedKey, 'UninstallString', Result) then
    Exit;
  if not RegQueryStringValue(HKCU, LegacySelfContainedKey, 'UninstallString', Result) then
    Result := '';
end;

// True when the MantisZip UI executable is currently running. Relies on find.exe's exit
// code (0 = match) so no temp file or text decoding is involved.
function IsMantisZipRunning: Boolean;
var
  ResultCode: Integer;
begin
  Result := False;
  if Exec(ExpandConstant('{cmd}'),
          '/c tasklist /FI "IMAGENAME eq MantisZip.UI.Avalonia.exe" /NH | find /I "MantisZip.UI.Avalonia.exe" >nul',
          '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Result := (ResultCode = 0);
end;

procedure CloseMantisZip;
var
  ResultCode: Integer;
begin
  Log('Closing running MantisZip instance before migrating the old installation.');
  Exec(ExpandConstant('{cmd}'), '/c taskkill /IM MantisZip.UI.Avalonia.exe /T /F',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(800);
end;

// Splits an UninstallString such as  "C:\Program Files\MantisZip\unins000.exe" /foo
// into the executable path and its parameters.
procedure SplitLegacyCommandLine(const CommandLine: String; var ExeName, Params: String);
var
  S: String;
  P: Integer;
begin
  ExeName := '';
  Params := '';
  S := Trim(CommandLine);
  if S = '' then
    Exit;
  if S[1] = '"' then
  begin
    Delete(S, 1, 1);
    P := Pos('"', S);
    if P > 0 then
    begin
      ExeName := Copy(S, 1, P - 1);
      Params := Trim(Copy(S, P + 1, Length(S) - P));
    end
    else
      ExeName := S;
  end
  else
  begin
    P := Pos(' ', S);
    if P > 0 then
    begin
      ExeName := Copy(S, 1, P - 1);
      Params := Trim(Copy(S, P + 1, Length(S) - P));
    end
    else
      ExeName := S;
  end;
end;

// Inno's uninstaller may relaunch itself under a temporary name and return at once,
// so its exit code proves nothing. Wait until the uninstall registry key is gone.
function WaitForLegacyKeyGone(TimeoutMs: Integer): Boolean;
var
  Waited: Integer;
begin
  Waited := 0;
  while (Waited < TimeoutMs) and LegacyKeyExists do
  begin
    Sleep(500);
    Waited := Waited + 500;
  end;
  Result := not LegacyKeyExists;
end;

function RemoveLegacyInstallation(var ErrorMessage: String): Boolean;
var
  UninstallString: String;
  ExeName: String;
  Params: String;
  ResultCode: Integer;
begin
  Result := True;
  ErrorMessage := '';

  if not LegacyKeyExists then
  begin
    Log('No legacy MantisZip identity found; nothing to migrate.');
    Exit;
  end;

  Log('Legacy MantisZip identity found: ' + LegacySelfContainedKey);

  // Files held open by a running app cannot be deleted, and Inno would then schedule
  // them for deletion at the next reboot — which would delete the NEW files we are
  // about to write. So the app has to be closed first.
  if IsMantisZipRunning then
  begin
    if MsgBox(CustomMessage('LegacyAppRunningMsg'), mbConfirmation, MB_YESNO) <> IDYES then
    begin
      Result := False;
      ErrorMessage := CustomMessage('LegacyUninstallFailedMsg');
      Exit;
    end;
    CloseMantisZip;
  end;

  UninstallString := ReadLegacyUninstallString;
  SplitLegacyCommandLine(UninstallString, ExeName, Params);
  Log('Legacy uninstall command: ' + UninstallString);

  if ExeName = '' then
    Log('Legacy entry has no UninstallString; assuming its files are already gone.')
  else if not FileExists(ExeName) then
    Log('Legacy uninstaller no longer exists (' + ExeName + '); assuming already gone.')
  else
  begin
    Params := Trim(Params + ' /VERYSILENT /SUPPRESSMSGBOXES /NORESTART');
    Log('Running legacy uninstaller: ' + ExeName + ' ' + Params);
    if not Exec(ExeName, Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    begin
      Log('Failed to launch the legacy uninstaller.');
      Result := False;
      ErrorMessage := CustomMessage('LegacyUninstallFailedMsg');
      Exit;
    end;
    Log('Legacy uninstaller exited with code: ' + IntToStr(ResultCode));
  end;

  if not WaitForLegacyKeyGone(60000) then
  begin
    Log('Legacy uninstall registry key is still present; aborting installation.');
    Result := False;
    ErrorMessage := CustomMessage('LegacyUninstallFailedMsg');
    Exit;
  end;

  // Stale first-run markers would override the choices made in this wizard.
  RegDeleteValue(HKCU, 'Software\MantisZip', 'FirstRunShell');
  RegDeleteValue(HKCU, 'Software\MantisZip', 'FirstRunAssoc');
  Log('Legacy identity migrated successfully.');
end;

// Runs before any file is copied: old product removed first, new files written second.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Err: String;
begin
  Result := '';
  NeedsRestart := False;
  if not RemoveLegacyInstallation(Err) then
    Result := Err;
end;

// Intercept the "Ready to Install" page — download prerequisites before installation begins.
// The download happens AFTER the user clicks Install but BEFORE file copying.
function NextButtonClick(CurPageID: Integer): Boolean;
var
  NeedDownload: Boolean;
begin
  if CurPageID = wpReady then
  begin
    NeedDownload := False;
    DownloadPage.Clear;
    if not IsDotNet10Installed then
    begin
      DownloadPage.Add(DotNet10RuntimeUrl, 'dotnet-runtime-10.0-win-x64.exe', '');
      NeedDownload := True;
    end;

    // Only show the download page if there are items to download.
    if NeedDownload then
    begin
      case MsgBox(CustomMessage('DownloadConfirmMsg'), mbConfirmation, MB_YESNOCANCEL) of
        IDYES: begin
          // User chose to download dependency — show download page
          DownloadPage.Show;
          try
            try
              DownloadPage.Download;
              Result := True;
            except
              Result := False;
            end;
          finally
            DownloadPage.Hide;
          end;
        end;
        IDNO: begin
          // User chose to skip the download. Ask once more, because the resulting state
          // ("installed but cannot start") is exactly what issue #45 was about.
          if MsgBox(CustomMessage('SkipRuntimeConfirmMsg'), mbConfirmation,
                    MB_YESNO or MB_DEFBUTTON2) = IDYES then
          begin
            Log('User confirmed skipping the dependency download.');
            Result := True;
          end
          else
          begin
            Log('User went back after the skip warning; returning to the Ready page.');
            Result := False;
          end;
        end;
        else begin
          // IDCANCEL or any other result — exit installer
          Result := False;
        end;
      end;
    end else
      Result := True;
  end else
    Result := True;
end;

// Install runtime (already downloaded via DownloadPage)
// Write installer settings to AppData after install completes
procedure CurStepChanged(CurStep: TSetupStep);
var
  BootstrapperPath: string;
  ResultCode: Integer;
  Json: string;
  SettingsDir: string;
  SettingsFile: string;
  WindowFile: string;
  ManifestPath: string;
  ManifestJson: string;
  InstalledAt: string;
  i: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    // 1. .NET 10 Runtime (more critical — app won't start without it)
    // File was already downloaded via DownloadPage before installation began.
    if not IsDotNet10Installed then
    begin
      BootstrapperPath := ExpandConstant('{tmp}\dotnet-runtime-10.0-win-x64.exe');
      if FileExists(BootstrapperPath) then
      begin
        Log('Installing .NET 10 Runtime...');
        if Exec(BootstrapperPath, '/quiet /install /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
        begin
          if ResultCode = 0 then
          begin
            Log('.NET 10 Runtime installed successfully.');
            // Wait for .NET registry registration to complete before running
            // shell/assoc registration (which also launch MantisZip.exe).
            // The installer process exits before the registry is fully synced.
            i := 0;
            while (i < 60) and (not IsDotNet10Installed) do
            begin
              Sleep(500);
              i := i + 1;
            end;
            if IsDotNet10Installed then
              Log('.NET registration confirmed after ~' + IntToStr(i * 500) + 'ms')
            else
              Log('.NET registration not detected after ~30s, proceeding anyway');
          end
          else
            Log('.NET 10 Runtime installer exited with code: ' + IntToStr(ResultCode));
        end
        else
          Log('Failed to launch .NET 10 bootstrapper.');
      end
      else
        Log('.NET 10 bootstrapper not found. Download may have failed; user may need to install manually.');
    end
    else
      Log('.NET 10 Runtime is already installed.');

    // 2. Shell integration deferred to first user launch (non-elevated context).
    //    SHChangeNotify from an elevated (installer) process does NOT propagate
    //    to the non-elevated Explorer.exe, so dynamic COM context menus appear
    //    missing until reinstalled from MantisZip's Settings window.
    //    Instead: write a FirstRun marker. MantisZip's App.OnStartup detects it
    //    on the first normal (non-elevated) launch and calls ShellIntegration.Install()
    //    — which includes SHChangeNotify from the correct integrity level.
    if InstallShellCheck.Checked then
    begin
      RegWriteStringValue(HKCU, 'Software\MantisZip', 'FirstRunShell', '1');
      Log('FirstRunShell marker written (shell integration will install on first user launch)');
    end;
    if IsAnyAssocChecked then
    begin
      RegWriteStringValue(HKCU, 'Software\MantisZip', 'FirstRunAssoc', '1');
      Log('FirstRunAssoc marker written (file associations will register on first user launch)');
    end;

    SettingsDir := ExpandConstant('{localappdata}\MantisZip');
    SettingsFile := SettingsDir + '\settings.json';
    WindowFile := SettingsDir + '\window.json';

    // Only write on fresh install — don't overwrite existing user settings on upgrade
    if not FileExists(SettingsFile) then
    begin
      Log('Writing prebuilt settings to: ' + SettingsDir);
      if not DirExists(SettingsDir) then
        CreateDir(SettingsDir);

      // Copy prebuilt settings.json (including Language + Theme from wizard)
      // Users can replace installer\prebuilt\ with their own files before building the installer
      if CopyFile(ExpandConstant('{app}\prebuilt\settings.json'), SettingsFile, False) then
      begin
        Log('Prebuilt settings.json copied.')
        // Override placeholder tokens with actual wizard selections
        PatchSettingsThemeAndLanguage(SettingsFile);
      end
      else
      begin
        Log('Failed to copy prebuilt settings.json, writing minimal config...');
        // Fallback: write minimal settings (Language + Theme from wizard)
        Json := '{' +
          '"Language": "' + GetAppLanguageCode + '",' +
          '"Theme": "' + GetSelectedTheme + '"' +
          '}';
        SaveStringToFile(SettingsFile, Json, False);
      end;

      // Copy prebuilt window.json if it exists
      if FileExists(ExpandConstant('{app}\prebuilt\window.json')) then
      begin
        if CopyFile(ExpandConstant('{app}\prebuilt\window.json'), WindowFile, False) then
          Log('Prebuilt window.json copied.')
        else
          Log('Failed to copy prebuilt window.json.');
      end;
    end
    else
      Log('Settings file already exists, preserving existing user settings.');

    // 3. Installation manifest — lets us tell which variant is really on disk without
    //    guessing (issue #45 troubleshooting: users could not say what they installed).
    InstalledAt := GetDateTimeString('yyyy-mm-dd hh:nn:ss', '-', ':');
    ManifestJson := '{' +
      '"variant": "WebSetup",' +
      '"version": "{#MyAppVersion}",' +
      '"installedAt": "' + InstalledAt + '",' +
      '"appId": "' + MyAppId + '"' +
      '}';
    ManifestPath := ExpandConstant('{app}\install-manifest.json');
    if SaveStringToFile(ManifestPath, ManifestJson, False) then
      Log('Install manifest written: ' + ManifestPath)
    else
      Log('Failed to write install manifest: ' + ManifestPath);
    RegWriteStringValue(HKLM64, 'Software\MantisZip', 'InstalledVariant', 'WebSetup');
    RegWriteStringValue(HKLM64, 'Software\MantisZip', 'InstalledVersion', '{#MyAppVersion}');
  end;
end;

// Ask (unless uninstalling silently) whether per-user data should go too, and always
// clear the first-run markers so a later reinstall starts from a clean slate.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  SettingsDir: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    SettingsDir := ExpandConstant('{localappdata}\MantisZip');
    if DirExists(SettingsDir) and (not UninstallSilent) then
    begin
      if MsgBox(CustomMessage('RemoveUserSettingsMsg'), mbConfirmation,
                MB_YESNO or MB_DEFBUTTON2) = IDYES then
      begin
        Log('Removing per-user settings: ' + SettingsDir);
        DelTree(SettingsDir, True, True, True);
      end
      else
        Log('Keeping per-user settings: ' + SettingsDir);
    end;
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\MantisZip');
  end;
end;
