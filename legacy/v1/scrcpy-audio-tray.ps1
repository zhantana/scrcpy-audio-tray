# 手机声音 -> 电脑耳机  托盘控制面板
param([switch]$SelfTest)
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

# DPI 感知必须在任何窗口创建之前声明。
# 不声明的话，本进程按「缩放后的逻辑坐标」工作（125% 下 1 逻辑像素 = 1.25 物理像素），
# 而 scrcpy 是按物理像素的，把它嵌进来后系统会把它的画面再缩放一次，
# 结果是尺寸对不上、比例看着不对，而且拖窗口四边也改不动。
$script:dpiScale = 1.0
try {
    Add-Type -Namespace TrayDpi -Name Api -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] public static extern uint GetDpiForSystem();
'@ -ErrorAction Stop
    try { [void][TrayDpi.Api]::SetProcessDPIAware() } catch { }
    try {
        $script:sysDpi = [int][TrayDpi.Api]::GetDpiForSystem()
        if ($script:sysDpi -ge 96) { $script:dpiScale = $script:sysDpi / 96.0 }
    } catch {
        # 老系统没有 GetDpiForSystem，退回注册表里记的系统 DPI
        try {
            $v = [int](Get-ItemProperty 'HKCU:\Control Panel\Desktop\WindowMetrics' -Name AppliedDPI -ErrorAction SilentlyContinue).AppliedDPI
            if ($v -ge 96) { $script:dpiScale = $v / 96.0 }
        } catch { }
    }
} catch { }
$script:S = $script:dpiScale

$logFile = Join-Path $PSScriptRoot 'tray-error.log'

$mutex = New-Object System.Threading.Mutex($false, 'Global\ScrcpyAudioTrayUI')
if (-not $mutex.WaitOne(0)) { exit }

try {

# ---------------- 定位 scrcpy ----------------
$script:scrcpy = $null
# 优先用同目录自带的 scrcpy（安装包会带一份），这样不依赖机器上是否装过
$localScrcpy = Join-Path $PSScriptRoot 'scrcpy\scrcpy.exe'
if (Test-Path $localScrcpy) { $script:scrcpy = $localScrcpy }
$c = Get-Command scrcpy.exe -ErrorAction SilentlyContinue
if (-not $script:scrcpy -and $c) { $script:scrcpy = $c.Source }
if (-not $script:scrcpy) {
    $pkgRoot = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages'
    if (Test-Path $pkgRoot) {
        foreach ($d in (Get-ChildItem $pkgRoot -Directory -Filter 'Genymobile.scrcpy_*' -ErrorAction SilentlyContinue)) {
            foreach ($s in (Get-ChildItem $d.FullName -Directory -Filter 'scrcpy-win64-*' -ErrorAction SilentlyContinue)) {
                $exe = Join-Path $s.FullName 'scrcpy.exe'
                if (Test-Path $exe) { $script:scrcpy = $exe; break }
            }
            if ($script:scrcpy) { break }
        }
    }
}
if (-not $script:scrcpy) {
    [System.Windows.Forms.MessageBox]::Show('没找到 scrcpy.exe', '启动失败') | Out-Null
    exit
}
$script:adb = Join-Path (Split-Path $script:scrcpy) 'adb.exe'

# ---------------- 状态 ----------------
$script:settings = @{ Buffer = 40; Codec = 'raw'; Serial = $null; Screen = $false; OnTop = $false; MaxFps = 60; WifiTargets = @(); HotkeyPause = 'Ctrl+Alt+P'; HotkeyScreen = 'F7'; SizePortrait = @(); SizeLandscape = @(); WindowMode = 'embedded' }
$script:proc = $null
# 上一次看到的「别的 scrcpy 实例」数量，用来发现有人关了投屏
$script:otherCount = 0
$script:audioRestartPending = 0
$script:devLabel = ''
$script:namesFile = Join-Path $PSScriptRoot 'device-names.json'
$script:pinFile = Join-Path $PSScriptRoot 'device-pin.json'
$script:settingsFile = Join-Path $PSScriptRoot 'settings.json'
$script:userStopped = $false
$script:retryLeft = 0
$script:retryTimer = $null
# 用户这次已经拒绝过输密码，自动重试就别再弹框；主动点菜单会重置
$script:pinAskBlock = $false

function Read-Settings {
    if (-not (Test-Path $script:settingsFile)) { return }
    try {
        $raw = [IO.File]::ReadAllText($script:settingsFile, [Text.Encoding]::UTF8).TrimStart([char]0xFEFF)
        if (-not $raw.Trim()) { return }
        $o = $raw | ConvertFrom-Json
        if ($null -ne $o.Buffer) { $script:settings.Buffer = [int]$o.Buffer }
        if ($o.Codec) { $script:settings.Codec = [string]$o.Codec }
        if ($null -ne $o.Screen) { $script:settings.Screen = [bool]$o.Screen }
        if ($null -ne $o.OnTop) { $script:settings.OnTop = [bool]$o.OnTop }
        if ($null -ne $o.MaxFps) {
            $f = [int]$o.MaxFps
            # 0 = 不限帧率；低于 15 的当无效值，回落到默认 60
            if ($f -eq 0 -or $f -ge 15) { $script:settings.MaxFps = $f }
        }
        # 记住的无线地址，下次启动直接连
        $wt = @()
        if ($o.WifiTargets) { foreach ($t in @($o.WifiTargets)) { if ("$t".Trim()) { $wt += "$t".Trim() } } }
        $script:settings.WifiTargets = @($wt | Select-Object -Unique)
        # 空字符串是有意义的（表示禁用快捷键），只有属性整个不存在才用默认值
        if ($null -ne $o.HotkeyPause) {
            $hp = ([string]$o.HotkeyPause).Trim()
            # "None" 是旧版录制时读错事件参数留下的垃圾值，当作没设置过，回默认
            if ($hp -eq 'None' -or $hp -eq 'none') { $hp = 'Ctrl+Alt+P' }
            $script:settings.HotkeyPause = $hp
        }
        if ($null -ne $o.HotkeyScreen) {
            $hs = ([string]$o.HotkeyScreen).Trim()
            if ($hs -eq 'None' -or $hs -eq 'none') { $hs = 'F7' }
            $script:settings.HotkeyScreen = $hs
        }
        # 各方向记忆的窗口尺寸 [宽,高]
        if ($o.SizePortrait) {
            $sp = @($o.SizePortrait | ForEach-Object { [int]$_ })
            if ($sp.Count -eq 2 -and $sp[0] -gt 200 -and $sp[1] -gt 200) { $script:settings.SizePortrait = $sp }
        }
        if ($o.SizeLandscape) {
            $sl = @($o.SizeLandscape | ForEach-Object { [int]$_ })
            if ($sl.Count -eq 2 -and $sl[0] -gt 200 -and $sl[1] -gt 200) { $script:settings.SizeLandscape = $sl }
        }
        if ($o.WindowMode) {
            $wm = ([string]$o.WindowMode).Trim().ToLower()
            if ($wm -eq 'native' -or $wm -eq 'embedded') { $script:settings.WindowMode = $wm }
        }
    } catch { }
}

function Save-Settings {
    try {
        @{
            Buffer = $script:settings.Buffer; Codec = $script:settings.Codec; Screen = $script:settings.Screen
            OnTop = $script:settings.OnTop; MaxFps = $script:settings.MaxFps
            WifiTargets = @($script:settings.WifiTargets)
            HotkeyPause = [string]$script:settings.HotkeyPause
            HotkeyScreen = [string]$script:settings.HotkeyScreen
            SizePortrait = @($script:settings.SizePortrait)
            SizeLandscape = @($script:settings.SizeLandscape)
            WindowMode = [string]$script:settings.WindowMode
        } | ConvertTo-Json -Depth 3 | Out-File -FilePath $script:settingsFile -Encoding utf8
    } catch { }
}

# 记住一个无线地址：去重、新的排前面、最多 8 条
function Add-WifiTarget($addr) {
    $addr = ([string]$addr).Trim()
    if (-not $addr) { return }
    $cur = @($script:settings.WifiTargets)
    if ($cur.Count -gt 0 -and $cur[0] -eq $addr) { return }
    $list = @($addr)
    foreach ($t in $cur) { if ($t -and $t -ne $addr) { $list += $t } }
    $script:settings.WifiTargets = @($list | Select-Object -First 8)
    Save-Settings
}

Read-Settings

function Notify($title, $text, $kind = 'Info') {
    try {
        if (-not $script:ni) { return }
        $script:ni.BalloonTipTitle = $title
        $script:ni.BalloonTipText = $text
        $script:ni.BalloonTipIcon = $kind
        $script:ni.ShowBalloonTip(2500)
    } catch { }
}

function Connect-Saved {
    $ErrorActionPreference = 'Continue'
    try {
        # 记住过的无线地址先连；旧的 wifi_target.txt 只作一次性迁移来源
        foreach ($t in @($script:settings.WifiTargets)) {
            if ($t) { & $script:adb connect $t 2>$null | Out-Null }
        }
        $target = Join-Path $PSScriptRoot 'wifi_target.txt'
        if (Test-Path $target) {
            $t = (Get-Content $target -Raw).Trim()
            if ($t) {
                & $script:adb connect $t 2>$null | Out-Null
                # 老文件里的地址搬进 settings.json，文件本身留着不做破坏性删除
                Add-WifiTarget $t
            }
        }
        $svc = @()
        try { $svc = @(& $script:adb mdns services 2>$null) } catch { $svc = @() }
        foreach ($line in $svc) {
            if ("$line" -notmatch '_adb-tls-connect\._tcp') { continue }
            $parts = @(("$line" -split '\s+') | Where-Object { $_ })
            if ($parts.Count -ge 3) { & $script:adb connect $parts[2] 2>$null | Out-Null }
        }
    } catch { }
}

# ---------------- 全局快捷键 ----------------
# 托盘没有窗口，RegisterHotKey 需要一个句柄收 WM_HOTKEY，这里借 NativeWindow 造一个。
# 编译失败就退化成「没有快捷键」，不能连累托盘启动。
$script:hkAvailable = $false
try {
$wfAsm = [System.Windows.Forms.Form].Assembly.Location
$dwAsm = [System.Drawing.Point].Assembly.Location
Add-Type -ReferencedAssemblies $wfAsm, $dwAsm -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public class TrayHotkeyWindow : NativeWindow
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public event EventHandler HotkeyPressed;
    public event EventHandler ScreenTogglePressed;

    public TrayHotkeyWindow()
    {
        CreateParams cp = new CreateParams();
        cp.Caption = "ScrcpyAudioTrayHotkey";
        this.CreateHandle(cp);
    }

    public bool Register(int id, uint modifiers, uint key)
    {
        return RegisterHotKey(this.Handle, id, modifiers, key);
    }

    public void Unregister(int id)
    {
        UnregisterHotKey(this.Handle, id);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312)
        {
            // wParam carries the registered id: 0x5150 pause, 0x5151 screen toggle
            int id = m.WParam.ToInt32();
            if (id == 0x5150)
            {
                EventHandler h = this.HotkeyPressed;
                if (h != null) { h(this, EventArgs.Empty); }
            }
            else if (id == 0x5151)
            {
                EventHandler h = this.ScreenTogglePressed;
                if (h != null) { h(this, EventArgs.Empty); }
            }
        }
        base.WndProc(ref m);
    }
}
"@
$script:hkAvailable = $true
} catch {
    # 编译器缺失或引用不到 WinForms 时走到这里；快捷键不能用，其他照常
}

$script:hkWin = $null
$script:hkId = 0x5150
$script:hkScreenId = 0x5151
$script:hkOk = $false

# "Ctrl+Alt+P" / "F8" / "MediaPlayPause" -> modifiers + vk
function Parse-Hotkey($spec) {
    $mods = 0
    $vk = 0
    foreach ($raw in (@([string]$spec) -split '\+')) {
        $p = $raw.Trim()
        if (-not $p) { continue }
        $low = $p.ToLower()
        if ($low -eq 'ctrl' -or $low -eq 'control') { $mods = $mods -bor 0x0002; continue }
        if ($low -eq 'alt') { $mods = $mods -bor 0x0001; continue }
        if ($low -eq 'shift') { $mods = $mods -bor 0x0004; continue }
        if ($low -eq 'win') { $mods = $mods -bor 0x0008; continue }
        $name = $p
        if ($name -match '^[0-9]$') { $name = "D$name" }
        elseif ($name.Length -eq 1) { $name = $name.ToUpper() }
        try {
            $k = [Enum]::Parse([System.Windows.Forms.Keys], $name, $true)
            $vk = [int]$k
        } catch {
            return @{ Ok = $false }
        }
    }
    if ($vk -eq 0) { return @{ Ok = $false } }
    return @{ Ok = $true; Mods = [uint32]$mods; Vk = [uint32]$vk }
}

function Format-Hotkey($mods, $vk) {
    $parts = @()
    if ($mods -band 0x0002) { $parts += 'Ctrl' }
    if ($mods -band 0x0001) { $parts += 'Alt' }
    if ($mods -band 0x0004) { $parts += 'Shift' }
    if ($mods -band 0x0008) { $parts += 'Win' }
    $name = ([System.Windows.Forms.Keys]$vk).ToString()
    if ($name -match '^D([0-9])$') { $name = $Matches[1] }
    $parts += $name
    return ($parts -join '+')
}

function Update-HotkeyMenu {
    $spec = [string]$script:settings.HotkeyPause
    $specS = [string]$script:settings.HotkeyScreen
    if ($script:miPause) {
        # 空字符串会让 WinForms 退回去显示 ShortcutKeys 的默认值 "None"，必须给 $null
        $script:miPause.ShortcutKeyDisplayString = if ($spec) { $spec } else { $null }
    }
    if ($script:miScreen) {
        $script:miScreen.ShortcutKeyDisplayString = if ($specS) { $specS } else { $null }
    }
    if ($script:miHotkeyEdit) {
        $script:miHotkeyEdit.Text = if ($spec) { "修改…（现在是 $spec）" } else { '修改…（当前未设置）' }
    }
    if ($script:miHotkeyOff) { $script:miHotkeyOff.Checked = (-not $spec) }
    if ($script:miHotkeyEditS) {
        $script:miHotkeyEditS.Text = if ($specS) { "修改…（现在是 $specS）" } else { '修改…（当前未设置）' }
    }
    if ($script:miHotkeyOffS) { $script:miHotkeyOffS.Checked = (-not $specS) }
    if ($script:wm -and $script:wm.Screen) {
        $script:wm.Screen.ShortcutKeyDisplayString = if ($specS) { $specS } else { $null }
    }
}

# 注册单个热键。返回是否成功，两个键互相独立，一个失败不影响另一个。
function Register-OneHotkey($id, $spec, $label) {
    if (-not $spec) { return $false }
    $p = Parse-Hotkey $spec
    if (-not $p.Ok) {
        Notify '快捷键写法不对' "$label「$spec」认不出来，右键 → 快捷键 重新设一个。" 'Warning'
        return $false
    }
    $ok = $script:hkWin.Register($id, $p.Mods, $p.Vk)
    if (-not $ok) {
        Notify '快捷键没注册上' "$label「$spec」被别的软件占用了，右键 → 快捷键 换一个。" 'Warning'
    }
    return $ok
}

function Register-Hotkeys {
    if (-not $script:hkAvailable -or -not $script:hkWin) { Update-HotkeyMenu; return }
    try { $script:hkWin.Unregister($script:hkId) } catch { }
    try { $script:hkWin.Unregister($script:hkScreenId) } catch { }
    $hp = ([string]$script:settings.HotkeyPause).Trim()
    $hs = ([string]$script:settings.HotkeyScreen).Trim()
    # 两个键设成同一个：RegisterHotKey 会互相顶掉，拒绝这种配置
    if ($hp -and $hs -and $hp -eq $hs) {
        $script:settings.HotkeyScreen = ''
        Save-Settings
        Notify '两个快捷键不能一样' "投屏键和暂停键都是「$hp」，投屏键已清空，请重新设置。" 'Warning'
        $hs = ''
    }
    $script:hkOk = Register-OneHotkey $script:hkId $hp '暂停键'
    [void](Register-OneHotkey $script:hkScreenId $hs '投屏键')
    Update-HotkeyMenu
}

# 录制窗口：按下即生效，Esc 取消。打开期间先注销，否则按到旧键会被系统直接吞掉。
function Show-HotkeyDialog($which) {
    # 录制时两个键都注销，免得按到另一个键时被系统吞掉
    try { $script:hkWin.Unregister($script:hkId) } catch { }
    try { $script:hkWin.Unregister($script:hkScreenId) } catch { }

    $script:hkResult = $null
    $form = New-Object System.Windows.Forms.Form
    $form.Text = "设置$which快捷键"
    $form.ClientSize = New-Object System.Drawing.Size(340, 152)
    $form.StartPosition = 'CenterScreen'
    $form.FormBorderStyle = 'FixedDialog'
    $form.MaximizeBox = $false
    $form.MinimizeBox = $false
    $form.TopMost = $true
    $form.KeyPreview = $true

    $lbl = New-Object System.Windows.Forms.Label
    $lbl.Text = "请按下要用的$which快捷键"
    $lbl.SetBounds(16, 14, 308, 20)
    $form.Controls.Add($lbl)

    $script:hkBig = New-Object System.Windows.Forms.Label
    $script:hkBig.Text = '（等待按键）'
    $script:hkBig.TextAlign = 'MiddleCenter'
    $script:hkBig.BorderStyle = 'FixedSingle'
    $script:hkBig.Font = New-Object System.Drawing.Font('Consolas', 15, [System.Drawing.FontStyle]::Bold)
    $script:hkBig.SetBounds(16, 40, 308, 46)
    $form.Controls.Add($script:hkBig)

    $tip = New-Object System.Windows.Forms.Label
    $tip.Text = 'Esc 取消。单键会全局占用，推荐用 F 键。'
    $tip.SetBounds(16, 94, 308, 20)
    $tip.ForeColor = [System.Drawing.Color]::DimGray
    $form.Controls.Add($tip)

    $form.Add_KeyDown({
        # 事件参数必须显式接住。之前直接写 $e，PowerShell 没给这个变量，
        # 读到的是 $null，KeyCode 变 0，最后存成了 "None"。
        param($sender, $keyArgs)
        $k = $keyArgs.KeyCode
        if ($k -eq [System.Windows.Forms.Keys]::Escape) {
            $script:hkResult = 'ESC'
            $sender.Close()
            return
        }
        # 光按修饰键不算，等真正的键
        if ($k -eq [System.Windows.Forms.Keys]::ControlKey -or $k -eq [System.Windows.Forms.Keys]::Menu -or
            $k -eq [System.Windows.Forms.Keys]::ShiftKey -or $k -eq [System.Windows.Forms.Keys]::LWin -or
            $k -eq [System.Windows.Forms.Keys]::RWin) { return }
        if ($k -eq [System.Windows.Forms.Keys]::None) {
            $script:hkBig.Text = '这个键不行，换一个'
            return
        }
        $mods = 0
        if ($keyArgs.Control) { $mods = $mods -bor 0x0002 }
        if ($keyArgs.Alt) { $mods = $mods -bor 0x0001 }
        if ($keyArgs.Shift) { $mods = $mods -bor 0x0004 }
        $txt = Format-Hotkey $mods ([int]$k)
        if (-not $txt -or $txt -eq 'None') {
            $script:hkBig.Text = '这个键不行，换一个'
            return
        }
        $script:hkResult = $txt
        $script:hkBig.Text = $txt
        $sender.Close()
    })

    [void]$form.ShowDialog()
    $form.Dispose()
    if (-not $script:hkResult -or $script:hkResult -eq 'ESC') {
        Register-Hotkeys
        return $null
    }
    return $script:hkResult
}

