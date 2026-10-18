@echo off
cd /d "%~dp0"
if not exist "publish\DiskMonitor.exe" (
    echo publish\DiskMonitor.exe not found.
    echo Please run build-and-run.cmd first, or re-publish.
    pause
    exit /b 1
)
start "" "publish\DiskMonitor.exe"
