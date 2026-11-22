Set sh = CreateObject("Shell.Application")
base = Left(WScript.ScriptFullName, InStrRev(WScript.ScriptFullName, "\"))
args = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File """ & base & "scrcpy-audio-tray.ps1"""
sh.ShellExecute "powershell.exe", args, base, "runas", 0