function Set-Hotkey($which, $spec) {
    $spec = ([string]$spec).Trim()
    # 空 = 禁用；非空必须能被解析出来，否则不写盘（防止再把 "None" 存进去）
    if ($spec -and -not (Parse-Hotkey $spec).Ok) {
        Notify '快捷键没保存' "「$spec」认不出来，换一个再试。" 'Warning'
        return
    }
    # 两个键不能一样
    $other = if ($which -eq 'screen') { [string]$script:settings.HotkeyPause } else { [string]$script:settings.HotkeyScreen }
    if ($spec -and $spec -eq $other.Trim()) {
        Notify '快捷键没保存' "和另一个快捷键重复，换一个再试。" 'Warning'
        return
    }
    if ($which -eq 'screen') { $script:settings.HotkeyScreen = $spec }
    else { $script:settings.HotkeyPause = $spec }
    Save-Settings
    Register-Hotkeys
}

# 发给手机媒体键。先看手机上有没有媒体会话，没有就不发，免得拉起来自带音乐 App。
function Send-MediaPause {
    $serial = $script:settings.Serial
    if (-not $serial) { $serial = Get-Serial }
    if (-not $serial) { Notify '没有设备' '先连上手机' 'Warning'; return }

    $dump = ''
    try { $dump = (& $script:adb -s $serial shell dumpsys media_session 2>$null | Out-String) } catch { $dump = '' }
    if ($dump -notmatch 'package=') {
        Notify '手机上没在播放' '当前没有可暂停的媒体。游戏一般也不响应这个键。' 'Info'
        return
    }
    try { & $script:adb -s $serial shell input keyevent 85 2>$null | Out-Null } catch { }
}

# 投屏开关：复用 Set-Screen，声音不受影响
function Invoke-ScreenToggle {
    $on = -not $script:settings.Screen
    Set-Screen $on
    if ($on) { Notify '投屏已开启' '手机画面已显示，声音正常。' 'Info' }
    else { Notify '投屏已关闭' '画面已停，声音继续转发。' 'Info' }
}

function Read-Names {
    $h = @{}
    if (Test-Path $script:namesFile) {
        try {
            $raw = [IO.File]::ReadAllText($script:namesFile, [Text.Encoding]::UTF8).TrimStart([char]0xFEFF)
            if ($raw.Trim()) {
                $o = $raw | ConvertFrom-Json
                foreach ($p in $o.PSObject.Properties) { if ($p.Value) { $h[$p.Name] = [string]$p.Value } }
            }
        } catch { }
    }
    return $h
}

function Save-Names($h) {
    try {
        if ($h.Count -eq 0) {
            if (Test-Path $script:namesFile) { Remove-Item $script:namesFile -Force }
        } else {
            ($h | ConvertTo-Json -Depth 3) | Out-File -FilePath $script:namesFile -Encoding utf8
        }
    } catch { }
}

# USB 的序列号是纯字母数字；无线一定是 IP:端口 或 mDNS 服务名
function Get-ConnKind($serial) {
    $s = [string]$serial
    if ($s -match '^[0-9.]+:[0-9]+$' -or $s -match '_adb-tls') { return 'WiFi' }
    return 'USB'
}

function Get-Devices {
    # adb 的 stderr（比如设备掉线）不能冒泡成致命错误，否则整个托盘会崩
    $ErrorActionPreference = 'Continue'
    $names = Read-Names
    $out = @()
    try { $out = @(& $script:adb devices -l 2>$null) } catch { $out = @() }
    $list = @()
    foreach ($line in $out) {
        if ($line -notmatch '\sdevice(\s|$)') { continue }
        $serial = ($line -split '\s+')[0]
        $model = ''
        if ($line -match 'model:(\S+)') { $model = $Matches[1] }
        $name = ''
        if ($names.ContainsKey($serial)) { $name = $names[$serial] }
        elseif ($model -and $names.ContainsKey($model)) { $name = $names[$model] }
        $list += [pscustomobject]@{ Serial = $serial; Model = $model; Name = $name; Kind = (Get-ConnKind $serial) }
    }
    return $list
}

function Get-Serial {
    $devs = @(Get-Devices)
    if ($devs.Count -eq 0) { return $null }
    # 用户明确选过的那台优先，其次 USB，再无线。
    # 老版本是无线优先，方向反了：Wi-Fi 会抖，USB 是直连。
    if ($script:settings.Serial) {
        $hit = $devs | Where-Object { $_.Serial -eq $script:settings.Serial } | Select-Object -First 1
        if ($hit) { return $hit.Serial }
    }
    $usb = $devs | Where-Object { $_.Kind -eq 'USB' } | Select-Object -First 1
    $pick = if ($usb) { $usb } else { $devs[0] }
    $script:settings.Serial = $pick.Serial
    return $pick.Serial
}

function Set-Device($serial) {
    $was = Restart-IfRunning
    $script:settings.Serial = $serial
    # 用户亲手选的这条，就别再被「插线自动切有线」抢走
    $script:manualSerial = $serial
    Update-State
    if ($was) { Start-Audio }
}

function Show-Input($prompt, $title, $default) {
    $form = New-Object System.Windows.Forms.Form
    $form.Text = $title
    $form.ClientSize = New-Object System.Drawing.Size(360, 132)
    $form.StartPosition = 'CenterScreen'
    $form.FormBorderStyle = 'FixedDialog'
    $form.MaximizeBox = $false
    $form.MinimizeBox = $false
    $form.TopMost = $true

    $lbl = New-Object System.Windows.Forms.Label
    $lbl.Text = $prompt
    $lbl.SetBounds(14, 12, 332, 40)
    $form.Controls.Add($lbl)

    $tb = New-Object System.Windows.Forms.TextBox
    $tb.Text = $default
    $tb.SetBounds(14, 58, 332, 24)
    $form.Controls.Add($tb)

    $ok = New-Object System.Windows.Forms.Button
    $ok.Text = '确定'
    $ok.SetBounds(180, 94, 78, 26)
    $ok.DialogResult = [System.Windows.Forms.DialogResult]::OK
    $form.Controls.Add($ok)
    $form.AcceptButton = $ok

    $cancel = New-Object System.Windows.Forms.Button
    $cancel.Text = '取消'
    $cancel.SetBounds(268, 94, 78, 26)
    $cancel.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
    $form.Controls.Add($cancel)
    $form.CancelButton = $cancel

    $r = $form.ShowDialog()
    $val = $tb.Text
    $form.Dispose()
    if ($r -eq [System.Windows.Forms.DialogResult]::OK) { return $val }
    return $null
}

function Show-TwoInput($title, $hint, $l1, $d1, $l2, $d2) {
    $form = New-Object System.Windows.Forms.Form
    $form.Text = $title
    $form.ClientSize = New-Object System.Drawing.Size(392, 186)
    $form.StartPosition = 'CenterScreen'
    $form.FormBorderStyle = 'FixedDialog'
    $form.MaximizeBox = $false
    $form.MinimizeBox = $false
    $form.TopMost = $true

    $lblHint = New-Object System.Windows.Forms.Label
    $lblHint.Text = $hint
    $lblHint.SetBounds(14, 10, 364, 42)
    $form.Controls.Add($lblHint)

    $la = New-Object System.Windows.Forms.Label
    $la.Text = $l1
    $la.SetBounds(14, 62, 80, 22)
    $form.Controls.Add($la)

    $ta = New-Object System.Windows.Forms.TextBox
    $ta.Text = $d1
    $ta.SetBounds(96, 60, 282, 24)
    $form.Controls.Add($ta)

    $lb = New-Object System.Windows.Forms.Label
    $lb.Text = $l2
    $lb.SetBounds(14, 96, 80, 22)
    $form.Controls.Add($lb)

    $tb = New-Object System.Windows.Forms.TextBox
    $tb.Text = $d2
    $tb.SetBounds(96, 94, 282, 24)
    $form.Controls.Add($tb)

    $ok = New-Object System.Windows.Forms.Button
    $ok.Text = '确定'
    $ok.SetBounds(212, 138, 78, 26)
    $ok.DialogResult = [System.Windows.Forms.DialogResult]::OK
    $form.Controls.Add($ok)
    $form.AcceptButton = $ok

    $cancel = New-Object System.Windows.Forms.Button
    $cancel.Text = '取消'
    $cancel.SetBounds(300, 138, 78, 26)
    $cancel.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
    $form.Controls.Add($cancel)
    $form.CancelButton = $cancel

    $r = $form.ShowDialog()
    $va = $ta.Text
    $vb = $tb.Text
    $form.Dispose()
    if ($r -eq [System.Windows.Forms.DialogResult]::OK) { return @{ A = $va; B = $vb } }
    return $null
}

# ---------------- 息屏覆盖层 ----------------
# 手机睡下去后投屏画面会停在最后一帧，用户看不出已经断了。
# 托盘通知容易被忽略，所以直接在投屏画面原位盖一层黑屏，写清楚状态并给一个唤醒按钮。
$script:sleepForm = $null
$script:sleepLabel = $null
$script:sleepSub = $null
$script:sleepBtn = $null
$script:sleepLayout = $null
$script:sleepLastRect = $null

# 需要拿到 scrcpy 窗口在屏幕上的位置，才能把黑屏精确贴在画面上。
# Add-Type 编译要一秒左右，延后到真正投屏时再做，不拖慢托盘启动。
$script:hasWin32 = $false
$script:win32Tried = $false

