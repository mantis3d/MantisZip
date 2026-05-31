; MantisZip Installer Script
; Requires Inno Setup 6

#define MyAppName "MantisZip"
#define MyAppVersion "0.3.7"
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
english.InstallAssoc=Associate archive file types (.zip, .7z, .rar, etc.)

; Chinese (Simplified)
chinese.ConfigPageTitle=安装配置
chinese.ConfigDesc=选择偏好的外观和系统集成设置
chinese.ThemeGroup=外观
chinese.ThemeLight=浅色主题
chinese.ThemeDark=深色主题
chinese.ShellGroup=系统集成
chinese.InstallShell=添加到 Windows 右键菜单
chinese.InstallAssoc=关联压缩包文件格式（.zip, .7z, .rar 等）

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "publish_output\MantisZip.UI.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish_output\MantisZip.UI.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish_output\MantisZip.UI.deps.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish_output\MantisZip.UI.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish_output\MantisZip.Core.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish_output\MantisZip.Core.pdb"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish_output\MantisZip.UI.pdb"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish_output\CommunityToolkit.Mvvm.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish_output\ICSharpCode.SharpZipLib.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish_output\Markdig.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish_output\Microsoft.Web.WebView2.Core.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish_output\Microsoft.Web.WebView2.Wpf.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish_output\Ookii.Dialogs.Wpf.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish_output\Ude.NetStandard.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish_output\x64\7z.dll"; DestDir: "{app}\x64"; Flags: ignoreversion
Source: "publish_output\x86\7z.dll"; DestDir: "{app}\x86"; Flags: ignoreversion
; LGPL license for 7z.dll (distributed under GNU Lesser General Public License)
Source: "lgpl.txt"; DestDir: "{app}"; Flags: ignoreversion
; Include all native DLLs recursively if any
Source: "publish_output\*.dll"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--install-shell"; Flags: nowait skipifsilent; WorkingDir: "{app}"; Check: IsShellInstallChecked
Filename: "{app}\{#MyAppExeName}"; Parameters: "--install-assoc"; Flags: nowait skipifsilent; WorkingDir: "{app}"; Check: IsAssocInstallChecked
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent; WorkingDir: "{app}"

[UninstallRun]
; Note: shell integration cleanup is manual via Settings window

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
  InstallAssocCheck: TNewCheckBox;

// Create the custom configuration wizard page (theme + system integration)
procedure CreateConfigPage;
var
  ThemeGroupLabel: TNewStaticText;
  ShellGroupLabel: TNewStaticText;
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

  InstallAssocCheck := TNewCheckBox.Create(WPConfigPage);
  InstallAssocCheck.Parent := WPConfigPage.Surface;
  InstallAssocCheck.Caption := CustomMessage('InstallAssoc');
  InstallAssocCheck.Top := InstallShellCheck.Top + ScaleY(24);
  InstallAssocCheck.Left := 16;
  InstallAssocCheck.Width := WPConfigPage.SurfaceWidth - ScaleX(32);
  InstallAssocCheck.Checked := True;
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

function IsAssocInstallChecked: Boolean;
begin
  Result := InstallAssocCheck.Checked;
end;

procedure InitializeWizard;
begin
  CreateConfigPage;
end;

// Check if WebView2 Runtime is already installed
function IsWebView2Installed: Boolean;
begin
  Result := RegKeyExists(HKLM, WebView2RegKey) or
            RegKeyExists(HKCU, WebView2RegKey);
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
  if CurStep = ssInstall then
  begin
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
  end;

  if CurStep = ssPostInstall then
  begin
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
