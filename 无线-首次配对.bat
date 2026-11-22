@echo off
chcp 936 >nul
title 无线配对（只需一次）
setlocal enabledelayedexpansion

set "ADB="
if exist "%~dp0scrcpy\adb.exe" set "ADB=%~dp0scrcpy\adb.exe"
if not defined ADB for /f "delims=" %%i in ('where adb.exe 2^>nul') do set "ADB=%%i"
if not defined ADB (
  for /d %%a in ("%LOCALAPPDATA%\Microsoft\WinGet\Packages\Genymobile.scrcpy_*") do (
    for /d %%b in ("%%~a\scrcpy-win64-*") do (
      if exist "%%~b\adb.exe" set "ADB=%%~b\adb.exe"
    )
  )
)
if not defined ADB (
  echo [错误] 找不到 adb.exe，请先安装: winget install Genymobile.scrcpy
  pause & exit /b 1
)

echo ============================================================
echo   无线配对（一次性，之后永远不用插线）
echo ============================================================
echo.
echo  手机操作：
echo    1. 设置 - 系统 - 开发者选项 - 打开「无线调试」
echo    2. 进入「无线调试」页面 - 点「使用配对码配对设备」
echo    3. 弹出框里有两样东西：IP地址和端口 / 6位配对码
echo    4. 手机与电脑需连同一个 Wi-Fi
echo.
echo  注意: 配对弹窗一旦关闭，配对码就失效，必须重新点开。
echo ------------------------------------------------------------
echo.

echo [1/4] 填写配对信息
set "PAIRADDR="
set /p PAIRADDR=  配对弹窗里的「IP地址和端口」(形如 192.168.1.xxx:37123):
if "%PAIRADDR%"=="" (echo  未输入，退出。& pause & exit /b 1)
echo.

echo [2/4] 开始配对
echo  adb 提示 Enter pairing code 时，输入手机上的 6 位数字后回车
echo  （输入时不显示字符，是正常的）
echo.
"%ADB%" pair %PAIRADDR%
if errorlevel 1 (
  echo.
  echo ============================================================
  echo  [失败] 配对没有成功。请逐条核对：
  echo    - 配对码是否输错或超时（弹窗关掉就要重新点开）
  echo    - 填的是「配对」弹窗里的端口，不是「无线调试」主页的端口
  echo    - 手机和电脑是否在同一网段
  echo  修正后重新双击本脚本。
  echo ============================================================
  pause & exit /b 1
)

echo.
echo [3/4] 配对成功，正在自动查找连接端口 ...
"%ADB%" mdns services 2>nul | findstr /c:"_adb-tls-connect._tcp" > "%TEMP%\scrcpy_mdns.txt"
set "FOUND="
for /f "usebackq tokens=3" %%s in ("%TEMP%\scrcpy_mdns.txt") do (
  if not defined FOUND (
    set "FOUND=%%s"
    echo  发现设备地址: %%s
    "%ADB%" connect %%s
  )
)
if not defined FOUND (
  echo  未收到广播，请回到手机「无线调试」主页面抄下 IP:端口
  set "MANUAL="
  set /p MANUAL=  手动输入 IP:端口（直接回车放弃）:
  if not "!MANUAL!"=="" (
    "%ADB%" connect !MANUAL!
    set "FOUND=!MANUAL!"
  )
)

echo.
echo [4/4] 连接自检 ...
timeout /t 2 /nobreak >nul 2>&1
"%ADB%" devices > "%TEMP%\scrcpy_dev.txt" 2>&1
type "%TEMP%\scrcpy_dev.txt"
findstr /r /c:"device$" "%TEMP%\scrcpy_dev.txt" >nul 2>&1
if errorlevel 1 (
  echo.
  echo [警告] 设备还没就绪。可能是手机端授权弹窗没点「允许」，
  echo        或者手机「无线调试」刚被关闭。处理后重跑本脚本即可。
  pause & exit /b 1
)

if defined FOUND (
  >"%~dp0wifi_target.txt" echo !FOUND!
  echo.
  echo 已保存连接地址: !FOUND!
)

echo.
echo ============================================================
echo  完成。以后直接双击「无线音频-游戏模式.bat」。
echo  手机重启后若「无线调试」被关闭，重新打开开关即可，
echo  端口变了也没关系，脚本会自动发现。
echo ============================================================
echo.
pause
