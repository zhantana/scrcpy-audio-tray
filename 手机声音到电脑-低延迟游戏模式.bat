@echo off
chcp 936 >nul
title Phone Audio -^> PC Headset (Low Latency)
setlocal enabledelayedexpansion

set "SC="
if exist "%~dp0scrcpy\scrcpy.exe" set "SC=%~dp0scrcpy\scrcpy.exe"
if not defined SC for /f "delims=" %%i in ('where scrcpy.exe 2^>nul') do set "SC=%%i"
if not defined SC (
  for /d %%a in ("%LOCALAPPDATA%\Microsoft\WinGet\Packages\Genymobile.scrcpy_*") do (
    for /d %%b in ("%%~a\scrcpy-win64-*") do (
      if exist "%%~b\scrcpy.exe" set "SC=%%~b\scrcpy.exe"
    )
  )
)
if not defined SC (
  echo [ERROR] scrcpy not found. Run: winget install Genymobile.scrcpy
  pause & exit /b 1
)

echo ============================================================
echo  Phone audio -^> PC headset   [game mode, audio only]
echo ============================================================
echo  Require: USB cable + Developer options / USB debugging ON
echo          Android 11 or newer
echo  Exit: close this window
echo ============================================================
echo.

"%SC%" --no-video --no-window ^
  --audio-source=output ^
  --audio-codec=raw ^
  --audio-buffer=25 ^
  --audio-output-buffer=10 ^
  --require-audio

rem restore codepage after scrcpy
chcp 936 >nul

echo.
echo [Disconnected] Press any key to close.
pause >nul
