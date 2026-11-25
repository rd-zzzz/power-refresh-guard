@echo off
rem ASCII-only on purpose: cmd.exe parses .bat using the OEM codepage,
rem so non-ASCII text here breaks line parsing. All Chinese output comes
rem from install.ps1, which PowerShell decodes correctly.
setlocal EnableExtensions
set "HERE=%~dp0"
set "HERE=%HERE:~0,-1%"

echo ================================================
echo   RefreshGuard - START / enable logon autostart
echo ================================================
echo.
echo Dir: %HERE%
echo.

if not exist "%HERE%\RefreshGuard.exe" goto :noexe
if not exist "%HERE%\install.ps1" goto :nops

set "PS=powershell"
where pwsh >nul 2>nul && set "PS=pwsh"

echo [1/2] Registering scheduled task + logon Run key...
echo.
"%PS%" -NoProfile -ExecutionPolicy Bypass -File "%HERE%\install.ps1" -InstallDir "%HERE%"
if errorlevel 1 goto :fail

echo.
echo [2/2] Aligning refresh rate once (silent)...
"%HERE%\RefreshGuard.exe" --auto --silent
echo Triggered. See the newest file under logs\ for the result.
goto :end

:noexe
echo [ERROR] RefreshGuard.exe not found in: %HERE%
goto :end
:nops
echo [ERROR] install.ps1 not found in: %HERE%
goto :end
:fail
echo.
echo [ERROR] Registration failed. See messages above.
:end
echo.
pause
endlocal