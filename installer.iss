; MantisZip Installer Script
; Requires Inno Setup 6

#define MyAppName "MantisZip"
#define MyAppVersion "0.2.1"
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
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    // Optional: register shell integration post-install
    // This is left as a manual operation via the Settings window
  end;
end;
