; 手机音频托盘 —— Inno Setup 安装脚本
#define MyAppName "手机音频托盘"
#define MyAppVersion "1.7.1"
#define MyAppPublisher "ScrcpyAudioTray"
#define ScrcpyDir "C:\Users\PC\AppData\Local\Microsoft\WinGet\Packages\Genymobile.scrcpy_Microsoft.Winget.Source_8wekyb3d8bbwe\scrcpy-win64-v5.0.1"

[Setup]
AppId={{A7E4C1D2-5B3F-4C86-9E21-7D5A0B4F8C63}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\ScrcpyAudioTray
DefaultGroupName={#MyAppName}
; 强制显示「选择安装位置」页：PrivilegesRequired=lowest 时默认会跳过这页
DisableDirPage=no
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=dist
OutputBaseFilename=ScrcpyAudioTray-Setup-{#MyAppVersion}
SetupIconFile=tray-active.ico
UninstallDisplayIcon={app}\tray-active.ico
UninstallDisplayName={#MyAppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
AllowNoIcons=yes
ShowLanguageDialog=no
MinVersion=10.0
AppSupportURL=
AppUpdatesURL=

[Languages]
Name: "cn"; MessagesFile: "build\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："
Name: "startup"; Description: "开机自动启动（静默启动，不弹窗）"; GroupDescription: "附加任务："; Flags: unchecked

[Files]
Source: "scrcpy-audio-tray.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "tray-idle.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "tray-active.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "icons\scrcpy.png"; DestDir: "{app}\icons"; Flags: ignoreversion
Source: "启动-手机音频托盘.vbs"; DestDir: "{app}"; Flags: ignoreversion
Source: "手机投屏带声音.bat"; DestDir: "{app}"; Flags: ignoreversion
Source: "无线-首次配对.bat"; DestDir: "{app}"; Flags: ignoreversion
Source: "无线音频-游戏模式.bat"; DestDir: "{app}"; Flags: ignoreversion
Source: "手机声音到电脑-低延迟游戏模式.bat"; DestDir: "{app}"; Flags: ignoreversion
Source: "README.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "build\stop-tray.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "build\stop-tray.ps1"; Flags: dontcopy
Source: "{#ScrcpyDir}\*"; DestDir: "{app}\scrcpy"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\启动-手机音频托盘.vbs"; IconFilename: "{app}\tray-active.ico"; Comment: "启动托盘"
Name: "{group}\无线配对（首次使用）"; Filename: "{app}\无线-首次配对.bat"; IconFilename: "{app}\tray-idle.ico"; Comment: "第一次连接手机时运行"
Name: "{group}\使用说明"; Filename: "{app}\README.txt"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\启动-手机音频托盘.vbs"; IconFilename: "{app}\tray-active.ico"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\启动-手机音频托盘.vbs"; Tasks: startup

[Run]
Filename: "{app}\启动-手机音频托盘.vbs"; Description: "立即启动托盘"; Flags: postinstall nowait skipifsilent shellexec

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\stop-tray.ps1"""; Flags: runhidden; RunOnceId: "StopTray"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  R: Integer;
begin
  if CurStep = ssInstall then
  begin
    { 覆盖安装时先停掉正在运行的托盘，否则图标和脚本文件会占用 }
    ExtractTemporaryFile('stop-tray.ps1');
    Exec('powershell.exe',
         ExpandConstant('-NoProfile -ExecutionPolicy Bypass -File "{tmp}\stop-tray.ps1"'),
         '', SW_HIDE, ewWaitUntilTerminated, R);
  end;
end;
