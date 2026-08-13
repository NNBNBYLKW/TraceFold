@echo off
setlocal
chcp 65001 >nul
set "ROOT=%~dp0"
if not exist "%ROOT%.venv\Scripts\python.exe" (
  echo 尚未安装测试环境，请先运行“安装测试环境.cmd”。
  pause
  exit /b 1
)
"%ROOT%.venv\Scripts\python.exe" "%ROOT%app\launch_test_app.py"
set "RESULT=%errorlevel%"
if not "%RESULT%"=="0" pause
exit /b %RESULT%
