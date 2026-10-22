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
Name: "webicon"; Description: "Tạo lối tắt web (máy chủ học) ngoài Desktop"; GroupDescription: "Lối tắt:"; Flags: unchecked

[Files]
; The desktop app.
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; The study server and website, in a folder of its own (it carries its own .NET runtime).
Source: "..\artifacts\publish-server\*"; DestDir: "{app}\server"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
; Starts the server (a minimised window; close it to stop the website) and opens the website.
Name: "{autoprograms}\{#MyWebName}"; Filename: "{app}\server\{#MyServerExeName}"; Parameters: "--open-browser"; WorkingDir: "{app}\server"; Flags: runminimized
Name: "{autodesktop}\{#MyWebName}"; Filename: "{app}\server\{#MyServerExeName}"; Parameters: "--open-browser"; WorkingDir: "{app}\server"; Flags: runminimized; Tasks: webicon
; The same, reachable from phones on the same Wi-Fi (http://<this PC's IP>:5080).
Name: "{autoprograms}\{#MyWebName} (cho điện thoại cùng Wi-Fi)"; Filename: "{app}\server\{#MyServerExeName}"; Parameters: "--open-browser --urls http://0.0.0.0:5080"; WorkingDir: "{app}\server"; Flags: runminimized

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Khởi chạy {#MyAppName}"; Flags: nowait postinstall skipifsilent
Filename: "{app}\server\{#MyServerExeName}"; Parameters: "--open-browser"; WorkingDir: "{app}\server"; Description: "Mở {#MyWebName}"; Flags: nowait postinstall skipifsilent unchecked runminimized
