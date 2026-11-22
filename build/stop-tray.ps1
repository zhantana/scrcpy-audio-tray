# 安装/卸载前清理：停掉本程序自己的托盘和投屏进程，避免文件被占用
# 安装时从临时目录调用（本目录没有主脚本），此时清理所有同名实例；
# 卸载时从安装目录调用，只清理本目录这一份，不碰用户手上的其他副本，也不杀全局 adb 服务。
$ErrorActionPreference = 'SilentlyContinue'

$here = $PSScriptRoot
$scoped = Test-Path (Join-Path $here 'scrcpy-audio-tray.ps1')

$trays = Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" |
    Where-Object { $_.CommandLine -like '*scrcpy-audio-tray.ps1*' }
if ($scoped) {
    $trays = $trays | Where-Object { $_.CommandLine -like "*$here*" }
}
$trays | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }

$engine = Get-CimInstance Win32_Process -Filter "Name='scrcpy.exe' or Name='adb.exe'"
if ($scoped) {
    $engine = $engine | Where-Object { $_.ExecutablePath -like "$here\*" }
}
$engine | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }

Start-Sleep -Milliseconds 700
