@echo off
setlocal
cd /d "%~dp0"

echo [1/2] Restore and build (Release) ...
dotnet build src\DiskMonitor.csproj -c Release || goto :err

echo [2/2] Launch (requires admin, UAC may prompt) ...
dotnet run --project src\DiskMonitor.csproj -c Release --no-build || goto :err
goto :eof

:err
echo.
echo Build or launch failed. Please check:
echo   1) .NET 8 SDK installed  (dotnet --list-sdks)
echo   2) network access for NuGet restore
echo   3) running as administrator
echo.
pause
exit /b 1