function Ensure-Win32 {
    if ($script:win32Tried) { return $script:hasWin32 }
    $script:win32Tried = $true
    try {
        Add-Type -Namespace TrayWin32 -Name Native -MemberDefinition @'
[StructLayout(LayoutKind.Sequential)]
public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
[StructLayout(LayoutKind.Sequential)]
public struct POINT { public int X; public int Y; }
[DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
[DllImport("user32.dll", SetLastError=true)] public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
[DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
[DllImport("user32.dll", SetLastError=true)] public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
[DllImport("user32.dll", SetLastError=true)] public static extern IntPtr GetParent(IntPtr hWnd);
[DllImport("user32.dll", EntryPoint="GetWindowLongPtrW", SetLastError=true)] public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
[DllImport("user32.dll", EntryPoint="SetWindowLongPtrW", SetLastError=true)] public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
[DllImport("user32.dll", SetLastError=true)] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
[DllImport("user32.dll", SetLastError=true)] public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
[DllImport("user32.dll")] public static extern bool ReleaseCapture();
[DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
[DllImport("user32.dll")] public static extern IntPtr SetFocus(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
'@ -ErrorAction Stop
        $script:hasWin32 = $true
        # DPI 不感知时拿到的是缩放后的逻辑坐标，嵌入的画面会错位。必须在建窗口前就设。
        try { [void][TrayWin32.Native]::SetProcessDPIAware() } catch { }
    } catch { $script:hasWin32 = $false }
    return $script:hasWin32
}

# ---------------- 无边框窗体（标题与菜单同一行） ----------------
# 标准窗体的标题栏是系统画的非客户区，菜单只能放它下面，必然是两行。
# 要并成一行，只能去掉系统标题栏，缩放行为自己补：WM_NCHITTEST 返回各方向的命中值。
$script:uiClassesOk = $false
$script:uiClassesTried = $false

function Ensure-UiClasses {
    if ($script:uiClassesTried) { return $script:uiClassesOk }
    $script:uiClassesTried = $true
    try {
        Add-Type -ReferencedAssemblies System.Windows.Forms, System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Windows.Forms;

public class BorderlessForm : Form
{
    private const int WM_NCHITTEST = 0x0084;
    private const int WM_NCCALCSIZE = 0x0083;
    private const int WM_EXITSIZEMOVE = 0x0232;
    public int GripSize = 12;
    // 用户拖动窗口结束后触发（程序改尺寸不会触发，正好用来记忆尺寸）
    public event EventHandler ResizeEnded;

    public BorderlessForm()
    {
        // 必须保留 Sizable：它会带上 WS_THICKFRAME。
        // 没有这个样式，系统根本不会执行我们返回的 HTLEFT / HTTOP 缩放，
        // 边缘就是死的，怎么拖都没反应。边框和标题栏靠下面的 WM_NCCALCSIZE 去掉。
        this.FormBorderStyle = FormBorderStyle.Sizable;
        this.SetStyle(ControlStyles.ResizeRedraw, true);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_EXITSIZEMOVE)
        {
            EventHandler h = this.ResizeEnded;
            if (h != null) { h(this, EventArgs.Empty); }
        }
        if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
        {
            // 让客户区铺满整个窗口：标题栏、边框都不画，看上去就是无边框
            m.Result = IntPtr.Zero;
            return;
        }

        // 无边框窗口没有非客户区，命中结果自己算。
        // 不先调 base 再判断它的返回值——WinForms 内部可能把它改成别的值，
        // 那样边缘就永远判不到。
        if (m.Msg == WM_NCHITTEST && this.WindowState == FormWindowState.Normal)
        {
            int lp = m.LParam.ToInt32();
            int sx = (short)(lp & 0xFFFF);
            int sy = (short)((lp >> 16) & 0xFFFF);
            Point p = this.PointToClient(new Point(sx, sy));

            int g = this.GripSize;
            if (g < 6) g = 6;
            bool left = p.X < g;
            bool right = p.X >= this.ClientSize.Width - g;
            bool top = p.Y < g;
            bool bottom = p.Y >= this.ClientSize.Height - g;

            int r = 1;                     // HTCLIENT
            if (top && left) r = 13;       // HTTOPLEFT
            else if (top && right) r = 14; // HTTOPRIGHT
            else if (bottom && left) r = 16;
            else if (bottom && right) r = 17;
            else if (left) r = 10;
            else if (right) r = 11;
            else if (top) r = 12;
            else if (bottom) r = 15;

            m.Result = (IntPtr)r;
            return;
        }
        base.WndProc(ref m);
    }
}

public class DarkColorTable : ProfessionalColorTable
{
    private static Color BG = Color.FromArgb(32, 32, 38);
    private static Color DROP = Color.FromArgb(40, 40, 48);
    private static Color HOVER = Color.FromArgb(62, 62, 74);
    private static Color LINE = Color.FromArgb(56, 56, 66);

    public override Color ToolStripDropDownBackground { get { return DROP; } }
    public override Color ImageMarginGradientBegin { get { return DROP; } }
    public override Color ImageMarginGradientMiddle { get { return DROP; } }
    public override Color ImageMarginGradientEnd { get { return DROP; } }
    public override Color MenuBorder { get { return LINE; } }
    public override Color MenuItemBorder { get { return HOVER; } }
    public override Color MenuItemSelected { get { return HOVER; } }
    public override Color MenuItemSelectedGradientBegin { get { return HOVER; } }
    public override Color MenuItemSelectedGradientEnd { get { return HOVER; } }
    public override Color MenuItemPressedGradientBegin { get { return BG; } }
    public override Color MenuItemPressedGradientEnd { get { return BG; } }
    public override Color ToolStripGradientBegin { get { return BG; } }
    public override Color ToolStripGradientMiddle { get { return BG; } }
    public override Color ToolStripGradientEnd { get { return BG; } }
    public override Color SeparatorDark { get { return LINE; } }
    public override Color SeparatorLight { get { return LINE; } }
}

public class DarkRenderer : ToolStripProfessionalRenderer
{
    public DarkRenderer() : base(new DarkColorTable()) { }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (!e.Item.Enabled) e.TextColor = Color.FromArgb(108, 108, 120);
        else e.TextColor = Color.FromArgb(230, 230, 236);
        base.OnRenderItemText(e);
    }
}
'@ -ErrorAction Stop
        $script:uiClassesOk = $true
    } catch {
        $script:uiClassesOk = $false
    }
    return $script:uiClassesOk
}

function Get-ScrcpyRect {
    if (-not (Ensure-Win32)) { return $null }
    if (-not (Is-Running)) { return $null }
    try {
        $script:proc.Refresh()
        $h = $script:proc.MainWindowHandle
        if ($h -eq [IntPtr]::Zero) { return $null }
        $r = [TrayWin32.Native+RECT]::new()
        if (-not [TrayWin32.Native]::GetClientRect($h, [ref]$r)) { return $null }
        $p = [TrayWin32.Native+POINT]::new()
        $p.X = 0; $p.Y = 0
        if (-not [TrayWin32.Native]::ClientToScreen($h, [ref]$p)) { return $null }
        $w = $r.Right - $r.Left
        $h2 = $r.Bottom - $r.Top
        if ($w -le 0 -or $h2 -le 0) { return $null }
        return @{ X = $p.X; Y = $p.Y; W = $w; H = $h2 }
    } catch { return $null }
}

function Ensure-SleepForm {
    if ($script:sleepForm) { return }
    $f = New-Object System.Windows.Forms.Form
    $f.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::None
    $f.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
    $f.BackColor = [System.Drawing.Color]::FromArgb(10, 10, 12)
    $f.TopMost = $true
    $f.ShowInTaskbar = $false

    $title = New-Object System.Windows.Forms.Label
    $title.Text = '手机已息屏'
    $title.ForeColor = [System.Drawing.Color]::FromArgb(235, 235, 240)
    $title.Font = New-Object System.Drawing.Font('Microsoft YaHei UI', 20, [System.Drawing.FontStyle]::Bold)
    $title.TextAlign = [System.Drawing.ContentAlignment]::MiddleCenter
    $title.AutoSize = $false

    $sub = New-Object System.Windows.Forms.Label
    $sub.Text = '投屏画面已停止更新'
    $sub.ForeColor = [System.Drawing.Color]::FromArgb(140, 140, 150)
    $sub.Font = New-Object System.Drawing.Font('Microsoft YaHei UI', 9)
    $sub.TextAlign = [System.Drawing.ContentAlignment]::MiddleCenter
    $sub.AutoSize = $false

    $btn = New-Object System.Windows.Forms.Button
    $btn.Text = '唤醒并解锁'
    $btn.FlatStyle = [System.Windows.Forms.FlatStyle]::Flat
    $btn.BackColor = [System.Drawing.Color]::FromArgb(0, 120, 215)
    $btn.ForeColor = [System.Drawing.Color]::White
    $btn.FlatAppearance.BorderSize = 0
    $btn.Font = New-Object System.Drawing.Font('Microsoft YaHei UI', 10)
    $btn.Cursor = [System.Windows.Forms.Cursors]::Hand
    $btn.Add_Click({
        Hide-SleepOverlay
        try {
            $r = Wake-Unlock
            if ($r.Ok) { Notify '已唤醒' $r.Msg 'Info' } else { Notify '没唤醒' $r.Msg 'Warning' }
        } catch { }
    })

    $f.Controls.Add($title)
    $f.Controls.Add($sub)
    $f.Controls.Add($btn)

    # 覆盖层尺寸跟着投屏窗口走，控件按比例重排
    $layout = {
        if (-not $script:sleepForm) { return }
        $w = $script:sleepForm.ClientSize.Width
        $h = $script:sleepForm.ClientSize.Height
        $btnW = [Math]::Min(220, [Math]::Max(120, [int]($w * 0.55)))
        $btnH = 42
        $cx = [int]($w / 2)
        $cy = [int]($h / 2)
        $script:sleepLabel.SetBounds(0, $cy - 70, $w, 44)
        $script:sleepSub.SetBounds(0, $cy - 22, $w, 24)
        $script:sleepBtn.SetBounds($cx - [int]($btnW / 2), $cy + 20, $btnW, $btnH)
    }
    $f.Add_Resize($layout)

    $script:sleepForm = $f
    $script:sleepLabel = $title
    $script:sleepSub = $sub
    $script:sleepBtn = $btn
    $script:sleepLayout = $layout
}

# ---------------- 主窗口：单行顶栏 + 画面嵌入 ----------------
# 系统标题栏是非客户区，菜单只能排在它下面，必然是两行。
# 所以窗口不画系统边框，顶栏自己做：左边标题、中间菜单、右边三个窗口按钮，一行。
$script:appVersion = '1.3.0'
$script:mainForm = $null
$script:topBar = $null
$script:contentPanel = $null
$script:mainMenuBar = $null
$script:titleLabel = $null
$script:statusBar = $null
$script:btnMin = $null
$script:btnMax = $null
$script:btnClose = $null
$script:sleepInner = $null
$script:sleepInnerTitle = $null
$script:sleepInnerSub = $null
$script:sleepInnerBtn = $null
$script:sleepInnerLayout = $null
$script:reallyExit = $false
$script:showWindowWanted = $true
$script:devMenuParent = $null
$script:autoSized = $false
$script:wm = @{}
$script:wmFps = @{}
$script:wmBuf = @{}
$script:wmCodec = @{}
$script:embedHwnd = [IntPtr]::Zero
$script:embedStyle = $null
$script:embedded = $false
$script:attachTimer = $null
$script:attachLeft = 0
$script:fullScreen = $false
# 画面四周留一圈给鼠标抓窗口边缘。压到最窄，正常看几乎看不见
$script:embedInset = [int](4 * $script:S)
# 手机画面的宽高比，用来把窗口宽度收到贴着画面
$script:videoRatio = 0
# 视频流方向轮询节流
$script:orientTick = 0
# 顶栏的设计高度，布局时只认这个值；底部没有状态条了
$script:topBarH = [int](38 * $script:S)
$script:statusH = 0

$script:barBg = [System.Drawing.Color]::FromArgb(32, 32, 38)
$script:barHover = [System.Drawing.Color]::FromArgb(56, 56, 68)
$script:barBar = [System.Drawing.Color]::FromArgb(226, 226, 232)

function New-BarButton($glyph) {
    $b = New-Object System.Windows.Forms.Label
    $b.Text = $glyph
    $b.TextAlign = [System.Drawing.ContentAlignment]::MiddleCenter
    $b.Font = New-Object System.Drawing.Font('Segoe MDL2 Assets', 9)
    $b.ForeColor = $script:barBar
    $b.BackColor = $script:barBg
    $b.Cursor = [System.Windows.Forms.Cursors]::Hand
    $b.Add_MouseEnter({ param($s, $e)
        if ($s -eq $script:btnClose) { $s.BackColor = [System.Drawing.Color]::FromArgb(196, 43, 28) }
        else { $s.BackColor = $script:barHover }
    })
    $b.Add_MouseLeave({ param($s, $e) $s.BackColor = $script:barBg })
    return $b
}

# 三块区域自己摆，不用 Dock。
# Dock 的实际处理顺序跟控件加入顺序相反，Fill 的内容区会先把整块吃掉，
# 顶栏和状态条就只是盖在它上面，画面从窗口最顶端开始画，顶部被顶栏遮住。
# 注意：高度必须用下面这两个常量，不能读控件当前的 Height —— 一旦某次设成 0 就再也回不来。
function Update-MainLayout {
    if (-not $script:mainForm) { return }
    $cs = $script:mainForm.ClientSize
    if ($cs.Width -le 0 -or $cs.Height -le 0) { return }
    $topH = $script:topBarH
    $botH = 0
    if (-not $script:fullScreen) { $botH = $script:statusH }
    # 四边留出一条缝给窗口自己。缝里没有任何子控件，鼠标才会落到窗口上，
    # 边缘的拖拽判断才有机会执行——这一点是之前拖不动的真正原因：
    # 子控件铺满整个客户区时，系统把鼠标消息直接给了子控件，窗口收不到。
    $inset = 0
    if (-not $script:fullScreen) { $inset = $script:embedInset }
    # 顶栏和状态条占满整宽（菜单和按钮要有地方放）；
    # 内容区四边各缩进一条缝，缝里没有子控件，鼠标落在缝上才归窗口管，
    # 边缘的拖拽判断才生效——子控件铺满整个客户区时，系统把消息直接给了子控件。
    if ($script:topBar) { $script:topBar.SetBounds(0, 0, $cs.Width, $topH) }
    if ($script:statusBar) { $script:statusBar.SetBounds(0, $cs.Height - $botH, $cs.Width, $botH) }
    $ch = [Math]::Max(0, $cs.Height - $topH - $botH - 2 * $inset)
    if ($script:contentPanel) { $script:contentPanel.SetBounds($inset, $topH + $inset, [Math]::Max(0, $cs.Width - 2 * $inset), $ch) }
}

function Update-BarLayout {
    if (-not $script:mainForm) { return }
    $tb = $script:topBar
    $w = $tb.Width
    $h = $tb.Height
    $S = $script:S
    # 右边只有最小化和关闭
    $bw = [int](44 * $S)
    $x = $w - $bw
    foreach ($b in @($script:btnClose, $script:btnMin)) {
        if ($b) { $b.SetBounds($x, 0, $bw, $h); $x -= $bw }
    }
    # 标题不显示了，「操作」直接贴左边
    if ($script:titleLabel) { $script:titleLabel.Visible = $false }
    $pad = [int](10 * $S)
    $mh = $script:mainMenuBar.Height
    if ($mh -le 0) { $mh = [int](24 * $S) }
    $script:mainMenuBar.Location = New-Object System.Drawing.Point($pad, [int](($h - $mh) / 2))
}

function Set-FullScreen($on) {
    if (-not $script:mainForm) { return }
    $script:fullScreen = [bool]$on
    try {
        # 顶栏一直留着：全屏后总得有地方点「退出全屏」。
        # 之前连顶栏一起藏了，焦点又在画面里，结果退不出来。
        $script:topBar.Visible = $true
        if ($script:statusBar) { $script:statusBar.Visible = -not $on }
        $script:mainForm.WindowState = if ($on) { 'Maximized' } else { 'Normal' }
        if ($script:wm -and $script:wm.Full) {
            $script:wm.Full.Text = if ($on) { '退出全屏' } else { '全屏' }
        }
        Update-MainLayout
    } catch { }
    Sync-EmbedSize
}

function Sync-EmbedSize {
    if (-not $script:embedded) { return }
    if ($script:embedHwnd -eq [IntPtr]::Zero) { return }
    if (-not $script:contentPanel) { return }
    try {
        # 内容区本身已经四边留了缝，画面直接铺满它就行
        $w = $script:contentPanel.ClientSize.Width
        $h = $script:contentPanel.ClientSize.Height
        if ($w -le 0 -or $h -le 0) { return }
        # SWP_NOZORDER | SWP_NOACTIVATE
        [void][TrayWin32.Native]::SetWindowPos($script:embedHwnd, [IntPtr]::Zero, 0, 0, $w, $h, 0x0014)
    } catch { }
}

function Build-MainMenu {
    $bar = $script:mainMenuBar
    if (-not $bar) { return }
    $bar.Items.Clear()
    $script:wm = @{}
    $script:wmFps = @{}
    $script:wmBuf = @{}
    $script:wmCodec = @{}

    # ---- 设备 ----
    $mDev = New-Object System.Windows.Forms.ToolStripMenuItem
    $mDev.Text = '设备(&D)'
    $mDev.Add_DropDownOpening({ Update-DeviceMenu $script:wm.Device })
    $script:wm.Device = $mDev

    # ---- 音频 ----
    $mAudio = New-Object System.Windows.Forms.ToolStripMenuItem
    $mAudio.Text = '音频(&A)'

    $wStart = New-Object System.Windows.Forms.ToolStripMenuItem
    $wStart.Text = '启动转发'
    $wStart.Add_Click({
        $script:userStopped = $false
        if (-not (Start-Audio)) { Notify '没有设备' '先连手机：设备 → 重新连接' 'Warning' }
    })
    $mAudio.DropDownItems.Add($wStart) | Out-Null
    $script:wm.Start = $wStart

    $wStop = New-Object System.Windows.Forms.ToolStripMenuItem
    $wStop.Text = '停止'
    $wStop.Add_Click({
        $script:userStopped = $true
        $script:retryLeft = 0
        if ($script:retryTimer) { $script:retryTimer.Stop() }
        Stop-Audio
        Hide-MainWindow
    })
    $mAudio.DropDownItems.Add($wStop) | Out-Null
    $script:wm.Stop = $wStop

    $mAudio.DropDownItems.Add((New-Object System.Windows.Forms.ToolStripSeparator)) | Out-Null

    $wPause = New-Object System.Windows.Forms.ToolStripMenuItem
    $wPause.Text = '暂停 / 继续（手机）'
    $wPause.Add_Click({ Send-MediaPause })
    $mAudio.DropDownItems.Add($wPause) | Out-Null
    $script:wm.Pause = $wPause

    $mAudio.DropDownItems.Add((New-Object System.Windows.Forms.ToolStripSeparator)) | Out-Null

    $wDelay = New-Object System.Windows.Forms.ToolStripMenuItem
    $wDelay.Text = '缓冲 / 延迟'
    foreach ($pair in @(@(40, '低延迟  40ms'), @(60, '标准  60ms'), @(100, '稳定  100ms'))) {
        $msVal = $pair[0]; $label = $pair[1]
        $mi = New-Object System.Windows.Forms.ToolStripMenuItem
        $mi.Text = $label
        $mi.Tag = $msVal
        $mi.Add_Click({ Set-Buffer ([int]$this.Tag) })
        $wDelay.DropDownItems.Add($mi) | Out-Null
        $script:wmBuf[$msVal] = $mi
    }
    $mAudio.DropDownItems.Add($wDelay) | Out-Null

    $wCodec = New-Object System.Windows.Forms.ToolStripMenuItem
    $wCodec.Text = '音频格式'
    foreach ($pair in @(@('raw', '无损 PCM（延迟最低）'), @('opus', 'Opus（抗抖动，省带宽）'))) {
        $code = $pair[0]; $label = $pair[1]
        $mi = New-Object System.Windows.Forms.ToolStripMenuItem
        $mi.Text = $label
        $mi.Tag = $code
        $mi.Add_Click({ Set-Codec ([string]$this.Tag) })
        $wCodec.DropDownItems.Add($mi) | Out-Null
        $script:wmCodec[$code] = $mi
    }
    $mAudio.DropDownItems.Add($wCodec) | Out-Null

    # ---- 视图 ----
    $mView = New-Object System.Windows.Forms.ToolStripMenuItem
    $mView.Text = '视图(&V)'

    $wOnTop = New-Object System.Windows.Forms.ToolStripMenuItem
    $wOnTop.Text = '窗口置顶'
    $wOnTop.Add_Click({ Set-OnTop (-not $script:settings.OnTop) })
    $mView.DropDownItems.Add($wOnTop) | Out-Null
    $script:wm.OnTop = $wOnTop

    $wFps = New-Object System.Windows.Forms.ToolStripMenuItem
    $wFps.Text = '帧率'
    foreach ($pair in @(@(30, '30 帧（省电）'), @(60, '60 帧（推荐）'), @(90, '90 帧'), @(120, '120 帧'), @(144, '144 帧'), @(0, '不限（跟手机刷新率）'))) {
        $f = $pair[0]; $label = $pair[1]
        $mi = New-Object System.Windows.Forms.ToolStripMenuItem
        $mi.Text = $label
        $mi.Tag = $f
        $mi.Add_Click({ Set-MaxFps ([int]$this.Tag) })
        $wFps.DropDownItems.Add($mi) | Out-Null
        $script:wmFps[$f] = $mi
    }
    $mView.DropDownItems.Add($wFps) | Out-Null
    $script:wm.Fps = $wFps

    $wScreen = New-Object System.Windows.Forms.ToolStripMenuItem
    $wScreen.Text = '显示手机画面（投屏）'
    $wScreen.Add_Click({ Set-Screen (-not $script:settings.Screen) })
    $mView.DropDownItems.Add($wScreen) | Out-Null
    $script:wm.Screen = $wScreen

    # 窗口模式：切到原生后本窗口会关掉，改从托盘菜单切回来
    $wMode = New-Object System.Windows.Forms.ToolStripMenuItem
    $wMode.Text = '窗口模式'
    $wEmb = New-Object System.Windows.Forms.ToolStripMenuItem
    $wEmb.Text = '程序窗口'
    $wEmb.Add_Click({ Set-WindowMode 'embedded' })
    $wNat = New-Object System.Windows.Forms.ToolStripMenuItem
    $wNat.Text = 'scrcpy 原生窗口（切换后本窗口关闭）'
    $wNat.Add_Click({ Set-WindowMode 'native' })
    $wMode.DropDownItems.Add($wEmb) | Out-Null
    $wMode.DropDownItems.Add($wNat) | Out-Null
    $mView.DropDownItems.Add($wMode) | Out-Null
    $script:wm.ModeEmbedded = $wEmb
    $script:wm.ModeNative = $wNat

    $mView.DropDownItems.Add((New-Object System.Windows.Forms.ToolStripSeparator)) | Out-Null

    $wFull = New-Object System.Windows.Forms.ToolStripMenuItem
    $wFull.Text = '全屏'
    $wFull.ShortcutKeyDisplayString = 'F11'
    $wFull.Add_Click({ Set-FullScreen (-not $script:fullScreen) })
    $mView.DropDownItems.Add($wFull) | Out-Null
    $script:wm.Full = $wFull

    # ---- 帮助 ----
    $mHelp = New-Object System.Windows.Forms.ToolStripMenuItem
    $mHelp.Text = '帮助(&H)'

    $wKeys = New-Object System.Windows.Forms.ToolStripMenuItem
    $wKeys.Text = '快捷键说明'
    $wKeys.Add_Click({
        $k = if ($script:settings.HotkeyPause) { $script:settings.HotkeyPause } else { '（未设置）' }
        $s = if ($script:settings.HotkeyScreen) { $script:settings.HotkeyScreen } else { '（未设置）' }
        [System.Windows.Forms.MessageBox]::Show(
            "暂停 / 继续手机的媒体：$k`n投屏开 / 关：$s`n`n两项都能在托盘菜单「快捷键」里修改、禁用、恢复默认。`n`n注意：游戏不响应媒体暂停键，只有视频、音乐这类播放器认。",
            '快捷键说明') | Out-Null
    })
    $mHelp.DropDownItems.Add($wKeys) | Out-Null

    $wLog = New-Object System.Windows.Forms.ToolStripMenuItem
    $wLog.Text = '查看日志'
    $wLog.Add_Click({
        if (Test-Path $logFile) { Start-Process notepad.exe $logFile }
        else { [System.Windows.Forms.MessageBox]::Show('暂无日志', '日志') | Out-Null }
    })
    $mHelp.DropDownItems.Add($wLog) | Out-Null

    $wReadme = New-Object System.Windows.Forms.ToolStripMenuItem
    $wReadme.Text = '打开使用说明'
    $wReadme.Add_Click({
        $r = Join-Path $PSScriptRoot 'README.txt'
        if (Test-Path $r) { Start-Process notepad.exe $r }
        else { [System.Windows.Forms.MessageBox]::Show('没找到 README.txt', '使用说明') | Out-Null }
    })
    $mHelp.DropDownItems.Add($wReadme) | Out-Null

    # ---- 关于 ----
    $mAbout = New-Object System.Windows.Forms.ToolStripMenuItem
    $mAbout.Text = '关于(&B)'
    $mAbout.Add_Click({
        [System.Windows.Forms.MessageBox]::Show(
            "手机音频 $($script:appVersion)`n`n手机声音与画面无线转发到电脑。`n基于 scrcpy。",
            '关于') | Out-Null
    })
    # 顶栏只留一个入口，其余功能都从这里展开，给画面省宽度
    $mRoot = New-Object System.Windows.Forms.ToolStripMenuItem
    $mRoot.Text = '操作'
    foreach ($mm in @($mDev, $mAudio, $mView, $mHelp, $mAbout)) { $mRoot.DropDownItems.Add($mm) | Out-Null }
    $bar.Items.Add($mRoot) | Out-Null
}

# 窗口菜单的勾选 / 可用状态跟托盘菜单保持一致，避免两处说法不一样
function Sync-WindowMenu {
    if (-not $script:mainForm) { return }
    $w = $script:wm
    if (-not $w -or $w.Count -eq 0) { return }
    try {
        if ($w.Start) { $w.Start.Enabled = $script:miStart.Enabled }
        if ($w.Stop) { $w.Stop.Enabled = $script:miStop.Enabled }
        if ($w.Screen) { $w.Screen.Checked = [bool]$script:settings.Screen }
        if ($w.ModeEmbedded) { $w.ModeEmbedded.Checked = -not (Is-NativeMode) }
        if ($w.ModeNative) { $w.ModeNative.Checked = Is-NativeMode }
        if ($w.OnTop) {
            $w.OnTop.Checked = [bool]$script:settings.OnTop
            $w.OnTop.Enabled = [bool]$script:settings.Screen
        }
        if ($w.Fps) { $w.Fps.Enabled = [bool]$script:settings.Screen }
        foreach ($k in $script:wmFps.Keys) { $script:wmFps[$k].Checked = ($k -eq $script:settings.MaxFps) }
        foreach ($k in $script:wmBuf.Keys) { $script:wmBuf[$k].Checked = ($k -eq $script:settings.Buffer) }
        foreach ($k in $script:wmCodec.Keys) { $script:wmCodec[$k].Checked = ($k -eq $script:settings.Codec) }
        if ($w.Pause -and $script:miPause) { $w.Pause.ShortcutKeyDisplayString = $script:miPause.ShortcutKeyDisplayString }

        # 顶栏不显示设备名和地址，窗口标题固定
        if ($script:mainForm -and $script:mainForm.Text -ne '手机音频') { $script:mainForm.Text = '手机音频' }
        if ($script:statusBar) {
            $fmt = if ($script:settings.Codec -eq 'raw') { '无损' } else { 'Opus' }
            $fps = if ($script:settings.MaxFps -gt 0) { "$($script:settings.MaxFps)fps" } else { '不限帧' }
            $mode = if ($script:settings.Screen) { '投屏' } else { '纯音频' }
            $newStatus = if (Is-Running) {
                $state = if ($script:deviceAsleep) { '画面已冻结' } else { '运行中' }
                "$state   ·   $mode   ·   $fmt $($script:settings.Buffer)ms   ·   $fps"
            } else {
                "已停止   ·   $mode   ·   $fmt $($script:settings.Buffer)ms"
            }
            if ($script:statusBar.Text -ne $newStatus) { $script:statusBar.Text = $newStatus }
        }
    } catch { }
}

function Ensure-MainForm {
    if ($script:mainForm) { return $true }
    if (-not (Ensure-UiClasses)) { return $false }
    if (-not (Ensure-Win32)) { return $false }
    try {
        $S = $script:S
        $f = New-Object BorderlessForm
        $f.Text = '手机音频'
        $f.StartPosition = [System.Windows.Forms.FormStartPosition]::CenterScreen
        # 尺寸按 DPI 缩放自己算，不让 WinForms 再自动缩放一遍，否则两套缩放下布局会错位
        $f.AutoScaleMode = [System.Windows.Forms.AutoScaleMode]::None
        # 高度不超过屏幕可用高度，免得在小屏笔记本上一开就顶到任务栏外面
        $wa = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
        $initH = [Math]::Min([int](900 * $S), [int]($wa.Height * 0.92))
        $f.Size = New-Object System.Drawing.Size([int](620 * $S), $initH)
        $f.MinimumSize = New-Object System.Drawing.Size([int](400 * $S), [int](480 * $S))
        $f.BackColor = [System.Drawing.Color]::FromArgb(18, 18, 22)
        $f.ShowIcon = $false
        $f.TopMost = [bool]$script:settings.OnTop
        # 可拖的边缘跟内容区留的缝一样宽
        try { $f.GripSize = [int](10 * $S) } catch { }
        # 无边框窗口最大化会盖住任务栏，限制在工作区内
        try { $f.MaximizedBounds = [System.Windows.Forms.Screen]::FromControl($f).WorkingArea } catch { }

        # ---- 顶栏 ----
        $tb = New-Object System.Windows.Forms.Panel
        $tb.Dock = [System.Windows.Forms.DockStyle]::None
        $tb.Height = $script:topBarH
        $tb.BackColor = $script:barBg
        $f.Controls.Add($tb)

        $title = New-Object System.Windows.Forms.Label
        $title.Text = '手机音频'
        $title.ForeColor = [System.Drawing.Color]::FromArgb(240, 240, 246)
        $title.Font = New-Object System.Drawing.Font('Microsoft YaHei UI', 9.5, [System.Drawing.FontStyle]::Regular)
        $title.TextAlign = [System.Drawing.ContentAlignment]::MiddleLeft
        $title.AutoEllipsis = $true
        $tb.Controls.Add($title)

        $ms = New-Object System.Windows.Forms.MenuStrip
        $ms.Dock = [System.Windows.Forms.DockStyle]::None
        $ms.AutoSize = $true
        $ms.GripStyle = [System.Windows.Forms.ToolStripGripStyle]::Hidden
        $ms.Padding = New-Object System.Windows.Forms.Padding(0)
        $ms.BackColor = $script:barBg
        $ms.ForeColor = $script:barBar
        $ms.Font = New-Object System.Drawing.Font('Microsoft YaHei UI', 9)
        $ms.Renderer = New-Object DarkRenderer
        $tb.Controls.Add($ms)

        $btnMin = New-BarButton ([string][char]0xE921)
        $btnClose = New-BarButton ([string][char]0xE8BB)
        foreach ($b in @($btnMin, $btnClose)) { $tb.Controls.Add($b) }

        $btnMin.Add_Click({ $script:mainForm.WindowState = 'Minimized' })
        $btnClose.Add_Click({
            Stop-Audio
            Hide-MainWindow
        })

        # 拖顶栏 = 拖窗口；双击 = 最大化/还原。走系统的 HTCAPTION，顺带获得贴边分屏。
        $drag = {
            param($s, $e)
            if ($e.Button -ne [System.Windows.Forms.MouseButtons]::Left) { return }
            if ($script:mainForm.WindowState -eq 'Maximized') { return }
            [void][TrayWin32.Native]::ReleaseCapture()
            [void][TrayWin32.Native]::SendMessage($script:mainForm.Handle, 0x00A1, [IntPtr]2, [IntPtr]::Zero)
        }
        $dbl = {
            param($s, $e)
            if ($script:mainForm.WindowState -eq 'Maximized') { $script:mainForm.WindowState = 'Normal' }
            else { $script:mainForm.WindowState = 'Maximized' }
        }
        $tb.Add_MouseDown($drag)
        $title.Add_MouseDown($drag)
        $tb.Add_MouseDoubleClick($dbl)
        $title.Add_MouseDoubleClick($dbl)
        $tb.Add_Resize({ Update-BarLayout })
        $title.Add_TextChanged({ Update-BarLayout })

        # ---- 内容区 ----
        $cp = New-Object System.Windows.Forms.Panel
        $cp.Dock = [System.Windows.Forms.DockStyle]::None
        # 深灰而不是纯黑：画面四周留一圈，鼠标才抓得住窗口边缘去拖大小
        $cp.BackColor = [System.Drawing.Color]::FromArgb(26, 26, 32)
        $f.Controls.Add($cp)
        $cp.Add_Resize({ Sync-EmbedSize })

        # 底部不再放状态条，画面直接到底
        $sb = $null

        # 内容区永远在最底层，顶栏压在它上面，避免画面盖住菜单
        $cp.SendToBack()
        $tb.BringToFront()

        $f.Add_Resize({
            Update-MainLayout
            Update-BarLayout
            if ($script:mainForm.WindowState -eq 'Maximized') {
                try { $script:mainForm.MaximizedBounds = [System.Windows.Forms.Screen]::FromControl($script:mainForm).WorkingArea } catch { }
            }
        })
        # 用户拖完窗口才记尺寸；程序自己调尺寸不会触发 WM_EXITSIZEMOVE
        try { $f.add_ResizeEnded({ Remember-CurrentSize }) } catch { }
        # F11 全屏，全屏时 Esc 退出
        $f.KeyPreview = $true
        $f.Add_KeyDown({
            param($s, $e)
            if ($e.KeyCode -eq [System.Windows.Forms.Keys]::F11) {
                Set-FullScreen (-not $script:fullScreen)
                $e.Handled = $true
            } elseif ($e.KeyCode -eq [System.Windows.Forms.Keys]::Escape -and $script:fullScreen) {
                Set-FullScreen $false
                $e.Handled = $true
            }
        })
        # 关窗口 = 停止投屏，托盘留在右下角
        $f.Add_FormClosing({
            param($s, $e)
            if (-not $script:reallyExit) {
                $e.Cancel = $true
                Stop-Audio
                Hide-MainWindow
            }
        })

        $script:mainForm = $f
        $script:topBar = $tb
        $script:contentPanel = $cp
        $script:mainMenuBar = $ms
        $script:titleLabel = $title
        $script:statusBar = $sb
        $script:btnMin = $btnMin
        $script:btnMax = $btnMax
        $script:btnClose = $btnClose

        Build-MainMenu
        Update-MainLayout
        Update-BarLayout
        return $true
    } catch {
        try { $_ | Out-File -FilePath $logFile -Encoding utf8 -Append } catch { }
        $script:mainForm = $null
        return $false
    }
}

function Show-MainWindow {
    if (-not (Ensure-MainForm)) { return $false }
    try {
        if (-not $script:mainForm.Visible) { $script:mainForm.Show() }
        if ($script:mainForm.WindowState -eq 'Minimized') { $script:mainForm.WindowState = 'Normal' }
        $script:mainForm.PerformLayout()
        Update-MainLayout
        $script:mainForm.Activate()
        Update-BarLayout
        return $true
    } catch { return $false }
}

function Hide-MainWindow {
    try { if ($script:mainForm -and $script:mainForm.Visible) { $script:mainForm.Hide() } } catch { }
}

# 等 scrcpy 把窗口建出来再嵌。启动是异步的，窗口可能要两三秒才有。
function Get-ScrcpyHwnd {
    if (-not (Is-Running)) { return [IntPtr]::Zero }
    try {
        $script:proc.Refresh()
        $h = $script:proc.MainWindowHandle
        if ($h -ne [IntPtr]::Zero) { return $h }
    } catch { }
    try {
        if (Ensure-Win32 -and $script:settings.Serial) {
            $h = [TrayWin32.Native]::FindWindow($null, "ScrcpyBridge|$($script:settings.Serial)")
            if ($h -ne [IntPtr]::Zero) { return $h }
        }
    } catch { }
    return [IntPtr]::Zero
}

# 嵌入完成后把窗口宽度收到刚好包住画面：内容区高度不变，宽度按画面比例反算。
# 这样两侧不会留黑边，用户也不用自己去拖。
# 用户手动拖完窗口后调用：按当前方向记住尺寸
function Remember-CurrentSize {
    if (-not $script:mainForm) { return }
    if ($script:mainForm.WindowState -ne 'Normal') { return }
    if (-not $script:videoRatio -or $script:videoRatio -le 0) { return }
    $w = [int]$script:mainForm.Width
    $h = [int]$script:mainForm.Height
    if ($w -le 200 -or $h -le 200) { return }
    if ($script:videoRatio -lt 1) { $script:settings.SizePortrait = @($w, $h) }
    else { $script:settings.SizeLandscape = @($w, $h) }
    Save-Settings
    try {
        $dir = if ($script:videoRatio -lt 1) { 'portrait' } else { 'landscape' }
        "size $dir=${w}x${h}" | Out-File -FilePath $logFile -Append -Encoding utf8
    } catch { }
}

function Adjust-WindowToVideo {
    if (-not $script:mainForm) { return }
    if ($script:mainForm.WindowState -ne 'Normal') { return }
    if (-not $script:videoRatio -or $script:videoRatio -le 0) { return }
    if (-not $script:contentPanel) { return }
    try {
        $S = $script:S
        $ins = $script:embedInset
        $chromeH = $script:topBarH + $script:statusH + 2 * $ins
        $wa = [System.Windows.Forms.Screen]::FromControl($script:mainForm).WorkingArea

        $isPortrait = $script:videoRatio -lt 1
        $saved = if ($isPortrait) { @($script:settings.SizePortrait) } else { @($script:settings.SizeLandscape) }

        if ($saved.Count -eq 2 -and $saved[0] -gt 200 -and $saved[1] -gt 200) {
            # 有记忆尺寸：直接用，不拿当前状态当基准
            $wantW = [int]$saved[0]
            $wantH = [int]$saved[1]
        } elseif ($isPortrait) {
            # 竖屏首次：按工作区高度和画面比例算一个默认尺寸
            $maxH = [int]($wa.Height * 0.95)
            $wantH = $maxH
            $chH = $wantH - $chromeH
            $wantW = [int]($chH * $script:videoRatio) + 2 * $ins
            $floorW = [int](360 * $S)
            if ($wantW -lt $floorW) { $wantW = $floorW }
        } else {
            # 横屏首次：宽度优先，尽量用满屏幕宽度，高度按画面比例算
            $wantW = [int]($wa.Width * 0.95)
            $minW = [int](520 * $S)
            if ($wantW -lt $minW) { $wantW = $minW }
            $chW = $wantW - 2 * $ins
            $wantH = [int]($chW / $script:videoRatio) + $chromeH
            $maxH = [int]($wa.Height * 0.95)
            if ($wantH -gt $maxH) {
                $wantH = $maxH
                $chH = $wantH - $chromeH
                $wantW = [int]($chH * $script:videoRatio) + 2 * $ins
            }
        }

        if ([Math]::Abs($script:mainForm.Width - $wantW) -le 4 -and
            [Math]::Abs($script:mainForm.Height - $wantH) -le 4) { return }
        $script:mainForm.Size = New-Object System.Drawing.Size($wantW, $wantH)
        Update-MainLayout
        Sync-EmbedSize
        try {
            "adjust ratio=$([Math]::Round($script:videoRatio,4)) want=${wantW}x${wantH} got=$($script:mainForm.Width)x$($script:mainForm.Height)" |
                Out-File -FilePath $logFile -Append -Encoding utf8
        } catch { }
    } catch { }
}

function Attach-ScrcpyWindow($hwnd) {
    if (-not (Ensure-Win32)) { return $false }
    if (-not $script:contentPanel) { return $false }
    if ($hwnd -eq [IntPtr]::Zero) { return $false }
    try {
        $panel = $script:contentPanel
        # 先藏起来，避免原窗口在桌面闪一下
        [void][TrayWin32.Native]::ShowWindow($hwnd, 0)

        $style = [TrayWin32.Native]::GetWindowLongPtr($hwnd, -16).ToInt64()
        if ($style -lt 0) { $style = $style -band [int64]0xFFFFFFFF }
        $script:embedStyle = $style
        # 必须连 WS_POPUP 一起去掉。SDL 建的是弹出窗口，只摘标题栏和边框的话，
        # SetParent 之后它照样不受父窗口裁剪，会压到顶栏上面，也不会跟着父窗口缩放。
        # POPUP | CAPTION | THICKFRAME | MINIMIZEBOX | MAXIMIZEBOX | SYSMENU
        $drop = [int64]0x80CF0000
        $new = (($style -band (-bnot $drop)) -bor [int64]0x40000000) -band [int64]0xFFFFFFFF
        [void][TrayWin32.Native]::SetWindowLongPtr($hwnd, -16, [IntPtr]::new([int64]$new))

        [void][TrayWin32.Native]::SetParent($hwnd, $panel.Handle)
        if ([TrayWin32.Native]::GetParent($hwnd) -ne $panel.Handle) { return $false }

        $w = $panel.ClientSize.Width
        $h = $panel.ClientSize.Height
        if ($w -le 0) { $w = [Math]::Max(1, $panel.Width) }
        if ($h -le 0) { $h = [Math]::Max(1, $panel.Height) }
        # SWP_FRAMECHANGED | SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW
        $ok = [TrayWin32.Native]::SetWindowPos($hwnd, [IntPtr]::Zero, 0, 0, $w, $h, 0x0074)
        if (-not $ok) { return $false }

        # SDL 有时候不接受外部改的尺寸，会把自己改回去。
        # 读回真实大小，对不上就再用 MoveWindow 强设一次，两次还不行就记日志，别让用户面对半个画面。
        $gotW = 0; $gotH = 0
        for ($i = 0; $i -lt 3; $i++) {
            $rc = [TrayWin32.Native+RECT]::new()
            if ([TrayWin32.Native]::GetWindowRect($hwnd, [ref]$rc)) {
                $gotW = $rc.Right - $rc.Left
                $gotH = $rc.Bottom - $rc.Top
            }
            if ([Math]::Abs($gotW - $w) -le 4 -and [Math]::Abs($gotH - $h) -le 4) { break }
            [void][TrayWin32.Native]::MoveWindow($hwnd, 0, 0, $w, $h, $true)
            Start-Sleep -Milliseconds 80
        }

        # 再显式显示一次：某些情况下 SDL 窗口初始状态是隐藏的，只靠 SWP_SHOWWINDOW 不一定顶出来
        [void][TrayWin32.Native]::ShowWindow($hwnd, 5)
        try {
            "attach  hwnd=$hwnd style=$('{0:X8}' -f $style) new=$('{0:X8}' -f $new) want=${w}x${h} got=${gotW}x${gotH}" |
                Out-File -FilePath $logFile -Append -Encoding utf8
        } catch { }

        $script:embedHwnd = $hwnd
        $script:embedded = $true
        Adjust-WindowToVideo
        return $true
    } catch {
        try { $_ | Out-File -FilePath $logFile -Encoding utf8 -Append } catch { }
        return $false
    }
}

# 嵌入失败：画面回退到 scrcpy 自己的窗口，功能照用，只是没有菜单栏
function Undo-Attach($hwnd) {
    try {
        if ($hwnd -ne [IntPtr]::Zero) {
            [void][TrayWin32.Native]::SetParent($hwnd, [IntPtr]::Zero)
            if ($script:embedStyle) { [void][TrayWin32.Native]::SetWindowLongPtr($hwnd, -16, [IntPtr]::new([int64]$script:embedStyle)) }
            [void][TrayWin32.Native]::SetWindowPos($hwnd, [IntPtr]::Zero, 0, 0, 0, 0, 0x0027)
            [void][TrayWin32.Native]::ShowWindow($hwnd, 5)
        }
    } catch { }
    $script:embedHwnd = [IntPtr]::Zero
    $script:embedded = $false
}

function Start-AttachTimer {
    if (-not $script:attachTimer) {
        $t = New-Object System.Windows.Forms.Timer
        # 60ms 一查，发现就立刻藏起来。就算坐标参数没生效，也来不及在桌面闪成第二个窗口
        $t.Interval = 60
        $t.Add_Tick({
            if (-not (Is-Running)) {
                # scrcpy 没起来（没设备、参数不对、秒退），别留个黑窗口给用户
                $script:attachTimer.Stop()
                Hide-MainWindow
                return
            }
            $script:attachLeft--
            $h = Get-ScrcpyHwnd
            if ($h -ne [IntPtr]::Zero) {
                $script:attachTimer.Stop()
                if (-not $script:showWindowWanted) { return }
                if (Show-MainWindow) {
                    if (Attach-ScrcpyWindow $h) {
                        Sync-EmbedSize
                        [void][TrayWin32.Native]::SetFocus($h)
                    } else {
                        Undo-Attach $h
                        Hide-MainWindow
                        Notify '画面已回退' '窗口嵌入失败，画面在独立窗口里显示，功能不受影响。' 'Warning'
                    }
                }
            } elseif ($script:attachLeft -le 0) {
                $script:attachTimer.Stop()
            }
        })
        $script:attachTimer = $t
    }
    $script:attachLeft = 100
    $script:attachTimer.Start()
}

function Show-SleepOverlay {
    # 画面已经嵌在窗口里：提示直接盖在内容区上，不用再往桌面上浮一个窗
    if ($script:embedded -and $script:contentPanel) {
        Ensure-SleepInner
        if ($script:sleepInner) {
            $script:sleepInner.Visible = $true
            $script:sleepInner.BringToFront()
            # 把嵌入的画面压到提示层下面（SWP_NOSIZE|SWP_NOMOVE|SWP_NOACTIVATE）
            try { [void][TrayWin32.Native]::SetWindowPos($script:embedHwnd, $script:sleepInner.Handle, 0, 0, 0, 0, 0x0013) } catch { }
            & $script:sleepInnerLayout
            return
        }
    }
    $rect = Get-ScrcpyRect
    if ($rect) { $script:sleepLastRect = $rect }
    # 进程已经退干净时没有窗口可取，用最后一次记录的位置，让黑屏留在原位
    if (-not $script:sleepLastRect) { return }
    Ensure-SleepForm
    $r = $script:sleepLastRect
    $script:sleepForm.Bounds = New-Object System.Drawing.Rectangle($r.X, $r.Y, $r.W, $r.H)
    & $script:sleepLayout
    if (-not $script:sleepForm.Visible) { $script:sleepForm.Show() }
    $script:sleepForm.BringToFront()
}

function Hide-SleepOverlay {
    if ($script:sleepInner -and $script:sleepInner.Visible) { $script:sleepInner.Visible = $false }
    if ($script:sleepForm -and $script:sleepForm.Visible) { $script:sleepForm.Hide() }
}

# 窗口内的息屏提示层。和独立浮窗内容一样，只是住在内容区里。
function Ensure-SleepInner {
    if ($script:sleepInner) { return }
    if (-not $script:contentPanel) { return }
    $p = New-Object System.Windows.Forms.Panel
    $p.Dock = [System.Windows.Forms.DockStyle]::Fill
    $p.BackColor = [System.Drawing.Color]::FromArgb(10, 10, 12)
    $p.Visible = $false

    $title = New-Object System.Windows.Forms.Label
    $title.Text = '手机已息屏'
    $title.ForeColor = [System.Drawing.Color]::FromArgb(235, 235, 240)
    $title.Font = New-Object System.Drawing.Font('Microsoft YaHei UI', 18, [System.Drawing.FontStyle]::Bold)
    $title.TextAlign = [System.Drawing.ContentAlignment]::MiddleCenter

    $sub = New-Object System.Windows.Forms.Label
    $sub.Text = '投屏画面已停止更新，点下面的按钮唤醒'
    $sub.ForeColor = [System.Drawing.Color]::FromArgb(158, 158, 170)
    $sub.Font = New-Object System.Drawing.Font('Microsoft YaHei UI', 9.5)
    $sub.TextAlign = [System.Drawing.ContentAlignment]::MiddleCenter

    $btn = New-Object System.Windows.Forms.Button
    $btn.Text = '唤醒并解锁'
    $btn.FlatStyle = [System.Windows.Forms.FlatStyle]::Flat
    $btn.BackColor = [System.Drawing.Color]::FromArgb(0, 120, 215)
    $btn.ForeColor = [System.Drawing.Color]::White
    $btn.FlatAppearance.BorderSize = 0
    $btn.Font = New-Object System.Drawing.Font('Microsoft YaHei UI', 10)
    $btn.Cursor = [System.Windows.Forms.Cursors]::Hand
    $btn.Add_Click({
        Hide-SleepOverlay
        try {
            $r = Wake-Unlock
            if ($r.Ok) { Notify '已唤醒' $r.Msg 'Info' } else { Notify '没唤醒' $r.Msg 'Warning' }
        } catch { }
    })

    $p.Controls.Add($title)
    $p.Controls.Add($sub)
    $p.Controls.Add($btn)

    $layout = {
        if (-not $script:sleepInner) { return }
        $w = $script:sleepInner.ClientSize.Width
        $h = $script:sleepInner.ClientSize.Height
        $bw = [Math]::Min(220, [Math]::Max(120, [int]($w * 0.5)))
        $cx = [int]($w / 2)
        $cy = [int]($h / 2)
        $script:sleepInnerTitle.SetBounds(0, $cy - 62, $w, 40)
        $script:sleepInnerSub.SetBounds(0, $cy - 16, $w, 24)
        $script:sleepInnerBtn.SetBounds($cx - [int]($bw / 2), $cy + 22, $bw, 40)
    }
    $p.Add_Resize($layout)

    $script:contentPanel.Controls.Add($p)
    $script:sleepInner = $p
    $script:sleepInnerTitle = $title
    $script:sleepInnerSub = $sub
    $script:sleepInnerBtn = $btn
    $script:sleepInnerLayout = $layout
}

# 当前是否原生窗口模式
function Is-NativeMode { return ([string]$script:settings.WindowMode -eq 'native') }

# 切换窗口模式：投屏中则整路重启
function Set-WindowMode($mode) {
    $m = if ($mode -eq 'native') { 'native' } else { 'embedded' }
    $script:settings.WindowMode = $m
    Save-Settings
    try {
        "windowmode = $m" | Out-File -FilePath $logFile -Append -Encoding utf8
    } catch { }
    $was = Restart-IfRunning
    Update-State
    if ($was) { Start-Audio }
    $label = if ($m -eq 'native') { 'scrcpy 原生窗口' } else { '程序窗口' }
    Notify '窗口模式已切换' "现在是：$label。投屏已按新方式重启。" 'Info'
}

function Set-Screen($on) {
    $was = Restart-IfRunning
    $script:settings.Screen = [bool]$on
    Save-Settings
    # 关掉投屏就把窗口收起来；打开时由 Start-Audio 重新建窗口并嵌入
    if (-not $on) {
        $script:embedded = $false
        $script:embedHwnd = [IntPtr]::Zero
        Hide-MainWindow
    }
    Update-State
    if ($was) { Start-Audio }
}

function Set-OnTop($on) {
    $script:settings.OnTop = [bool]$on
    Save-Settings
    if (Is-NativeMode) {
        # 原生窗口靠 --always-on-top，改了要重启投屏
        $was = Restart-IfRunning
        Update-State
        if ($was) { Start-Audio }
    } else {
        # 程序窗口直接改层级：秒生效，不重启投屏、不黑屏
        try { if ($script:mainForm) { $script:mainForm.TopMost = [bool]$on } } catch { }
        Update-State
    }
}

# 帧率在启动参数里，改完同样得重启投屏才生效。0 表示不限，跟手机刷新率走。
function Set-MaxFps($fps) {
    $f = [int]$fps
    if ($f -lt 0) { $f = 0 }
    $was = Restart-IfRunning
    $script:settings.MaxFps = $f
    Save-Settings
    Update-State
    if ($was) { Start-Audio }
}

function Read-Pins {
    $h = @{}
    if (Test-Path $script:pinFile) {
        try {
            $raw = [IO.File]::ReadAllText($script:pinFile, [Text.Encoding]::UTF8).TrimStart([char]0xFEFF)
            if ($raw.Trim()) {
                $o = $raw | ConvertFrom-Json
                foreach ($p in $o.PSObject.Properties) { if ($p.Value) { $h[$p.Name] = [string]$p.Value } }
            }
        } catch { }
    }
    return $h
}

function Save-Pins($h) {
    try {
        if ($h.Count -eq 0) {
            if (Test-Path $script:pinFile) { Remove-Item $script:pinFile -Force }
        } else {
            ($h | ConvertTo-Json -Depth 3) | Out-File -FilePath $script:pinFile -Encoding utf8
        }
    } catch { }
}

function Get-DeviceKey($serial) {
    $d = @(Get-Devices) | Where-Object { $_.Serial -eq $serial } | Select-Object -First 1
    if ($d -and $d.Model) { return $d.Model }
    return $serial
}

function Get-Pin($serial) {
    $pins = Read-Pins
    if ($pins.Count -eq 0) { return $null }
    if ($pins.ContainsKey($serial)) { return $pins[$serial] }
    $key = Get-DeviceKey $serial
    if ($pins.ContainsKey($key)) { return $pins[$key] }
    return $null
}

function Set-Pin($serial, $pin) {
    $pins = Read-Pins
    $key = Get-DeviceKey $serial
    if ($pin) { $pins[$key] = [string]$pin }
    elseif ($pins.ContainsKey($key)) { $pins.Remove($key) }
    Save-Pins $pins
}

# 状态探测走双来源，单靠一个字段换机型就会瞎：
#   dumpsys window policy 的 interactiveState 是 AOSP 标准（Android 5~16 都有），判断亮屏最准
#   dumpsys window 的 isKeyguardShowing 判断锁屏，Android 9 以下叫 mShowingLockscreen
# 不能用 grep -q，它提前退出会让 dumpsys 报 Broken pipe，被 PowerShell 当成致命错误
function Get-DeviceState($serial) {
    $ErrorActionPreference = 'Continue'
    $out = @()
    try {
        $cmd = "dumpsys window policy | grep -E 'interactiveState='; dumpsys window | grep -E 'screenState=|isKeyguardShowing=|mShowingLockscreen='"
        $out = @(& $script:adb -s $serial shell $cmd 2>$null)
    } catch { $out = @() }
    $txt = (@($out) | ForEach-Object { "$_" }) -join "`n"

    $awake = ($txt -match 'interactiveState=INTERACTIVE_STATE_AWAKE')
    $asleep = ($txt -match 'interactiveState=INTERACTIVE_STATE_(SLEEP|DOZING)')
    # interactiveState 读不到时退回 screenState（个别精简 ROM 没有 policy dump）
    if (-not $awake -and -not $asleep) {
        $awake = ($txt -match 'screenState=SCREEN_STATE_ON')
        $asleep = ($txt -match 'screenState=SCREEN_STATE_OFF')
    }
    return @{
        On     = ($awake -and -not $asleep)
        Locked = ($txt -match 'isKeyguardShowing=true') -or ($txt -match 'mShowingLockscreen=true')
        Valid  = ($txt.Trim().Length -gt 0)
    }
}

function Get-LockState($serial) {
    return (Get-DeviceState $serial).Locked
}

# 轮询代替固定等待：状态一变就返回，解锁完立刻结束
function Wait-Unlocked($serial, $timeoutMs) {
    $end = [DateTime]::Now.AddMilliseconds($timeoutMs)
    while ($true) {
        $st = Get-DeviceState $serial
        if ($st.Valid -and -not $st.Locked) { return $true }
        if ([DateTime]::Now -ge $end) { return $false }
        Start-Sleep -Milliseconds 50
    }
}

# 等屏幕真正上电。POWER 按下到 interactiveState 变 AWAKE 有几百毫秒延迟，
# 这期间发出去的按键会被系统丢掉。
function Wait-Awake($serial, $timeoutMs) {
    $end = [DateTime]::Now.AddMilliseconds($timeoutMs)
    while ($true) {
        $st = Get-DeviceState $serial
        if ($st.Valid -and $st.On) { return $true }
        if ([DateTime]::Now -ge $end) { return $false }
        Start-Sleep -Milliseconds 50
    }
}

# 判断密码键盘在不在。
# 试过 uiautomator dump：锁屏密码界面带 FLAG_SECURE，读不到里面的节点，还会耗 3 秒，不可用。
# 但同一原因让截图变成全黑小图（正常画面 1~3MB，纯黑只有 20~30KB），用它判断又快又准。
# 只在「确认锁着且屏幕亮着」时调用，否则黑屏本身也会被误判成密码界面。
function Test-PinPad($serial) {
    $ErrorActionPreference = 'Continue'
    $out = ''
    try {
        $out = ((& $script:adb -s $serial shell "screencap -p /sdcard/.s.png; stat -c %s /sdcard/.s.png; rm -f /sdcard/.s.png" 2>$null) | Out-String)
    } catch { return $false }
    if ($out -match '(\d{4,})') {
        $n = [int64]$Matches[1]
        return ($n -lt 150000)
    }
    return $false
}

function Get-ScreenSize($serial) {
    $o = (& $script:adb -s $serial shell wm size 2>$null | Out-String)
    if ($o -match '(\d+)x(\d+)') { return @([int]$Matches[1], [int]$Matches[2]) }
    return @(1080, 2400)
}

# 读 scrcpy 虚拟屏的真实宽高——这就是视频流的方向，App 横竖屏时它会变。
# 不要用 mRotation（自动旋转关闭时恒为 0），也不要用 wm size（物理屏固定竖屏）。
function Get-VideoSize($serial) {
    try {
        $o = (& $script:adb -s $serial shell dumpsys display 2>$null | Out-String)
        foreach ($line in ($o -split "`n")) {
            if ($line -match 'DisplayDeviceInfo\{"scrcpy"') {
                # 取 "宽 x 高"，用逗号或引号后的空格界定，避开 uniqueId 里的 2000/96 等数字
                if ($line -match ',\s*(\d{3,5})\s+x\s+(\d{3,5}),') {
                    $w = [int]$Matches[1]; $h = [int]$Matches[2]
                    if ($w -gt 0 -and $h -gt 0) { return @($w, $h) }
                }
            }
        }
    } catch { }
    return @()
}

# 视频流方向变了就换窗口比例，画面始终完整显示
function Update-Orientation($serial) {
    if (-not $serial) { return }
    if (-not $script:settings.Screen) { return }
    $sz = @(Get-VideoSize $serial)
    if ($sz.Count -lt 2) { return }
    $w = [double]$sz[0]; $h = [double]$sz[1]
    $ratio = $w / $h

    # 比例没变就跳过（容差 0.02），避免每秒重排窗口
    if ($script:videoRatio -gt 0 -and [Math]::Abs($ratio - $script:videoRatio) -lt 0.02) { return }
    $script:videoRatio = $ratio
    Adjust-WindowToVideo
    try {
        "orientation videosize=${w}x${h} ratio=$([Math]::Round($ratio,4)) form=$($script:mainForm.Width)x$($script:mainForm.Height)" |
            Out-File -FilePath $logFile -Append -Encoding utf8
    } catch { }
}

# ---------------- 不让它锁屏 ----------------
# 解锁要靠「上滑手势 + 密码框位置」，每个厂商都不一样，没法通用。
# 但 screen_off_timeout 是 AOSP 标准设置，任何 Android 都认：
# 转发期间把它改成最大值，手机就不会自己息屏，也就不会回到锁屏界面。
# 这是唯一能跨品牌生效的做法。
$script:origTimeout = $null
$script:timeoutFile = Join-Path $PSScriptRoot 'device-timeout.json'

function Read-TimeoutBackup {
    $h = @{}
    if (Test-Path $script:timeoutFile) {
        try {
            $raw = [IO.File]::ReadAllText($script:timeoutFile, [Text.Encoding]::UTF8).TrimStart([char]0xFEFF)
            if ($raw.Trim()) {
                $o = $raw | ConvertFrom-Json
                foreach ($p in $o.PSObject.Properties) { if ($null -ne $p.Value) { $h[$p.Name] = [string]$p.Value } }
            }
        } catch { }
    }
    return $h
}

function Save-TimeoutBackup($h) {
    try {
        if ($h.Count -eq 0) {
            if (Test-Path $script:timeoutFile) { Remove-Item $script:timeoutFile -Force }
        } else {
            ($h | ConvertTo-Json -Depth 3) | Out-File -FilePath $script:timeoutFile -Encoding utf8
        }
    } catch { }
}

# 上次异常退出（被杀进程）没来得及恢复的值，先补回去。
# 不修的话，下次会把「永不息屏」当成用户原始设置存下来，手机从此不再自动息屏。
function Clear-StaleTimeout($serial) {
    $ErrorActionPreference = 'Continue'
    try {
        $bak = Read-TimeoutBackup
        if ($bak.Count -eq 0) { return }
        $key = Get-DeviceKey $serial
        if (-not $bak.ContainsKey($key)) { return }
        & $script:adb -s $serial shell settings put system screen_off_timeout $bak[$key] 2>$null | Out-Null
        $bak.Remove($key)
        Save-TimeoutBackup $bak
    } catch { }
}

function Push-KeepAwake($serial) {
    $ErrorActionPreference = 'Continue'
    try {
        Clear-StaleTimeout $serial
        $bak = Read-TimeoutBackup
        $key = Get-DeviceKey $serial
        $cur = ((& $script:adb -s $serial shell settings get system screen_off_timeout 2>$null | Out-String).Trim() -replace '\D', '')
        if ($cur -and [int64]$cur -lt 2147483647) {
            $bak[$key] = $cur
            Save-TimeoutBackup $bak
            $script:origTimeout = $cur
        }
        & $script:adb -s $serial shell settings put system screen_off_timeout 2147483647 2>$null | Out-Null
    } catch { }
}

function Pop-KeepAwake {
    $ErrorActionPreference = 'Continue'
    try {
        $bak = Read-TimeoutBackup
        if ($bak.Count -eq 0) { return }
        $serial = Get-Serial
        if (-not $serial) { return }
        $key = Get-DeviceKey $serial
        if ($bak.ContainsKey($key)) {
            & $script:adb -s $serial shell settings put system screen_off_timeout $bak[$key] 2>$null | Out-Null
            $bak.Remove($key)
            Save-TimeoutBackup $bak
        }
        $script:origTimeout = $null
    } catch { }
}

# 逐个字符发按键事件。锁屏密码框对 input text 不响应（没有可聚焦的输入框），
# 但对注入的 KEYCODE 是响应的。KEYCODE 编号是 AOSP 固定值，任何品牌都一样。
function Send-PinKeys($serial, $text) {
    $parts = @()
    foreach ($ch in ([string]$text).ToCharArray()) {
        if ($ch -match '^[0-9]$') {
            # KEYCODE_0=7 ... KEYCODE_9=16
            $n = [int][char]$ch - [int][char]'0' + 7
            $parts += "input keyevent $n"
        } elseif ($ch -match '^[a-zA-Z]$') {
            # KEYCODE_A=29 ... KEYCODE_Z=54
            $n = [int][char]([char]::ToUpper($ch)) - [int][char]'A' + 29
            $parts += "input keyevent $n"
        }
    }
    if ($parts.Count -eq 0) { return }
    # 中间留一点间隔，连续注入太快锁屏密码框会丢键
    $cmd = $parts -join '; sleep 0.05; '
    & $script:adb -s $serial shell $cmd 2>$null | Out-Null
}

# 把密码框弄出来 / 直接解掉锁屏。
# 各厂商锁屏逻辑差别极大：vivo 提示「双指上滑」、原生 Android 是单指上滑、有的 ROM 点亮就给键盘。
# 没有一种手势能通吃，所以按「标准度」从高到低依次试，每步之后查一次锁屏状态。
# 返回 $true 表示锁屏已经解开（不需要密码）。
function Show-Keyguard($serial) {
    $ErrorActionPreference = 'Continue'
    if (Wait-Unlocked $serial 500) { return $true }

    # 1. AOSP 官方命令，无密码 / 滑动锁直接解
    & $script:adb -s $serial shell wm dismiss-keyguard 2>$null | Out-Null
    if (Wait-Unlocked $serial 600) { return $true }

    # 2. ENTER 是 KeyguardSecurityContainer 的标准确认键，多数 ROM 会拉起密码框
    & $script:adb -s $serial shell input keyevent 66 2>$null | Out-Null
    if (Wait-Unlocked $serial 600) { return $true }

    # 3. 原生 Android 标准手势，坐标按屏幕比例算，不写死像素
    $wh = Get-ScreenSize $serial
    $cx = [int]($wh[0] / 2)
    & $script:adb -s $serial shell input swipe $cx ([int]($wh[1] * 0.88)) $cx ([int]($wh[1] * 0.28)) 180 2>$null | Out-Null
    if (Wait-Unlocked $serial 700) { return $true }
    return $false
}

# 真正干活的部分。必须在 scrcpy 没接管屏幕时调用：
# scrcpy 的 --turn-screen-off 会持续把显示电源关掉，那时 adb 怎么发键都点不亮。
function Invoke-Unlock($serial, [switch]$AllowPrompt, [switch]$ForceWake) {
    $ErrorActionPreference = 'Continue'
    $st = Get-DeviceState $serial
    if (-not $st.Valid) { return @{ Ok = $false; Msg = '读不到手机状态，可能已经断开' } }
    # 只看有没有锁屏。投屏中 interactiveState 会假报 AWAKE，拿它判断会跳过必要的点亮
    if (-not $st.Locked -and -not $ForceWake) { return @{ Ok = $true; Msg = '已解锁' } }

    if ($st.On) {
        # 系统说亮着：可能真亮着但锁着，也可能显示电源被 scrcpy 关过（物理屏黑、状态假报）。
        # 后者单纯 WAKEUP 不会重新给显示上电，必须 POWER 关一次再唤醒。
        & $script:adb -s $serial shell input keyevent 26 2>$null | Out-Null
        Start-Sleep -Milliseconds 420
        & $script:adb -s $serial shell input keyevent 224 2>$null | Out-Null
    } else {
        # 真息屏：POWER 键点亮
        & $script:adb -s $serial shell input keyevent 26 2>$null | Out-Null
    }
    # 等亮屏完成再发键，否则前几个键会被丢掉
    [void](Wait-Awake $serial 1400)

    $pin = Get-Pin $serial

    # 先走通用路径：无密码 / 滑动锁在这一步就解了，省掉输密码
    if (Show-Keyguard $serial) { return @{ Ok = $true; Msg = '已唤醒解锁' } }

    if (-not $pin) {
        # 没存过密码。自动重试可能连调多次，问过一次没结果就不再打断用户
        if (-not $AllowPrompt -or $script:pinAskBlock) { return @{ Ok = $false; Msg = 'NEED_PIN' } }
        $tip = "手机锁屏需要密码，填一次以后自动输入。`n只存在本机（明文），取消则不解锁。"
        $ans = Show-Input $tip '自动解锁' ''
        if ($ans -eq $null) { $script:pinAskBlock = $true; return @{ Ok = $false; Msg = '已跳过解锁' } }
        $pin = $ans.Trim()
        if (-not $pin) { $script:pinAskBlock = $true; return @{ Ok = $false; Msg = '已跳过解锁' } }
        Set-Pin $serial $pin
        # 新密码要走一遍拉起密码框的流程
        [void](Show-Keyguard $serial)
    }

    if (-not $pin) { return @{ Ok = $false; Msg = '已跳过解锁' } }

    # 密码框已经在屏幕上（Show-Keyguard 试过三条路），直接敲。锁屏状态下按键不会漏到应用里。
    Send-PinKeys $serial $pin
    if (Wait-Unlocked $serial 800) { return @{ Ok = $true; Msg = '已唤醒解锁' } }
    # vivo 等机型不会自动提交密码，必须补一个回车；已解开就不按，避免误触桌面图标
    & $script:adb -s $serial shell input keyevent 66 2>$null | Out-Null
    if (Wait-Unlocked $serial 1300) { return @{ Ok = $true; Msg = '已唤醒解锁' } }

    # 还是没开：看一眼密码键盘在不在，区分「密码错」和「键盘没调出来」，提示才对症
    if (Test-PinPad $serial) { return @{ Ok = $false; Msg = '密码不对，或手机换了密码' } }
    return @{ Ok = $false; Msg = '没能调出密码键盘，这台手机可能要手动解一次' }
}

function Wake-Unlock {
    $ErrorActionPreference = 'Continue'
    $serial = Get-Serial
    if (-not $serial) { return @{ Ok = $false; Msg = '没有可用设备' } }
    # 用户主动点的，允许再问一次密码
    $script:pinAskBlock = $false
    # 投屏关屏时屏幕归 scrcpy 管，adb 点不亮，先停掉让它把屏幕还回来，解完再开
    $restart = $false
    if ((Is-Running) -and $script:settings.Screen) {
        Stop-Audio
        $restart = $true
        Start-Sleep -Milliseconds 400
    }
    $r = Invoke-Unlock $serial -AllowPrompt -ForceWake:([bool]$restart)
    if ($restart) { Start-Audio -SkipUnlock }
    return $r
}

function Connect-Address($addr) {
    $addr = ([string]$addr).Trim()
    if (-not $addr) { return '地址为空' }
    if ($addr -notmatch ':[0-9]+$') { $addr = "$addr:5555" }
    $r = & $script:adb connect $addr 2>&1
    $msg = (($r | ForEach-Object { "$_" }) -join "`n")
    if ($msg -match 'connected to') { Add-WifiTarget $addr }
    return $msg
}

function Pair-Address($addr, $code) {
    $addr = ([string]$addr).Trim()
    $code = ([string]$code).Trim()
    if (-not $addr -or -not $code) { return '地址或配对码为空' }
    $r = & $script:adb pair $addr $code 2>&1
    return (($r | ForEach-Object { "$_" }) -join "`n")
}

function Connect-All-Mdns {
    $n = 0
    & $script:adb mdns services 2>$null | Select-String '_adb-tls-connect._tcp' | ForEach-Object {
        $parts = @(($_.Line -split '\s+') | Where-Object { $_ })
        if ($parts.Count -ge 3) {
            & $script:adb connect $parts[2] 2>$null | Out-Null
            $n++
        }
    }
    return $n
}

function Clear-BadDevices {
    $ErrorActionPreference = 'Continue'
    if (Is-Running) { Stop-Audio }
    $all = @(Get-Devices)
    $ipModels = @($all | Where-Object { $_.Serial -match '^[0-9.]+:[0-9]+$' -and $_.Model } | ForEach-Object { $_.Model })
    $kill = @()
    foreach ($d in $all) {
        if ($d.Serial -match '_adb-tls-connect' -and $ipModels -contains $d.Model) { $kill += $d.Serial }
    }
    $out = & $script:adb devices 2>$null
    foreach ($line in $out) {
        if ($line -match '\s(offline|unauthorized|no permissions)\s*$') { $kill += ($line -split '\s+')[0] }
    }
    $kill = @($kill | Where-Object { $_ } | Select-Object -Unique)
    foreach ($s in $kill) { & $script:adb disconnect $s 2>$null | Out-Null }
    $script:settings.Serial = $null
    return $kill.Count
}

function Is-Running {
    try { return ($script:proc -and -not $script:proc.HasExited) } catch { return $false }
}

function Start-Audio {
    param([switch]$SkipUnlock)
    if (Is-Running) { return $true }
    $serial = Get-Serial
    if (-not $serial) { return $false }
    # 纯音频不改屏幕设置，但要顺手修掉上次崩溃残留的「永不息屏」
    if (-not $script:settings.Screen) { Clear-StaleTimeout $serial }
    # 投屏才需要解锁：锁屏画面投出来没意义。纯音频不必打扰手机。
    # 必须在 scrcpy 启动前做——一旦带上 --turn-screen-off，adb 就点不亮屏幕了。
    if (-not $SkipUnlock -and $script:settings.Screen) {
        $r = Invoke-Unlock $serial -AllowPrompt
        if (-not $r.Ok -and $r.Msg -eq 'NEED_PIN') {
            Notify '没能自动解锁' '手机锁屏密码未保存，投屏会停在锁屏界面。可右键「唤醒并解锁」再试。' 'Warning'
        }
        # 解锁完立刻改成不自动息屏：换机型时手势解锁靠不住，但这条设置所有 Android 都认，
        # 从根上避免又滑回锁屏。停止投屏时恢复原值。
        Push-KeepAwake $serial
    }
    $iconDir = Join-Path $PSScriptRoot 'icons'
    if (Test-Path (Join-Path $iconDir 'scrcpy.png')) { $env:SCRCPY_ICON_DIR = $iconDir }
    # 画面和声音放在同一个进程里。
    # 试过拆成两个进程（画面 --no-audio + 独立音频进程），实测声音会碎：8 秒内报 17 次
    # `Buffering threshold exceeded`，单进程时是 0 次。所以只能单进程。
    $a = @(
        '-s', $serial,
        '--audio-source=output',
        "--audio-codec=$($script:settings.Codec)",
        "--audio-buffer=$($script:settings.Buffer)",
        '--audio-output-buffer=10',
        '--require-audio'
    )
    if ($script:settings.Screen) {
        if (Is-NativeMode) {
            # 原生窗口：scrcpy 自己开窗，不嵌进我们的窗口，也不显示主窗口。
            # 置顶靠 scrcpy 的参数（改设置会整路重启），标题带序列号方便多开定位。
            if ($script:settings.OnTop) { $a += '--always-on-top' }
        } else {
        # 程序窗口：先把我们的窗口立起来，再把画面嵌进去。
        [void](Show-MainWindow)
        # 把 scrcpy 的窗口直接建在屏幕外：它原本会先在桌面冒出来，
        # 250ms 后被嵌进主窗口才消失，看着就像多出一个窗口又自己没了
        $a += '--window-x=-32000'
        $a += '--window-y=-32000'
        # 按手机画面比例定窗口大小：先保证顶栏菜单放得下，再按比例反推高度，
        # 让画面正好铺满内容区，左右不留黑边。高度顶到屏幕时改成以高度为准。
        try {
            if ($script:mainForm -and $script:mainForm.WindowState -eq 'Normal') {
                # 优先用 scrcpy 虚拟屏（视频流真实方向），读不到才回退物理屏
                $vsz = @(Get-VideoSize $serial)
                if ($vsz.Count -ge 2 -and $vsz[0] -gt 0 -and $vsz[1] -gt 0) {
                    $szInfo = $vsz
                } else {
                    $szInfo = @(Get-ScreenSize $serial)
                }
                if ($szInfo.Count -ge 2 -and $szInfo[0] -gt 0 -and $szInfo[1] -gt 0) {
                    $ratio = [double]$szInfo[0] / [double]$szInfo[1]
                    $script:videoRatio = $ratio
                    $ins = $script:embedInset
                    $extra = $script:topBarH + $script:statusH + 2 * $ins
                    $wa = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
                    $maxH = [int]($wa.Height * 0.95)
                    $saved = if ($ratio -lt 1) { @($script:settings.SizePortrait) } else { @($script:settings.SizeLandscape) }
                    if ($saved.Count -eq 2 -and $saved[0] -gt 200 -and $saved[1] -gt 200) {
                        # 有记忆尺寸：直接开窗，不用当前状态当基准
                        $wWant = [int]$saved[0]
                        $hWant = [int]$saved[1]
                    } elseif ($ratio -lt 1) {
                        # 竖屏首次：窄高，宽度按菜单最小宽度起
                        $wWant = [int](416 * $script:S)
                        $hWant = [int](($wWant - 2 * $ins) / $ratio) + $extra
                        if ($hWant -gt $maxH) {
                            $hWant = $maxH
                            $wWant = [int](($hWant - $extra) * $ratio) + 2 * $ins
                        }
                    } else {
                        # 横屏首次：宽扁，尽量用满屏幕宽度
                        $wWant = [int]($wa.Width * 0.95)
                        $hWant = [int](($wWant - 2 * $ins) / $ratio) + $extra
                        if ($hWant -gt $maxH) {
                            $hWant = $maxH
                            $wWant = [int](($hWant - $extra) * $ratio) + 2 * $ins
                        }
                    }
                    $script:mainForm.Size = New-Object System.Drawing.Size($wWant, $hWant)
                    Update-MainLayout
                }
            }
        } catch { }
        # 投屏时立刻关手机屏，省电防烧屏，投屏画面不受影响
        $a += '--turn-screen-off'
        # scrcpy 默认把第 4 键映射成「最近任务」、第 5 键映射成「通知栏」，
        # 电脑上按侧键极易误触。改成：右键返回、中键桌面、第 4 键返回、第 5 键忽略。
        # 冒号后是按住 Shift 的绑定，沿用默认「原样转发点击」。
        $a += '--mouse-bind=bhb-:++++'
        # 限帧：这台手机是 1440x3168@90Hz，不限帧时编码器压力大，电脑上看会卡。
        # 60 帧对游戏和视频都够用，也是默认值；设成「不限」时不加这个参数。
        if ($script:settings.MaxFps -gt 0) { $a += "--max-fps=$($script:settings.MaxFps)" }
        # 程序窗口模式：标题带序列号，多开时能精确找到自己那一个窗口，不会嵌错。
        # 原生模式：用可读标题，scrcpy 窗口直接显示给用户看。
        if (Is-NativeMode) {
            $devName = if ($script:devLabel) { $script:devLabel } else { $serial }
            $a += "--window-title=手机投屏 - $devName"
        } else {
            $a += "--window-title=ScrcpyBridge|$serial"
        }
        # 直接按内容区大小开窗。嵌进来之后再改尺寸不一定被 SDL 接受，
        # 一开始就给对的尺寸，画面从第一帧就是对的。（仅程序窗口模式）
        if (-not (Is-NativeMode) -and $script:contentPanel) {
            $iw = $script:contentPanel.ClientSize.Width - 2 * $script:embedInset
            $ih = $script:contentPanel.ClientSize.Height - 2 * $script:embedInset
            if ($iw -ge 120 -and $ih -ge 120) {
                $a += "--window-width=$iw"
                $a += "--window-height=$ih"
            }
        }
        } # /else 程序窗口
    } else {
        $a += '--no-video'
        $a += '--no-window'
    }
    if ($script:settings.Codec -ne 'raw') { $a += '--audio-bit-rate=192000' }
    # 用 CreateNoWindow 起进程：scrcpy 是控制台程序，直接 Start-Process 会多出一个 cmd 黑窗。
    # 之前用 -WindowStyle Hidden 也能藏住，但那样连它自己的画面窗口初始也是隐藏的，
    # 嵌入时得再靠 ShowWindow 顶出来，容易出岔子。CreateNoWindow 只影响控制台，窗口照常创建。
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $script:scrcpy
    $psi.Arguments = (($a | ForEach-Object { if ("$_" -match '\s') { '"' + $_ + '"' } else { "$_" } }) -join ' ')
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.WorkingDirectory = (Split-Path $script:scrcpy)
    $script:proc = [System.Diagnostics.Process]::Start($psi)
    $script:userStopped = $false
    $script:retryLeft = 0
    Update-State
    # 手机空闲时音频通路不激活，表现为「必须按一次物理键才出声」。
    # 启动后按一下音量键再按回来，音量不变，只把通路叫醒。先更新界面再等，用户不觉得卡。
    try {
        Start-Sleep -Milliseconds 1200
        Wake-AudioPath $serial
    } catch { }
    # 等进程都起来了再记基线，否则会把自己的进程算进「别人」
    $script:otherCount = Get-OtherScrcpyCount
    $script:audioRestartPending = 0
    # 程序窗口模式：等 scrcpy 把窗口建出来，再嵌进主窗口。原生模式不用嵌。
    if ($script:settings.Screen -and -not (Is-NativeMode)) {
        $script:showWindowWanted = $true
        Start-AttachTimer
    }
    return $true
}

function Stop-Audio {
    $wasScreen = $script:settings.Screen
    if (Is-Running) {
        try {
            $h = $script:embedHwnd
            if ($h -eq [IntPtr]::Zero) {
                $script:proc.Refresh()
                $h = $script:proc.MainWindowHandle
            }
            if ($h -ne [IntPtr]::Zero) {
                # 画面嵌进来后就没了系统菜单，CloseMainWindow 不管用，直接发 WM_CLOSE，
                # 让 scrcpy 自己把手机屏幕的电源状态恢复回去
                try { [void][TrayWin32.Native]::PostMessage($h, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) } catch { }
                if (-not $script:proc.WaitForExit(1500)) { $script:proc.Kill(); [void]$script:proc.WaitForExit(600) }
            } else {
                $script:proc.Kill()
                [void]$script:proc.WaitForExit(600)
            }
        } catch { }
    }
    $script:proc = $null
    $script:embedHwnd = [IntPtr]::Zero
    $script:embedded = $false
    if ($script:attachTimer) { $script:attachTimer.Stop() }
    Hide-MainWindow
    # 被强杀时 scrcpy 来不及恢复显示电源，系统仍认为屏幕亮着，
    # 这时按电源键点不亮。发一次 POWER 把电源状态同步成「关」，之后才能正常点亮。
    if ($wasScreen) {
        # 把「不自动息屏」改回去，否则手机屏幕会一直亮着耗电
        Pop-KeepAwake
        $ErrorActionPreference = 'Continue'
        try {
            $serial = Get-Serial
            if ($serial) {
                $st = Get-DeviceState $serial
                if ($st.On -and $st.Locked) { & $script:adb -s $serial shell input keyevent 26 2>$null | Out-Null }
            }
        } catch { }
    }
    Update-State
}

# 手机空闲时音频通路不激活，表现为「必须按一次物理键才出声」。
# 按一下音量键再按回来，音量不变，只把通路叫醒。
function Wake-AudioPath($serial) {
    if (-not $serial) { $serial = Get-Serial }
    if (-not $serial) { return }
    try { & $script:adb -s $serial shell "input keyevent 24; input keyevent 25" 2>$null | Out-Null } catch { }
}

# 数一下除了自己这一路以外，还有几个 scrcpy 实例在跑。
# 别家的实例退出时会把手机上的音频通路一起收走，要能发现「数量变少了」这个事件。
function Get-OtherScrcpyCount {
    $ErrorActionPreference = 'Continue'
    try {
        $all = @(Get-Process scrcpy -ErrorAction SilentlyContinue)
    } catch { return 0 }
    $mine = 0
    if ($script:proc) { try { if (-not $script:proc.HasExited) { $mine++ } } catch { } }
    $n = $all.Count - $mine
    if ($n -lt 0) { $n = 0 }
    return $n
}

function Restart-IfRunning {
    $was = Is-Running
    if ($was) { Stop-Audio }
    return $was
}

function Set-Buffer($ms) {
    $was = Restart-IfRunning
    $script:settings.Buffer = $ms
    Save-Settings
    Update-State
    if ($was) { Start-Audio }
}

function Set-Codec($codec) {
    $was = Restart-IfRunning
    $script:settings.Codec = $codec
    Save-Settings
    Update-State
    if ($was) { Start-Audio }
}

# ---------------- 托盘 ----------------
$script:ni = New-Object System.Windows.Forms.NotifyIcon
$script:icoIdle = $null
$script:icoActive = $null
$si = [System.Windows.Forms.SystemInformation]::SmallIconSize
$pIdle = Join-Path $PSScriptRoot 'tray-idle.ico'
$pActive = Join-Path $PSScriptRoot 'tray-active.ico'
if (Test-Path $pIdle) { try { $script:icoIdle = New-Object System.Drawing.Icon($pIdle, $si.Width, $si.Height) } catch { } }
if (Test-Path $pActive) { try { $script:icoActive = New-Object System.Drawing.Icon($pActive, $si.Width, $si.Height) } catch { } }
if ($script:icoIdle) {
    $script:ni.Icon = $script:icoIdle
} else {
    $iconSrc = [System.Drawing.Icon]::ExtractAssociatedIcon($script:scrcpy)
    if ($iconSrc) { $script:ni.Icon = $iconSrc } else { $script:ni.Icon = [System.Drawing.SystemIcons]::Application }
}
$script:ni.Text = '手机音频'
$script:ni.Visible = $true

$menu = New-Object System.Windows.Forms.ContextMenuStrip
# 蓝牙那项要悬停出提示，必须打开这个，否则 ToolTipText 不显示
$menu.ShowItemToolTips = $true

$script:miStatus = New-Object System.Windows.Forms.ToolStripMenuItem
$script:miStatus.Text = '未运行'
$script:miStatus.Enabled = $false
$menu.Items.Add($script:miStatus) | Out-Null
$menu.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator)) | Out-Null

