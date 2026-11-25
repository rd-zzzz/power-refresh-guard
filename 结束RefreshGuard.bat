@echo off
rem ASCII-only on purpose: cmd.exe parses .bat using the OEM codepage,
rem so non-ASCII text here breaks line parsing. All Chinese output comes
rem from install.ps1, which PowerShell decodes correctly.
setlocal EnableExtensions
set "HERE=%~dp0"
set "HERE=%HERE:~0,-1%"

echo ================================================
echo   RefreshGuard - STOP / disable logon autostart
echo ================================================
echo.
echo This will:
echo   1. Kill any running RefreshGuard instance
echo   2. Remove scheduled task "RefreshGuard"
echo   3. Remove logon Run key "RefreshGuard"
echo.
echo config.ini / state.ini / logs are kept. Click START to restore.
echo.
choice /c YN /m "Proceed? [Y/N]"
if errorlevel 2 goto :cancel

if not exist "%HERE%\install.ps1" goto :nops

set "PS=powershell"
where pwsh >nul 2>nul && set "PS=pwsh"

echo.
echo [1/2] Killing running instance...
taskkill /IM RefreshGuard.exe /T /F >nul 2>nul
if errorlevel 1 (echo No running instance found.) else (echo Killed.)

echo.
echo [2/2] Removing autostart + scheduled task...
echo.
"%PS%" -NoProfile -ExecutionPolicy Bypass -File "%HERE%\install.ps1" -Uninstall -InstallDir "%HERE%"
goto :end

:nops
echo [ERROR] install.ps1 not found in: %HERE%
goto :end
:cancel
echo.
echo Cancelled. Nothing was changed.
:end
echo.
pause
endlocal