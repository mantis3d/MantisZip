; MantisZip Self-Contained Installer Script
; This installer bundles the .NET 9 runtime — no separate runtime install needed.
; Derived from installer.iss for framework-dependent builds.
; Requires Inno Setup 6

#define MyAppName "MantisZip"
#ifndef MyAppVersion
#define MyAppVersion "0.4.4"
#endif
#define MyAppPublisher "MantisZip Contributors"
#define MyAppURL "https://github.com/mantis3d/MantisZip"
#define MyAppExeName "MantisZip.UI.Avalonia.exe"

[Setup]
; Must use a different AppId from the framework-dependent installer to avoid
; Windows Installer conflict (both installers can coexist).
AppId={{963001F3-5748-4834-9489-97D6D00E3917}
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

; Chinese (Simplified)
chinese.ConfigPageTitle=安装配置
chinese.ConfigDesc=选择偏好的外观和系统集成设置
chinese.ThemeGroup=外观
chinese.ThemeLight=浅色主题
chinese.ThemeDark=深色主题
chinese.ShellGroup=系统集成
chinese.InstallShell=添加到 Windows 右键菜单
chinese.AssocGroup=文件关联

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

; === 7z.dll (SharpSevenZip): architecture-specific subdirectories ===
; x86 uses skipifsourcedoesntexist: copy-7z-dll.ps1 only bundles a real 32-bit
; 7z.dll when a 32-bit 7-Zip is present on the build machine — never a fake copy.
Source: "publish_output_selfcontained\x64\7z.dll"; DestDir: "{app}\x64"; Flags: ignoreversion
Source: "publish_output_selfcontained\x86\7z.dll"; DestDir: "{app}\x86"; Flags: ignoreversion skipifsourcedoesntexist

; === Resources (context menu icons, drag cursors, localization) ===
Source: "publish_output_selfcontained\Resources\MenuIcons\*.ico"; DestDir: "{app}\Resources\MenuIcons"; Flags: ignoreversion
Source: "publish_output_selfcontained\Resources\Cursors\*.cur"; DestDir: "{app}\Resources\Cursors"; Flags: ignoreversion
Source: "publish_output_selfcontained\Localization\strings.en.json"; DestDir: "{app}\Localization"; Flags: ignoreversion
Source: "publish_output_selfcontained\Localization\strings.zh-CN.json"; DestDir: "{app}\Localization"; Flags: ignoreversion
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
Filename: "{app}\{#MyAppExeName}"; Parameters: "--uninstall-shell"; Flags: runhidden; WorkingDir: "{app}"
Filename: "{app}\{#MyAppExeName}"; Parameters: "--uninstall-assoc"; Flags: runhidden; WorkingDir: "{app}"

[Code]
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

// Write installer settings to AppData after install completes
procedure CurStepChanged(CurStep: TSetupStep);
var
  Json: string;
  SettingsDir: string;
  SettingsFile: string;
  WindowFile: string;
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
  end;
end;