# 设备选择
$script:miDevice = New-Object System.Windows.Forms.ToolStripMenuItem
$script:miDevice.Text = '选择设备'
$menu.Items.Add($script:miDevice) | Out-Null

function Get-SelectableDevices {
    $all = @(Get-Devices)
    $ipOnes = @($all | Where-Object { $_.Serial -match '^[0-9.]+:[0-9]+$' })
    $result = @()
    foreach ($d in $all) {
        if ($d.Serial -match '_adb-tls-connect') {
            if ($d.Model -and ($ipOnes | Where-Object { $_.Model -eq $d.Model })) { continue }
        }
        $result += $d
    }
    return $result
}

function Get-DevText($d) {
    $base = if ($d.Name) { $d.Name } elseif ($d.Model) { $d.Model } else { '未知机型' }
    if ($d.Kind -eq 'USB') { return "$base  [USB]" }
    $tail = if ($d.Serial -match '^([0-9.]+):[0-9]+$') { $Matches[1] } else { '自动发现' }
    return "$base  [WiFi]  $tail"
}

function Add-DevItem($parent, $d, $cur) {
    $mi = New-Object System.Windows.Forms.ToolStripMenuItem
    $mi.Text = Get-DevText $d
    $mi.Tag = $d.Serial
    $mi.Checked = ($d.Serial -eq $cur)
    $mi.Add_Click({ Set-Device ([string]$this.Tag) })
    $parent.DropDownItems.Add($mi) | Out-Null
}

