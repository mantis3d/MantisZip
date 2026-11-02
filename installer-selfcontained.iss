; MantisZip Self-Contained Installer Script
; This installer bundles the .NET 10 runtime — no separate runtime install needed.
; Derived from installer.iss for framework-dependent builds.
; Requires Inno Setup 6

#define MyAppName "MantisZip"
#ifndef MyAppVersion
#define MyAppVersion "0.5.5"
#endif
#define MyAppPublisher "MantisZip Contributors"
#define MyAppURL "https://github.com/mantis3d/MantisZip"
#define MyAppExeName "MantisZip.UI.Avalonia.exe"

[Setup]
; ONE product, two packagings (issue #45): this AppId is intentionally identical to
; installer.iss, so Windows treats the framework-dependent and self-contained builds as
; the same product — installing one removes the other instead of writing on top of it.
; (The previous script used a different AppId "so both installers can coexist"; since
; both write to the same {app} folder that coexistence was never real, it only produced
; two uninstall logs that deleted each other's files.)
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
OutputBaseFilename=MantisZip-{#MyAppVersion}-Setup-Offline
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
ChangesEnvironment=yes
CloseApplications=yes
; Shared with installer.iss — prevents two setups from writing {app} at the same time.
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

; 旧版产品身份迁移桥（旧的自包含安装包用了另一个产品身份）
chinese.LegacyAppRunningMsg=MantisZip 正在运行。%n%n安装程序需要先关闭它，才能移除旧版本。现在关闭并继续吗？
chinese.LegacyUninstallFailedMsg=无法自动移除上一版 MantisZip。%n%n请先从「设置 → 应用」中卸载 MantisZip，然后重新运行本安装程序。
; 卸载：是否同时删除个人数据
chinese.RemoveUserSettingsMsg=是否同时删除 MantisZip 的个人设置与最近文件记录？%n%n选择「否」将保留它们（以后重装可继续使用）。

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; === All DLLs (wildcard — automatically includes new dependencies) ===
Source: "publish_output_selfcontained\*.dll"; DestDir: "{app}"; Flags: ignoreversion

; === Executables ===
Source: "publish_output_selfcontained\MantisZip.UI.Avalonia.exe"; DestDir: "{app}"; Flags: ignoreversion

; === Runtime config (required for .NET assembly resolution) ===
Source: "publish_output_selfcontained\MantisZip.UI.Avalonia.deps.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish_output_selfcontained\MantisZip.UI.Avalonia.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion

; === ShellExt COM host (dynamic context menu) ===
Source: "publish_output_selfcontained\MantisZip.ShellExt.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion

; === 7z.dll (SharpSevenZip): x64 only (app is win-x64) ===
Source: "publish_output_selfcontained\x64\7z.dll"; DestDir: "{app}\x64"; Flags: ignoreversion

; === Resources (file type icons, context menu icons, drag cursors, localization) ===
Source: "publish_output_selfcontained\Resources\Icons\*.ico"; DestDir: "{app}\Resources\Icons"; Flags: ignoreversion
Source: "publish_output_selfcontained\Resources\MenuIcons\*.ico"; DestDir: "{app}\Resources\MenuIcons"; Flags: ignoreversion
Source: "publish_output_selfcontained\Resources\Cursors\*.cur"; DestDir: "{app}\Resources\Cursors"; Flags: ignoreversion
Source: "publish_output_selfcontained\Localization\strings.en.json"; DestDir: "{app}\Localization"; Flags: ignoreversion
Source: "publish_output_selfcontained\Localization\strings.zh-CN.json"; DestDir: "{app}\Localization"; Flags: ignoreversion
Source: "publish_output_selfcontained\Localization\strings.zh-TW.json"; DestDir: "{app}\Localization"; Flags: ignoreversion
Source: "publish_output_selfcontained\Resources\languages.json"; DestDir: "{app}\Resources"; Flags: ignoreversion

; === Contributor CSV files (compiled into AboutWindow) ===
Source: "publish_output_selfcontained\contributors-technical.csv"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish_output_selfcontained\contributors-financial.csv"; DestDir: "{app}"; Flags: ignoreversion

; === License files ===
; 7z.dll (SharpSevenZip) is distributed under GNU Lesser General Public License
Source: "lgpl.txt"; DestDir: "{app}"; Flags: ignoreversion

; === Prebuilt user settings (copied to %LOCALAPPDATA% on fresh install) ===
; Replace files in installer\prebuilt\ with your own settings from %LOCALAPPDATA%\MantisZip\
Source: "installer\prebuilt\settings.json"; DestDir: "{app}\prebuilt"; Flags: ignoreversion
Source: "installer\prebuilt\window.json"; DestDir: "{app}\prebuilt"; Flags: ignoreversion

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
  // Product identity — must stay identical to installer.iss (one product, two packagings).
  MyAppId = '{F7A3C8E1-2D4B-4A9F-9C6E-8B5D7A3F2E1C}';
  // Identity used by the old self-contained installer; kept only for the migration bridge.
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

// Sleep from kernel32 — used by the legacy migration bridge to poll the uninstall key.
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
  CreateConfigPage;
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

// Write installer settings to AppData after install completes
procedure CurStepChanged(CurStep: TSetupStep);
var
  Json: string;
  SettingsDir: string;
  SettingsFile: string;
  WindowFile: string;
  ManifestPath: string;
  ManifestJson: string;
  InstalledAt: string;
begin
  if CurStep = ssPostInstall then
  begin
    // Shell integration deferred to first user launch (non-elevated context).
    // SHChangeNotify from an elevated (installer) process does NOT propagate
    // to the non-elevated Explorer.exe, so dynamic COM context menus appear
    // missing until reinstalled from MantisZip's Settings window.
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

    // Installation manifest — lets us tell which variant is really on disk without
    // guessing (issue #45 troubleshooting).
    InstalledAt := GetDateTimeString('yyyy-mm-dd hh:nn:ss', '-', ':');
    ManifestJson := '{' +
      '"variant": "Offline",' +
      '"version": "{#MyAppVersion}",' +
      '"installedAt": "' + InstalledAt + '",' +
      '"appId": "' + MyAppId + '"' +
      '}';
    ManifestPath := ExpandConstant('{app}\install-manifest.json');
    if SaveStringToFile(ManifestPath, ManifestJson, False) then
      Log('Install manifest written: ' + ManifestPath)
    else
      Log('Failed to write install manifest: ' + ManifestPath);
    RegWriteStringValue(HKLM64, 'Software\MantisZip', 'InstalledVariant', 'Offline');
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
