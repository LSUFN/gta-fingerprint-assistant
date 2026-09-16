#ifndef AppVersion
  #define AppVersion "0.0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "."
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif
#ifndef AppIcon
  #define AppIcon "fingerprint-assistant.ico"
#endif

[Setup]
AppId={{5E537EDD-1B32-49A9-9A68-BCBB81A4519C}
AppName=名钻指纹助手
AppVersion={#AppVersion}
AppVerName=名钻指纹助手 {#AppVersion}
AppPublisher=LSF
AppPublisherURL=https://gtacn.org/
AppSupportURL=https://gtacn.org/downloads/fingerprint-assistant
AppUpdatesURL=https://gtacn.org/downloads/fingerprint-assistant
DefaultDirName={localappdata}\Programs\LSF\名钻指纹助手
DefaultGroupName=名钻指纹助手
DisableDirPage=no
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
SetupLogging=yes
SetupIconFile={#AppIcon}
Compression=lzma2/max
SolidCompression=yes
OutputDir={#OutputDir}
OutputBaseFilename=fingerprint-assistant-setup-windows-x64
UninstallDisplayName=名钻指纹助手
UninstallDisplayIcon={app}\名钻指纹助手.exe
VersionInfoCompany=LSF
VersionInfoDescription=名钻指纹助手安装程序
VersionInfoProductName=名钻指纹助手
VersionInfoProductVersion={#AppVersion}
VersionInfoVersion={#AppVersion}
CloseApplications=yes
RestartApplications=yes

[Languages]
Name: "chinesesimp"; MessagesFile: "ChineseSimplified.isl"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\名钻指纹助手"; Filename: "{app}\名钻指纹助手.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\名钻指纹助手"; Filename: "{app}\名钻指纹助手.exe"; WorkingDir: "{app}"

[Run]
Filename: "{app}\名钻指纹助手.exe"; Description: "启动名钻指纹助手"; Flags: nowait postinstall skipifsilent
Filename: "{app}\名钻指纹助手.exe"; Flags: nowait skipifnotsilent; Check: IsAutoUpdate

[Code]
function IsAutoUpdate: Boolean;
begin
  Result := CompareText(ExpandConstant('{param:AUTOUPDATE|0}'), '1') = 0;
end;
