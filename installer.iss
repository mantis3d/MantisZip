; MantisZip Installer Script
; Requires Inno Setup 6

#define MyAppName "MantisZip"
#define MyAppVersion "0.2.13"
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
Source: "publish_output\SevenZipExtractor.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish_output\Ude.NetStandard.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish_output\x64\7z.dll"; DestDir: "{app}\x64"; Flags: ignoreversion
Source: "publish_output\x86\7z.dll"; DestDir: "{app}\x86"; Flags: ignoreversion
; Include all native DLLs recursively if any
Source: "publish_output\*.dll"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent; WorkingDir: "{app}"

[UninstallRun]
; Note: shell integration cleanup is manual via Settings window

[Code]
const
  WebView2RegKey = 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  EvergreenBootstrapperUrl = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703';

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
procedure CurStepChanged(CurStep: TSetupStep);
var
  BootstrapperPath: string;
  ResultCode: Integer;
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
end;