function New-DisabledItem($parent, $text) {
    $mi = New-Object System.Windows.Forms.ToolStripMenuItem
    $mi.Text = $text
    $mi.Enabled = $false
    $parent.DropDownItems.Add($mi) | Out-Null
}

function Update-DeviceMenu($parent) {
    if (-not $parent) { $parent = $script:miDevice }
    # 记住是谁在打开这个菜单：托盘还是窗口。回调里重建同一份，免得刷错地方
    $script:devMenuParent = $parent
    $parent.DropDownItems.Clear()
    $devs = @()
    try { $devs = @(Get-SelectableDevices) } catch { }
    $cur = $script:settings.Serial
    if (-not $cur) { $cur = Get-Serial }
    $usbDevs = @($devs | Where-Object { $_.Kind -eq 'USB' })
    $wifiDevs = @($devs | Where-Object { $_.Kind -eq 'WiFi' })

    # ---- USB 有线 ----
    $miUsb = New-Object System.Windows.Forms.ToolStripMenuItem
    $miUsb.Text = 'USB 有线'
    if ($usbDevs.Count -eq 0) {
        New-DisabledItem $miUsb '未检测到（插线并开启 USB 调试）'
    } else {
        foreach ($d in $usbDevs) { Add-DevItem $miUsb $d $cur }
    }
    $parent.DropDownItems.Add($miUsb) | Out-Null

    # ---- WiFi 无线 ----
    $miWifi = New-Object System.Windows.Forms.ToolStripMenuItem
    $miWifi.Text = 'WiFi 无线'
    if ($wifiDevs.Count -eq 0) {
        New-DisabledItem $miWifi '当前没有在线的无线设备'
    } else {
        foreach ($d in $wifiDevs) { Add-DevItem $miWifi $d $cur }
    }

    # 记过地址但现在不在线的，列出来点一下就连
    $onlineSerials = @($devs | ForEach-Object { $_.Serial })
    $offline = @($script:settings.WifiTargets | Where-Object { $_ -and ($onlineSerials -notcontains $_) })
    if ($offline.Count -gt 0) {
        $miWifi.DropDownItems.Add((New-Object System.Windows.Forms.ToolStripSeparator)) | Out-Null
        foreach ($t in $offline) {
            $mi = New-Object System.Windows.Forms.ToolStripMenuItem
            $mi.Text = "$t   （点此连接）"
            $mi.Tag = $t
            $mi.Add_Click({
                $msg = Connect-Address ([string]$this.Tag)
                Update-DeviceMenu $script:devMenuParent
                Update-State
                if ($msg -notmatch 'connected to') { Notify '连接失败' $msg 'Warning' }
            })
            $miWifi.DropDownItems.Add($mi) | Out-Null
        }
    }
    $miWifi.DropDownItems.Add((New-Object System.Windows.Forms.ToolStripSeparator)) | Out-Null

    $miScan = New-Object System.Windows.Forms.ToolStripMenuItem
    $miScan.Text = '自动发现并连接'
    $miScan.Add_Click({
        $n = Connect-All-Mdns
        Update-DeviceMenu $script:devMenuParent
        Update-State
        Notify '无线发现' $(if ($n -gt 0) { "已连接 $n 个设备。" } else { '没发现可连的设备，确认手机和电脑在同一 Wi-Fi。' }) 'Info'
    })
    $miWifi.DropDownItems.Add($miScan) | Out-Null

    $miWifiAdd = New-Object System.Windows.Forms.ToolStripMenuItem
    $miWifiAdd.Text = '添加设备（IP:端口）…'
    $miWifiAdd.Add_Click({
        $addr = Show-Input "填手机地址，如 192.168.1.100:45329`n只填 IP 时默认补 :5555" '添加无线设备' ''
        if ($addr -eq $null -or $addr.Trim() -eq '') { return }
        $msg = Connect-Address $addr
        Update-DeviceMenu $script:devMenuParent
        Update-State
        [System.Windows.Forms.MessageBox]::Show($msg, '添加设备') | Out-Null
    })
    $miWifi.DropDownItems.Add($miWifiAdd) | Out-Null

    $miPair = New-Object System.Windows.Forms.ToolStripMenuItem
    $miPair.Text = '配对新设备…'
    $miPair.Add_Click({
        $hint = "手机上：开发者选项 → 无线调试 → 使用配对码配对设备`n填弹窗里的地址和 6 位码，弹窗保持打开"
        $r = Show-TwoInput '配对新设备' $hint 'IP:端口' '' '配对码' ''
        if (-not $r) { return }
        $msg = Pair-Address $r.A $r.B
        $n = Connect-All-Mdns
        Update-DeviceMenu $script:devMenuParent
        Update-State
        [System.Windows.Forms.MessageBox]::Show("$msg`n`n已自动连接 $n 个设备。", '配对结果') | Out-Null
    })
    $miWifi.DropDownItems.Add($miPair) | Out-Null
    $parent.DropDownItems.Add($miWifi) | Out-Null

    # ---- 蓝牙 ----
    # adb 只有 USB 和 TCP 两种通道，没有蓝牙，scrcpy 也就走不了蓝牙。
    $miBt = New-Object System.Windows.Forms.ToolStripMenuItem
    $miBt.Text = '蓝牙（不可用）'
    $miBt.Enabled = $false
    $miBt.ToolTipText = 'adb 不支持蓝牙传输，无法用于投屏和音频。请用 USB 或 WiFi。'
    $parent.DropDownItems.Add($miBt) | Out-Null

    $parent.DropDownItems.Add((New-Object System.Windows.Forms.ToolStripSeparator)) | Out-Null

    $miRename = New-Object System.Windows.Forms.ToolStripMenuItem
    $miRename.Text = '重命名此设备…'
    $miRename.Add_Click({
        $serial = $script:settings.Serial
        if (-not $serial) { $serial = Get-Serial }
        if (-not $serial) {
            [System.Windows.Forms.MessageBox]::Show('没有可用设备。', '重命名') | Out-Null
            return
        }
        $devs = @(Get-SelectableDevices)
        $curDev = $devs | Where-Object { $_.Serial -eq $serial } | Select-Object -First 1
        $key = if ($curDev -and $curDev.Model) { $curDev.Model } else { $serial }
        $names = Read-Names
        $old = if ($curDev -and $curDev.Name) { $curDev.Name } elseif ($names.ContainsKey($key)) { $names[$key] } else { '' }
        $hint = if ($curDev -and $curDev.Model) { "（原机型 $($curDev.Model)）" } else { '' }
        $new = Show-Input "给这台设备起个名字$hint`n留空并确定 = 恢复默认" '重命名设备' $old
        if ($new -eq $null) { return }
        if ($new.Trim() -eq '') {
            if ($names.ContainsKey($key)) { $names.Remove($key); Save-Names $names }
        } else {
            $names[$key] = $new.Trim()
            Save-Names $names
        }
        Update-DeviceMenu $script:devMenuParent
        Update-State
    })
    $parent.DropDownItems.Add($miRename) | Out-Null

    $miClean = New-Object System.Windows.Forms.ToolStripMenuItem
    $miClean.Text = '清理无效设备'
    $miClean.Add_Click({
        $n = Clear-BadDevices
        Update-DeviceMenu $script:devMenuParent
        Update-State
        $msg = if ($n -gt 0) { "已断开 $n 个无效/重复连接。" } else { '没有需要清理的设备。' }
        [System.Windows.Forms.MessageBox]::Show($msg, '清理完成') | Out-Null
    })
    $parent.DropDownItems.Add($miClean) | Out-Null

    $miClearPin = New-Object System.Windows.Forms.ToolStripMenuItem
    $miClearPin.Text = '忘记解锁密码（下次重新输）'
    $miClearPin.Add_Click({
        $serial = $script:settings.Serial
        if (-not $serial) { $serial = Get-Serial }
        if (-not $serial) { Notify '没有设备' '先连上手机' 'Warning'; return }
        Set-Pin $serial $null
        Notify '已忘记解锁密码' '下次唤醒会重新问一次' 'Info'
    })
    $parent.DropDownItems.Add($miClearPin) | Out-Null

    $miRefresh = New-Object System.Windows.Forms.ToolStripMenuItem
    $miRefresh.Text = '刷新列表'
    $miRefresh.Add_Click({ Update-DeviceMenu $script:devMenuParent })
    $parent.DropDownItems.Add($miRefresh) | Out-Null

    # 这两项放在设备菜单末尾，托盘和窗口菜单都能用上
    $miWakeDev = New-Object System.Windows.Forms.ToolStripMenuItem
    $miWakeDev.Text = '唤醒并解锁'
    $miWakeDev.Add_Click({
        $r = Wake-Unlock
        if ($r.Ok) { Notify '已解锁' $r.Msg 'Info' } else { Notify '没解锁' $r.Msg 'Warning' }
    })
    $parent.DropDownItems.Add($miWakeDev) | Out-Null

    $miReconnDev = New-Object System.Windows.Forms.ToolStripMenuItem
    $miReconnDev.Text = '重新连接设备'
    $miReconnDev.Add_Click({
        Connect-Saved
        Update-State
        if (-not $script:userStopped -and -not (Is-Running)) { [void](Start-Audio) }
    })
    $parent.DropDownItems.Add($miReconnDev) | Out-Null
}

