@echo off
chcp 936 >nul
title 手机声音 -^> 电脑耳机（无线 / 游戏模式）
setlocal enabledelayedexpansion

set "SC="
if exist "%~dp0scrcpy\scrcpy.exe" set "SC=%~dp0scrcpy\scrcpy.exe"
set "ADB="
if exist "%~dp0scrcpy\adb.exe" set "ADB=%~dp0scrcpy\adb.exe"
if not defined SC for /f "delims=" %%i in ('where scrcpy.exe 2^>nul') do set "SC=%%i"
if not defined ADB for /f "delims=" %%i in ('where adb.exe 2^>nul') do set "ADB=%%i"
if not defined SC (
  for /d %%a in ("%LOCALAPPDATA%\Microsoft\WinGet\Packages\Genymobile.scrcpy_*") do (
    for /d %%b in ("%%~a\scrcpy-win64-*") do (
      if exist "%%~b\scrcpy.exe" set "SC=%%~b\scrcpy.exe"
      if exist "%%~b\adb.exe" set "ADB=%%~b\adb.exe"
    )
  )
)
if not defined SC (
  echo [错误] 找不到 scrcpy.exe，请先安装: winget install Genymobile.scrcpy
  pause & exit /b 1
)

echo ============================================================
echo   手机声音 -^> 电脑耳机   [无线 / 游戏模式]
echo ============================================================
echo  关闭本窗口即断开
echo ============================================================
echo.

echo [1/3] 连接手机 ...
set "SAVED="
if exist "%~dp0wifi_target.txt" set /p SAVED=<"%~dp0wifi_target.txt"
if defined SAVED (
  "%ADB%" connect !SAVED! >nul 2>&1
  echo  已连接 !SAVED!
)

timeout /t 1 /nobreak >nul 2>&1
"%ADB%" devices > "%TEMP%\scrcpy_dev.txt" 2>&1
findstr /r /c:"device$" "%TEMP%\scrcpy_dev.txt" > "%TEMP%\scrcpy_ok.txt" 2>nul
set "ONLINE="
for /f "usebackq tokens=1" %%s in ("%TEMP%\scrcpy_ok.txt") do (
  if not defined ONLINE set "ONLINE=%%s"
)

if not defined ONLINE (
  echo [2/3] 未在线，自动发现 ...
  "%ADB%" mdns services 2>nul | findstr /c:"_adb-tls-connect._tcp" > "%TEMP%\scrcpy_mdns.txt"
  set "FOUND="
  for /f "usebackq tokens=3" %%s in ("%TEMP%\scrcpy_mdns.txt") do (
    if not defined FOUND (
      set "FOUND=%%s"
      echo  发现 !FOUND!
      "%ADB%" connect !FOUND! >nul 2>&1
    )
  )
  if not defined FOUND echo  没发现设备
  timeout /t 2 /nobreak >nul 2>&1
  "%ADB%" devices > "%TEMP%\scrcpy_dev.txt" 2>&1
  findstr /r /c:"device$" "%TEMP%\scrcpy_dev.txt" > "%TEMP%\scrcpy_ok.txt" 2>nul
  for /f "usebackq tokens=1" %%s in ("%TEMP%\scrcpy_ok.txt") do (
    if not defined ONLINE set "ONLINE=%%s"
  )
) else (
  echo [2/3] 已在线
)

if not defined ONLINE (
  echo.
  echo [3/3] 没有可用设备，已停止。
  echo  打不开「无线调试」，或配对失效，请先跑「无线-首次配对.bat」
  echo.
  pause & exit /b 1
)

rem 优先用 IP:端口 形式的序列号，避开 mDNS 服务名那条重复条目
findstr /r /c:"^[0-9][0-9.]*:[0-9][0-9]*" "%TEMP%\scrcpy_dev.txt" > "%TEMP%\scrcpy_pick.txt" 2>nul
set "SERIAL="
for /f "usebackq tokens=1" %%s in ("%TEMP%\scrcpy_pick.txt") do (
  if not defined SERIAL set "SERIAL=%%s"
)
if not defined SERIAL set "SERIAL=!ONLINE!"

echo [3/3] 启动音频转发 ^(设备 !SERIAL!^)
echo.

rem opus 抗抖动；听到断续把 --audio-output-buffer 调到 40~50
"%SC%" -s !SERIAL! ^
  --no-video --no-window ^
  --audio-source=output ^
  --audio-codec=raw ^
  --audio-buffer=40 ^
  --audio-output-buffer=10 ^
  --require-audio

rem restore codepage after scrcpy
chcp 936 >nul

echo.
echo 已断开。
echo.
echo 按任意键关闭。
pause >nul
