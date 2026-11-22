; 手机音频托盘 v2（C# / .NET 8）—— Inno Setup 安装脚本
#define MyAppName "手机音频托盘 v2"
#define MyAppVersion "2.0.0"
#define MyAppPublisher "ScrcpyAudioTray"
#define ScrcpyDir "C:\Users\PC\AppData\Local\Microsoft\WinGet\Packages\Genymobile.scrcpy_Microsoft.Winget.Source_8wekyb3d8bbwe\scrcpy-win64-v5.0.1"
#define V2Publish "..\dist-v2"

[Setup]
AppId={{B9E3D7A1-6C24-4F95-AE30-8D2C6F1B4E77}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
; 独立 AppId + 独立目录，与 v1 并存，不覆盖 v1
DefaultDirName={localappdata}\ScrcpyAudioTrayV2
DefaultGroupName={#MyAppName}
; 允许安装时自选目录
DisableDirPage=no
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=dist
OutputBaseFilename=ScrcpyAudioTrayV2-Setup-{#MyAppVersion}
SetupIconFile=..\legacy\v1\tray-active.ico
UninstallDisplayIcon={app}\PhoneAudioTray.exe
UninstallDisplayName={#MyAppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
AllowNoIcons=yes
ShowLanguageDialog=no
MinVersion=10.0

[Languages]
Name: "cn"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："
Name: "startup"; Description: "开机自动启动（静默启动）"; GroupDescription: "附加任务："; Flags: unchecked

[Files]
Source: "{#V2Publish}\PhoneAudioTray.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#V2Publish}\Resources\*"; DestDir: "{app}\Resources"; Flags: ignoreversion
Source: "..\README-V2.txt"; DestDir: "{app}"; Flags: ignoreversion
; scrcpy 引擎（v2 也依赖它）
Source: "{#ScrcpyDir}\*"; DestDir: "{app}\scrcpy"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\PhoneAudioTray.exe"; IconFilename: "{app}\Resources\tray-active.ico"; Comment: "启动托盘 v2"
Name: "{group}\使用说明"; Filename: "{app}\README-V2.txt"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\PhoneAudioTray.exe"; IconFilename: "{app}\Resources\tray-active.ico"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\PhoneAudioTray.exe"; Tasks: startup

[Run]
Filename: "{app}\PhoneAudioTray.exe"; Description: "立即启动 v2"; Flags: postinstall nowait skipifsilent

[UninstallRun]
; v2 进程按安装路径精确停止，避免误杀 v1
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Get-CimInstance Win32_Process -Filter \""Name='PhoneAudioTray.exe'\"" | Where-Object {{ $_.ExecutablePath -like '{app}*' }} | ForEach-Object {{ Stop-Process -Id $_.ProcessId -Force }}"""; Flags: runhidden; RunOnceId: "StopTrayV2"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