$script:miDevice.Add_DropDownOpening({ Update-DeviceMenu $script:miDevice })

$menu.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator)) | Out-Null

$miStart = New-Object System.Windows.Forms.ToolStripMenuItem
$miStart.Text = '启动音频转发'
$miStart.Add_Click({
    $script:userStopped = $false
    if (-not (Start-Audio)) { Notify '没有设备' '先连手机：选择设备 → 重新连接设备' 'Warning' }
})
$menu.Items.Add($miStart) | Out-Null

$script:miStop = New-Object System.Windows.Forms.ToolStripMenuItem
$script:miStop.Text = '停止'
$script:miStop.Add_Click({
    $script:userStopped = $true
    $script:retryLeft = 0
    if ($script:retryTimer) { $script:retryTimer.Stop() }
    Stop-Audio
})
$menu.Items.Add($script:miStop) | Out-Null

# 暂停 / 继续手机上的媒体：真的把手机那头的播放停住，不是电脑端静音
$script:miPause = New-Object System.Windows.Forms.ToolStripMenuItem
$script:miPause.Text = '暂停 / 继续（手机）'
$script:miPause.Add_Click({ Send-MediaPause })
$menu.Items.Add($script:miPause) | Out-Null

$miHotkey = New-Object System.Windows.Forms.ToolStripMenuItem
$miHotkey.Text = '快捷键'

