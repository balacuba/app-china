#define MyAppName "Chinese Learning App"
#define MyAppExeName "ChineseLearningApp.exe"
#define MyServerExeName "ChineseLearningServer.exe"
#define MyWebName "Học tiếng Trung - Web"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

[Setup]
AppId={{5171708E-BD60-48F7-9EBF-BC47DB3C08F3}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=ChineseLearningApp
DefaultDirName={localappdata}\Programs\ChineseLearningApp
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts
OutputBaseFilename=ChineseLearningApp-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
CloseApplicationsFilter={#MyAppExeName},{#MyServerExeName}

[Tasks]
Name: "desktopicon"; Description: "Tạo lối tắt app ngoài Desktop"; GroupDescription: "Lối tắt:"; Flags: unchecked

[InstallDelete]
; Older versions ran the website as a separate server with its own shortcuts;
; the app now runs it itself.
Type: filesandordirs; Name: "{app}\server"
Type: files; Name: "{autoprograms}\{#MyWebName}.lnk"
Type: files; Name: "{autoprograms}\{#MyWebName} (cho điện thoại cùng Wi-Fi).lnk"
Type: files; Name: "{autodesktop}\{#MyWebName}.lnk"

[Files]
; The desktop app, with the study website inside it.
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Khởi chạy {#MyAppName}"; Flags: nowait postinstall skipifsilent
; An update from inside the app runs this installer silently: open the app again afterwards.
Filename: "{app}\{#MyAppExeName}"; Flags: nowait postinstall skipifnotsilent
