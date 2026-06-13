; MantisZip Installer Script
; Requires Inno Setup 6

#define MyAppName "MantisZip"
#define MyAppVersion "0.3.13"
#define MyAppPublisher "MantisZip Contributors"
#define MyAppURL "https://github.com/yourusername/MantisZip"
#define MyAppExeName "MantisZip.UI.exe"

[Setup]
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
OutputBaseFilename=MantisZip-{#MyAppVersion}-Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
ChangesEnvironment=yes
SetupIconFile=src\MantisZip.UI\Resources\App.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinese"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

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
Source: "publish_output\*.dll"; DestDir: "{app}"; Flags: ignoreversion

; === Executables ===
Source: "publish_output\MantisZip.UI.exe"; DestDir: "{app}"; Flags: ignoreversion

; === Debug symbols ===
Source: "publish_output\MantisZip.Core.pdb"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish_output\MantisZip.UI.pdb"; DestDir: "{app}"; Flags: ignoreversion

; === Runtime config (required for .NET assembly resolution) ===
Source: "publish_output\MantisZip.UI.deps.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish_output\MantisZip.UI.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish_output\MantisZip.ShellExt.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion

; === 7z.dll (SharpSevenZip): architecture-specific subdirectories ===
Source: "publish_output\x64\7z.dll"; DestDir: "{app}\x64"; Flags: ignoreversion
Source: "publish_output\x86\7z.dll"; DestDir: "{app}\x86"; Flags: ignoreversion

; === Resources (app icon, file type icons, context menu icons, localization) ===
Source: "publish_output\Resources\App.ico"; DestDir: "{app}\Resources"; Flags: ignoreversion
Source: "publish_output\Resources\Icons\*.ico"; DestDir: "{app}\Resources\Icons"; Flags: ignoreversion
Source: "publish_output\Resources\MenuIcons\*.ico"; DestDir: "{app}\Resources\MenuIcons"; Flags: ignoreversion
Source: "publish_output\Resources\strings.en.json"; DestDir: "{app}\Resources"; Flags: ignoreversion
Source: "publish_output\Resources\strings.zh.json"; DestDir: "{app}\Resources"; Flags: ignoreversion
Source: "publish_output\Resources\languages.json"; DestDir: "{app}\Resources"; Flags: ignoreversion

; === License (7z.dll is distributed under GNU Lesser General Public License) ===
Source: "lgpl.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--install-shell"; Flags: nowait skipifsilent; WorkingDir: "{app}"; Check: IsShellInstallChecked
Filename: "{app}\{#MyAppExeName}"; Parameters: "--install-assoc {code:GetAssocParams}"; Flags: nowait skipifsilent; WorkingDir: "{app}"; Check: IsAnyAssocChecked
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent; WorkingDir: "{app}"

[UninstallRun]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--uninstall-shell"; Flags: runhidden; WorkingDir: "{app}"
Filename: "{app}\{#MyAppExeName}"; Parameters: "--uninstall-assoc"; Flags: runhidden; WorkingDir: "{app}"

[Code]
const
  WebView2RegKey = 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  EvergreenBootstrapperUrl = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703';

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

procedure InitializeWizard;
begin
  CreateConfigPage;
end;

// Check if WebView2 Runtime is already installed.
// Checks multiple registry locations and confirms a version value exists (not just a key).
function IsWebView2Installed: Boolean;
var
  version: string;
begin
  // 64-bit view (HKLM) or HKCU
  Result := RegQueryStringValue(HKLM, WebView2RegKey, 'pv', version) or
            RegQueryStringValue(HKCU, WebView2RegKey, 'pv', version);
  // 32-bit (WOW6432Node) view — WebView2 installer often registers here on 64-bit Windows
  if not Result then
    Result := RegQueryStringValue(HKLM32, WebView2RegKey, 'pv', version);
end;

// Download file via URLMon (built-in Windows API, no extra DLLs needed)
function URLDownloadToFile(pCaller: Cardinal; szURL: string; szFileName: string; dwReserved: Cardinal; lpfnCB: Cardinal): Integer;
  external 'URLDownloadToFileW@urlmon.dll stdcall';

// Install WebView2 Runtime before app starts
// Write installer settings to AppData after install completes
procedure CurStepChanged(CurStep: TSetupStep);
var
  BootstrapperPath: string;
  ResultCode: Integer;
  Json: string;
  SettingsDir: string;
  SettingsFile: string;
begin
  if CurStep = ssPostInstall then
  begin
    // 在复制文件完成后安装 WebView2 Runtime（避免阻塞文件安装进度条）
    if not IsWebView2Installed then
    begin
      BootstrapperPath := ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe');
      Log('WebView2 Runtime not found. Downloading Evergreen Bootstrapper...');
      if URLDownloadToFile(0, EvergreenBootstrapperUrl, BootstrapperPath, 0, 0) = 0 then
      begin
        Log('Downloaded bootstrapper. Installing silently...');
        if Exec(BootstrapperPath, '/silent /install', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
        begin
          if ResultCode = 0 then
            Log('WebView2 Runtime installed successfully.')
          else
            Log('WebView2 Runtime installer exited with code: ' + IntToStr(ResultCode));
        end
        else
          Log('Failed to launch WebView2 bootstrapper.');
      end
      else
        Log('Failed to download WebView2 bootstrapper. User may need to install manually.');
    end
    else
      Log('WebView2 Runtime is already installed.');

    SettingsDir := ExpandConstant('{localappdata}\MantisZip');
    SettingsFile := SettingsDir + '\settings.json';

    // Only write on fresh install — don't overwrite existing user settings on upgrade
    if not FileExists(SettingsFile) then
    begin
      Log('Writing installer settings to: ' + SettingsFile);
      if not DirExists(SettingsDir) then
        CreateDir(SettingsDir);

      Json := '{' +
        '"Language": "' + GetAppLanguageCode + '",' +
        '"Theme": "' + GetSelectedTheme + '"' +
        '}';
      SaveStringToFile(SettingsFile, Json, False);
      Log('Installer settings written successfully.');
    end
    else
      Log('Settings file already exists, preserving existing user settings.');
  end;
end;
