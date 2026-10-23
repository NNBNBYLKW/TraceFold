@echo off
setlocal EnableExtensions
set "HERE=%~dp0"
if "%HERE:~-1%"=="\" set "HERE=%HERE:~0,-1%"
set "EXIT_CODE=4"
rem Package layout: this file sits at the toolkit root and the helper under scripts.
rem Source layout: this file sits in scripts next to the helper.
if exist "%HERE%\scripts\Run-Stage5CC-Acceptance.ps1" (
    set "HELPER=%HERE%\scripts\Run-Stage5CC-Acceptance.ps1"
) else (
    set "HELPER=%HERE%\Run-Stage5CC-Acceptance.ps1"
)
set "PS51=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if not exist "%PS51%" goto no_powershell
if not exist "%HELPER%" goto no_helper
rem A PowerShell 7 parent exports a PSModulePath that Windows PowerShell 5.1 cannot resolve, and
rem Get-FileHash then disappears. Reset it to the system locations for this child only.
set "PSModulePath=%ProgramFiles%\WindowsPowerShell\Modules;%SystemRoot%\System32\WindowsPowerShell\v1.0\Modules"
"%PS51%" -NoProfile -ExecutionPolicy Bypass -File "%HELPER%" %*
set "EXIT_CODE=%ERRORLEVEL%"
goto finish

:no_powershell
echo [Run-Stage5CC] Windows PowerShell 5.1 was not found:
echo   %PS51%
goto finish

:no_helper
echo [Run-Stage5CC] The launcher helper is missing:
echo   %HELPER%
goto finish

:finish
echo.
echo [Run-Stage5CC] Exit code: %EXIT_CODE%
echo [Run-Stage5CC] Finished. Press Enter to close this window.
pause >nul
exit /b %EXIT_CODE%
