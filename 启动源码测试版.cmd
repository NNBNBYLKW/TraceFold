@echo off
setlocal
chcp 65001 >nul
set "ROOT=%~dp0"

where python >nul 2>&1
if errorlevel 1 (
  echo 未找到 Python，请先安装项目的 Python 开发环境。
  pause
  exit /b 1
)

python "%ROOT%scripts\launch_source_test_app.py"
set "RESULT=%errorlevel%"
if not "%RESULT%"=="0" pause
exit /b %RESULT%
