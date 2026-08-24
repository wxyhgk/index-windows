@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-Index.ps1" %*
set "INDEX_INSTALL_EXIT=%ERRORLEVEL%"
if not "%INDEX_INSTALL_EXIT%"=="0" (
  echo.
  echo Index installation failed with exit code %INDEX_INSTALL_EXIT%.
  pause
)
exit /b %INDEX_INSTALL_EXIT%
