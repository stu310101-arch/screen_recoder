#define AppName "Amos錄影與截圖"
#define AppVersion "1.3.2"

[Setup]
AppId={{B38929C2-823A-4AB2-9B5A-191483A8AD65}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=stu310101-arch
AppPublisherURL=https://github.com/stu310101-arch/screen_recoder
AppUpdatesURL=https://github.com/stu310101-arch/screen_recoder/releases/latest
DefaultDirName={localappdata}\Programs\AureoRecorder
DefaultGroupName={#AppName}
UsePreviousGroup=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\release
OutputBaseFilename=AmosRecorder-Setup
SetupIconFile=..\Source\aureo.ico
UninstallDisplayIcon={app}\icons\AmosRecorder-v{#AppVersion}.ico
WizardStyle=modern
DisableWelcomePage=no
DisableProgramGroupPage=yes
Compression=lzma2/fast
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}
VersionInfoDescription=Amos錄影與截圖 安裝程式
VersionInfoProductName={#AppName}

[Languages]
Name: "chinesetraditional"; MessagesFile: "ChineseTraditional.isl"

[Tasks]
Name: "desktopicon"; Description: "建立桌面捷徑"; GroupDescription: "捷徑選項："

[Files]
Source: "..\Source\aureo.ico"; DestDir: "{app}\icons"; DestName: "AmosRecorder-v{#AppVersion}.ico"; Flags: ignoreversion
Source: "..\App\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}\{#AppName}"; Filename: "{app}\AureoRecorder.exe"; WorkingDir: "{app}"; IconFilename: "{app}\icons\AmosRecorder-v{#AppVersion}.ico"; IconIndex: 0
Name: "{autoprograms}\{#AppName}\使用說明"; Filename: "{app}\使用說明.txt"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\AureoRecorder.exe"; WorkingDir: "{app}"; IconFilename: "{app}\icons\AmosRecorder-v{#AppVersion}.ico"; IconIndex: 0; Tasks: desktopicon

[InstallDelete]
Type: files; Name: "{autodesktop}\AUREO 錄影與截圖.lnk"
Type: files; Name: "{autodesktop}\AUREO 螢幕錄影.lnk"

[Run]
Filename: "{app}\AureoRecorder.exe"; Description: "立即啟動 Amos錄影與截圖"; Flags: nowait postinstall skipifsilent