# ---- 暂停键 ----
$miPauseKey = New-Object System.Windows.Forms.ToolStripMenuItem
$miPauseKey.Text = '暂停 / 继续'

$script:miHotkeyEdit = New-Object System.Windows.Forms.ToolStripMenuItem
$script:miHotkeyEdit.Text = '修改…'
$script:miHotkeyEdit.Add_Click({
    $new = Show-HotkeyDialog '暂停'
    if ($new) { Set-Hotkey 'pause' $new }
    Update-HotkeyMenu
})
$miPauseKey.DropDownItems.Add($script:miHotkeyEdit) | Out-Null

$script:miHotkeyOff = New-Object System.Windows.Forms.ToolStripMenuItem
$script:miHotkeyOff.Text = '禁用'
$script:miHotkeyOff.Add_Click({
    Set-Hotkey 'pause' ''
    Notify '暂停键已禁用' '需要时在这里重新设置。' 'Info'
})
$miPauseKey.DropDownItems.Add($script:miHotkeyOff) | Out-Null

$miHotkeyReset = New-Object System.Windows.Forms.ToolStripMenuItem
$miHotkeyReset.Text = '恢复默认（Ctrl+Alt+P）'
$miHotkeyReset.Add_Click({
    Set-Hotkey 'pause' 'Ctrl+Alt+P'
    Notify '暂停键已恢复' '暂停 / 继续：Ctrl+Alt+P' 'Info'
})
$miPauseKey.DropDownItems.Add($miHotkeyReset) | Out-Null

$miHotkey.DropDownItems.Add($miPauseKey) | Out-Null

# ---- 投屏键 ----
$miScreenKey = New-Object System.Windows.Forms.ToolStripMenuItem
$miScreenKey.Text = '投屏开关'

$script:miHotkeyEditS = New-Object System.Windows.Forms.ToolStripMenuItem
$script:miHotkeyEditS.Text = '修改…'
$script:miHotkeyEditS.Add_Click({
    $new = Show-HotkeyDialog '投屏'
    if ($new) { Set-Hotkey 'screen' $new }
    Update-HotkeyMenu
})
$miScreenKey.DropDownItems.Add($script:miHotkeyEditS) | Out-Null

$script:miHotkeyOffS = New-Object System.Windows.Forms.ToolStripMenuItem
$script:miHotkeyOffS.Text = '禁用'
$script:miHotkeyOffS.Add_Click({
    Set-Hotkey 'screen' ''
    Notify '投屏键已禁用' '需要时在这里重新设置。' 'Info'
})
$miScreenKey.DropDownItems.Add($script:miHotkeyOffS) | Out-Null

$miHotkeyResetS = New-Object System.Windows.Forms.ToolStripMenuItem
$miHotkeyResetS.Text = '恢复默认（F7）'
$miHotkeyResetS.Add_Click({
    Set-Hotkey 'screen' 'F7'
    Notify '投屏键已恢复' '投屏开关：F7' 'Info'
})
$miScreenKey.DropDownItems.Add($miHotkeyResetS) | Out-Null

$miHotkey.DropDownItems.Add($miScreenKey) | Out-Null

$menu.Items.Add($miHotkey) | Out-Null

$menu.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator)) | Out-Null

# 投屏开关
$script:miScreen = New-Object System.Windows.Forms.ToolStripMenuItem
$script:miScreen.Text = '显示手机画面（投屏）'
$script:miScreen.Add_Click({ Set-Screen (-not $script:settings.Screen) })
$menu.Items.Add($script:miScreen) | Out-Null

