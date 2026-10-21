@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
if errorlevel 1 (
  echo.
  echo 编译失败。
  pause
  exit /b 1
)
echo.
echo 生成文件: %~dp0bin\BattWhale.exe
pause
