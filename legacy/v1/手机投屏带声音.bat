@echo off
chcp 936 >nul
title Phone Mirror + Audio (with sound)
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
echo  Phone mirror + audio   [30fps, balanced]
echo ============================================================
echo  Require: USB cable + Developer options / USB debugging ON
echo ============================================================
echo.

"%SC%" --max-fps=30 --video-bit-rate=4M --max-size=1280 ^
  --audio-source=output ^
  --audio-codec=raw ^
  --audio-output-buffer=20 ^
  --require-audio

rem restore codepage after scrcpy
chcp 936 >nul

echo.
echo [Disconnected] Press any key to close.
pause >nul