# 投屏窗口置顶：可选，只在投屏时有效
$script:miOnTop = New-Object System.Windows.Forms.ToolStripMenuItem
$script:miOnTop.Text = '　　投屏窗口置顶'
$script:miOnTop.Add_Click({ Set-OnTop (-not $script:settings.OnTop) })
$menu.Items.Add($script:miOnTop) | Out-Null

# 窗口模式：程序窗口 / scrcpy 原生窗口
$script:miModeEmbedded = New-Object System.Windows.Forms.ToolStripMenuItem
$script:miModeEmbedded.Text = '程序窗口'
$script:miModeEmbedded.Add_Click({ Set-WindowMode 'embedded' })
$script:miModeNative = New-Object System.Windows.Forms.ToolStripMenuItem
$script:miModeNative.Text = 'scrcpy 原生窗口'
$script:miModeNative.Add_Click({ Set-WindowMode 'native' })
$miMode = New-Object System.Windows.Forms.ToolStripMenuItem
$miMode.Text = '窗口模式'
$miMode.DropDownItems.Add($script:miModeEmbedded) | Out-Null
$miMode.DropDownItems.Add($script:miModeNative) | Out-Null
$menu.Items.Add($miMode) | Out-Null

# 重置横竖屏记忆的窗口尺寸，下次按屏幕默认重算
$script:miResetSize = New-Object System.Windows.Forms.ToolStripMenuItem
$script:miResetSize.Text = '　　重置窗口尺寸'
$script:miResetSize.Add_Click({
    $script:settings.SizePortrait = @()
    $script:settings.SizeLandscape = @()
    Save-Settings
    if ($script:settings.Screen -and -not (Is-NativeMode)) { Adjust-WindowToVideo }
    Notify '窗口尺寸已重置' '下次切换方向时按屏幕重新计算。' 'Info'
})
$menu.Items.Add($script:miResetSize) | Out-Null

# 投屏帧数：高了更顺但是编码压力大，低了省电但画面不跟手。默认 60。
$script:miFps = New-Object System.Windows.Forms.ToolStripMenuItem
$script:miFps.Text = '　　投屏帧数'
$script:fpsItems = @{}
foreach ($pair in @(@(30, '30 帧（省电）'), @(60, '60 帧（推荐）'), @(90, '90 帧'), @(120, '120 帧'), @(144, '144 帧'), @(0, '不限（跟手机刷新率）'))) {
    $f = $pair[0]; $label = $pair[1]
    $mi = New-Object System.Windows.Forms.ToolStripMenuItem
    $mi.Text = $label
    $mi.Tag = $f
    $mi.Add_Click({ Set-MaxFps ([int]$this.Tag) })
    $script:fpsItems[$f] = $mi
    $script:miFps.DropDownItems.Add($mi) | Out-Null
}
$menu.Items.Add($script:miFps) | Out-Null

$miWake = New-Object System.Windows.Forms.ToolStripMenuItem
$miWake.Text = '唤醒并解锁'
$miWake.Add_Click({
    $r = Wake-Unlock
    if ($r.Ok) { Notify '已解锁' $r.Msg 'Info' }
    else { Notify '没解锁' $r.Msg 'Warning' }
})
$menu.Items.Add($miWake) | Out-Null

$menu.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator)) | Out-Null

# 延迟
$miDelay = New-Object System.Windows.Forms.ToolStripMenuItem
$miDelay.Text = '缓冲 / 延迟'
$script:bufItems = @{}
foreach ($pair in @(@(40, '低延迟  40ms'), @(60, '标准  60ms'), @(100, '稳定  100ms'))) {
    $ms = $pair[0]; $label = $pair[1]
    $mi = New-Object System.Windows.Forms.ToolStripMenuItem
    $mi.Text = $label
    $mi.Tag = $ms
    $mi.Add_Click({ Set-Buffer ([int]$this.Tag) })
    $script:bufItems[$ms] = $mi
    $miDelay.DropDownItems.Add($mi) | Out-Null
}
$menu.Items.Add($miDelay) | Out-Null

# 编码
$miCodec = New-Object System.Windows.Forms.ToolStripMenuItem
$miCodec.Text = '音频格式'
$script:codecItems = @{}
foreach ($pair in @(@('raw', '无损 PCM（延迟最低）'), @('opus', 'Opus（抗抖动，省带宽）'))) {
    $code = $pair[0]; $label = $pair[1]
    $mi = New-Object System.Windows.Forms.ToolStripMenuItem
    $mi.Text = $label
    $mi.Tag = $code
    $mi.Add_Click({ Set-Codec ([string]$this.Tag) })
    $script:codecItems[$code] = $mi
    $miCodec.DropDownItems.Add($mi) | Out-Null
}
$menu.Items.Add($miCodec) | Out-Null

$menu.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator)) | Out-Null

$miReconnect = New-Object System.Windows.Forms.ToolStripMenuItem
$miReconnect.Text = '重新连接设备'
$miReconnect.Add_Click({
    Connect-Saved
    Update-State
    if (-not $script:userStopped -and -not (Is-Running)) { [void](Start-Audio) }
})
$menu.Items.Add($miReconnect) | Out-Null

$miLog = New-Object System.Windows.Forms.ToolStripMenuItem
$miLog.Text = '查看日志'
$miLog.Add_Click({
    if (Test-Path $logFile) { Start-Process notepad.exe $logFile }
    else { [System.Windows.Forms.MessageBox]::Show('暂无日志', '日志') | Out-Null }
})
$menu.Items.Add($miLog) | Out-Null

$menu.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator)) | Out-Null

$miExit = New-Object System.Windows.Forms.ToolStripMenuItem
$miExit.Text = '退出'
$miExit.Add_Click({
    $script:reallyExit = $true
    Stop-Audio
    try { if ($script:hkWin) { $script:hkWin.Unregister($script:hkId); $script:hkWin.Unregister($script:hkScreenId) } } catch { }
    try { if ($script:mainForm) { $script:mainForm.Close() } } catch { }
    $script:ni.Visible = $false
    $script:ni.Dispose()
    [System.Windows.Forms.Application]::Exit()
})
$menu.Items.Add($miExit) | Out-Null

$script:ni.ContextMenuStrip = $menu
# 双击：没在跑就启动；在跑但窗口藏起来了就把窗口叫出来；窗口已经在就先停掉
$script:ni.add_MouseDoubleClick({
    if (Is-Running) {
        if ($script:mainForm -and $script:mainForm.Visible) {
            $script:userStopped = $true
            $script:retryLeft = 0
            if ($script:retryTimer) { $script:retryTimer.Stop() }
            Stop-Audio
        } else {
            Show-MainWindow
        }
    } else {
        $script:userStopped = $false
        if (-not (Start-Audio)) { Notify '没有设备' '先连手机：选择设备 → 重新连接设备' 'Warning' }
    }
})

# ---------------- 状态刷新 ----------------
# 设备是不是真的睡下去了。睡下去后不再推帧，投屏窗口会停在最后一帧，看着还像在投屏。
$script:sleepStreak = 0
$script:deviceAsleep = $false

# 当前设备和 adb 报的形态对不上时自动择优：插了线走有线，拔了线回无线。
# 只在形态真的变了才动，避免每秒钟来回切。
$script:pollTick = 0
$script:lastTransportSwitch = [DateTime]::MinValue
$script:manualSerial = $null

function Restart-Transport($msg) {
    $was = Restart-IfRunning
    if ($was) { [void](Start-Audio -SkipUnlock) }
    Notify '连接方式已切换' $msg 'Info'
    try { Update-DeviceMenu $script:miDevice } catch { }
}

function Update-Transport {
    if (((Get-Date) - $script:lastTransportSwitch).TotalSeconds -lt 5) { return }
    $cur = $script:settings.Serial
    if (-not $cur) { return }
    $devs = @(Get-Devices)
    if ($devs.Count -eq 0) { return }
    $curDev = $devs | Where-Object { $_.Serial -eq $cur } | Select-Object -First 1

    if (-not $curDev) {
        # 手上的设备没了，改用还活着的设备（USB 优先）
        $script:settings.Serial = $null
        $new = Get-Serial
        if ($new) {
            $script:lastTransportSwitch = Get-Date
            Restart-Transport "原连接已断开，改走 $(if ((Get-ConnKind $new) -eq 'USB') { '有线' } else { 'WiFi' })"
        }
        return
    }

    # 走 WiFi 时插上了同机型的 USB：有线更稳，切过去。
    # 用户亲手选的设备不抢（下面这句在上面 curDev 为空时不会走到）。
    if ($cur -eq $script:manualSerial) { return }
    if ($curDev.Kind -eq 'WiFi' -and $curDev.Model) {
        $usb = $devs | Where-Object { $_.Kind -eq 'USB' -and $_.Model -eq $curDev.Model } | Select-Object -First 1
        if ($usb) {
            $script:settings.Serial = $usb.Serial
            $script:lastTransportSwitch = Get-Date
            Restart-Transport '检测到 USB 已插入，已切换到有线连接'
        }
    }
}

function Update-State {
    param([switch]$PollDevice)

    $running = Is-Running

    # 别的投屏被关掉后，Android 会把手机上的音频通路一起收走，自己这一路就变成「有画面没声音」。
    # 每秒数一次别的 scrcpy 实例，发现变少了就重启声音那一路，投屏画面一动不动。
    if ($PollDevice) {
        # 每 5 秒看一次连接形态（插拔线），不用每秒都跑 adb devices
        $script:pollTick++
        if ($script:pollTick -ge 5) {
            $script:pollTick = 0
            Update-Transport
        }
        if ($running) {
            $others = Get-OtherScrcpyCount
            if ($others -lt $script:otherCount) {
                # 有人把投屏关了，等两拍再重连，给系统一点时间释放音频通路
                $script:audioRestartPending = 2
            }
            $script:otherCount = $others
            if ($script:audioRestartPending -gt 0) {
                $script:audioRestartPending--
                if ($script:audioRestartPending -eq 0) {
                    # 声音和画面在同一个进程里，没法只重启声音，只能整路重来。
                    # 代价是画面闪断一两秒，但至少声音一定接得回来。
                    if (Restart-IfRunning) {
                        [void](Start-Audio -SkipUnlock)
                        Notify '声音已恢复' '另一路投屏已关闭，声音已重新接上。' 'Info'
                    }
                }
            }
        } else {
            $script:otherCount = 0
            $script:audioRestartPending = 0
        }
    }

    if ($PollDevice -and $running -and $script:settings.Screen) {
        # 连续两次读到非醒着才判定，避免瞬时抖动误报
        # 脚本每秒执行，直接用手上已有的 serial，不再每次跑 adb devices
        $serial = $script:settings.Serial
        if (-not $serial) { $serial = Get-Serial }
        # 手机横竖屏变化时重排窗口，画面完整显示。每 2 秒查一次，别每秒都跑 dumpsys。
        $script:orientTick++
        if ($script:orientTick -ge 2) {
            $script:orientTick = 0
            if ($serial) { Update-Orientation $serial }
        }
        $st = $null
        if ($serial) { $st = Get-DeviceState $serial }
        if ($st -and $st.Valid) {
            if ($st.On) {
                $script:sleepStreak = 0
                if ($script:deviceAsleep) {
                    $script:deviceAsleep = $false
                    Hide-SleepOverlay
                }
                # 顺手记住窗口位置，睡下去时黑屏才能贴准
                $rr = Get-ScrcpyRect
                if ($rr) { $script:sleepLastRect = $rr }
            } else {
                $script:sleepStreak++
                if ($script:sleepStreak -ge 2 -and -not $script:deviceAsleep) {
                    $script:deviceAsleep = $true
                    Show-SleepOverlay
                    # 取不到窗口句柄（或黑屏没显示出来）时，退回托盘通知兜底
                    if (-not ($script:sleepForm -and $script:sleepForm.Visible)) {
                        Notify '手机已息屏' '投屏画面已停止更新。右键托盘「唤醒并解锁」可以恢复。' 'Warning'
                    }
                }
            }
        }
    } elseif (-not $running) {
        $script:sleepStreak = 0
        $script:deviceAsleep = $false
        Hide-SleepOverlay
    }

    # 置顶的窗口会把提示层盖住，每秒顶回来一次
    if ($script:deviceAsleep) {
        if ($script:sleepInner -and $script:sleepInner.Visible) { $script:sleepInner.BringToFront() }
        elseif ($script:sleepForm -and $script:sleepForm.Visible) { $script:sleepForm.BringToFront() }
    }

    $fmt = if ($script:settings.Codec -eq 'raw') { '无损' } else { 'Opus' }
    $fps = if (-not $script:settings.Screen) { '' } elseif ($script:settings.MaxFps -gt 0) { "  $($script:settings.MaxFps)fps" } else { '  不限帧' }
    $dev = if ($script:devLabel) { $script:devLabel } else { [string]$script:settings.Serial }
    $mode = if ($script:settings.Screen) { '投屏' } else { '纯音频' }
    if ($running) {
        $script:miStart.Enabled = $false
        $script:miStop.Enabled = $true
        if ($script:deviceAsleep) {
            $script:miStatus.Text = "● 画面已冻结  |  $mode  |  手机已息屏"
            $script:ni.Text = '手机音频 - 手机已息屏（投屏已停更）'
        } else {
            $script:miStatus.Text = "● 运行中  |  $mode  |  $fmt  $($script:settings.Buffer)ms$fps"
            $script:ni.Text = "手机音频 - 运行中 ($mode $fmt $($script:settings.Buffer)ms$fps)"
        }
    } else {
        $script:miStatus.Text = "○ 已停止  |  $mode  |  $fmt  $($script:settings.Buffer)ms$fps"
        $script:miStart.Enabled = $true
        $script:miStop.Enabled = $false
        $script:ni.Text = '手机音频 - 已停止'
    }
    if ($dev -and $dev.Length -le 40) { $script:ni.Text = "$($script:ni.Text)`n$dev" }
    if ($script:miScreen) { $script:miScreen.Checked = [bool]$script:settings.Screen }
    if ($script:miModeEmbedded) { $script:miModeEmbedded.Checked = -not (Is-NativeMode) }
    if ($script:miModeNative) { $script:miModeNative.Checked = Is-NativeMode }
    if ($script:miResetSize) { $script:miResetSize.Enabled = -not (Is-NativeMode) }
    if ($script:miOnTop) {
        $script:miOnTop.Checked = [bool]$script:settings.OnTop
        # 纯音频没有窗口，置顶没有意义，直接禁用，避免勾了不生效
        $script:miOnTop.Enabled = [bool]$script:settings.Screen
    }
    if ($script:miFps) {
        foreach ($k in $script:fpsItems.Keys) { $script:fpsItems[$k].Checked = ($k -eq $script:settings.MaxFps) }
        # 纯音频不推画面，帧率无意义
        $script:miFps.Enabled = [bool]$script:settings.Screen
    }
    # 息屏后画面是死的，图标不能还亮着绿色，否则和「运行中」分不出来
    $icon = if ($running -and -not $script:deviceAsleep) { $script:icoActive } else { $script:icoIdle }
    if ($icon) { $script:ni.Icon = $icon }
    foreach ($k in $script:bufItems.Keys) { $script:bufItems[$k].Checked = ($k -eq $script:settings.Buffer) }
    foreach ($k in $script:codecItems.Keys) { $script:codecItems[$k].Checked = ($k -eq $script:settings.Codec) }
    Sync-WindowMenu
}

Update-State

$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 1000
$timer.Add_Tick({ Update-State -PollDevice })
$timer.Start()

# ---------------- 启动即开始转发 ----------------
# 菜单建好才能把当前快捷键写到菜单上
Update-HotkeyMenu

# 自检：只构建设备菜单并打印，不启动转发、不常驻、不占用热键。用于改完菜单后验证。
if ($SelfTest) {
    Update-DeviceMenu $script:miDevice
    $lines = @()
    foreach ($top in $miDevice.DropDownItems) {
        if ($top -is [System.Windows.Forms.ToolStripSeparator]) { $lines += '  ----'; continue }
        $mark = if ($top.Checked) { '[x]' } elseif ($top.Enabled) { '[ ]' } else { '[-]' }
        $lines += "$mark $($top.Text)"
        if ($top.DropDownItems.Count -gt 0) {
            foreach ($sub in $top.DropDownItems) {
                if ($sub -is [System.Windows.Forms.ToolStripSeparator]) { $lines += '      ----'; continue }
                $sm = if ($sub.Checked) { '[x]' } elseif ($sub.Enabled) { '[ ]' } else { '[-]' }
                $lines += "      $sm $($sub.Text)"
            }
        }
    }
    $lines | ForEach-Object { [Console]::Out.WriteLine($_) }
    [Console]::Out.WriteLine("--- settings.WifiTargets = " + (@($script:settings.WifiTargets) -join ', '))
    return
}

# 热键窗口：注册当前快捷键，按下就往手机发媒体键
if ($script:hkAvailable) {
    try {
        $script:hkWin = New-Object TrayHotkeyWindow
        $script:hkWin.add_HotkeyPressed({ Send-MediaPause })
        $script:hkWin.add_ScreenTogglePressed({ Invoke-ScreenToggle })
    } catch {
        $script:hkWin = $null
    }
}
Register-Hotkeys

$script:retryTimer = New-Object System.Windows.Forms.Timer
$script:retryTimer.Interval = 4000
$script:retryTimer.Add_Tick({
    if ($script:userStopped -or (Is-Running) -or $script:retryLeft -le 0) { $script:retryTimer.Stop(); return }
    $script:retryLeft--
    Connect-Saved
    [void](Start-Audio)
})

$script:autoTimer = New-Object System.Windows.Forms.Timer
$script:autoTimer.Interval = 600
$script:autoTimer.Add_Tick({
    $script:autoTimer.Stop()
    Connect-Saved
    if (-not (Start-Audio)) {
        $script:retryLeft = 2
        $script:retryTimer.Start()
        Notify '还没找到手机' '后台继续重试，连上会自动开始'
    }
})
$script:autoTimer.Start()

$ctx = New-Object System.Windows.Forms.ApplicationContext
[System.Windows.Forms.Application]::Run($ctx)

$timer.Stop()
$script:ni.Visible = $false

}
catch {
    $_ | Out-File -FilePath $logFile -Encoding utf8 -Append
    [System.Windows.Forms.MessageBox]::Show("$($_.Exception.Message)`n`n详情见: $logFile", '托盘程序出错') | Out-Null
}
finally {
    # 异常退出也要把「不自动息屏」改回去，否则手机屏幕会一直亮着
    try { if ($script:origTimeout) { Pop-KeepAwake } } catch { }
    try { if ($script:hkWin) { $script:hkWin.Unregister($script:hkId); $script:hkWin.Unregister($script:hkScreenId) } } catch { }
    try { $mutex.ReleaseMutex() } catch { }
}
