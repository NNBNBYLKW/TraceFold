@echo off
setlocal
chcp 65001 >nul
set "ROOT=%~dp0"
where py >nul 2>nul
if %errorlevel%==0 (
  py -3.12 "%ROOT%app\install_test_env.py"
) else (
  python "%ROOT%app\install_test_env.py"
)
set "RESULT=%errorlevel%"
if not "%RESULT%"=="0" pause
exit /b %RESULT%
