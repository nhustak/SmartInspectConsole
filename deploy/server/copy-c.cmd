@echo off
setlocal EnableExtensions EnableDelayedExpansion
rem SmartInspectConsole install from SureCourt package folder (c)
rem Run from the FTP package directory that contains si-c.zip

set "ERR=0"
set "ENV_NAME=c"
set "SOURCE_ARCHIVE=%~dp0si-c.zip"
set "TARGET_ROOT=C:\Tools\SmartInspectConsole"
set "STAGE_DIR=%TARGET_ROOT%\current"
set "LOG_FILE=%~dp0deploy-si-c.log"

echo ========================================
echo SmartInspectConsole install (%ENV_NAME% / SureCourt)
echo Archive : %SOURCE_ARCHIVE%
echo Target  : %TARGET_ROOT%
echo ========================================
call :Log "START %DATE% %TIME%"

if not exist "%SOURCE_ARCHIVE%" (
  echo [FAIL] Missing archive: %SOURCE_ARCHIVE%
  call :Log "[FAIL] Missing archive: %SOURCE_ARCHIVE%"
  set "ERR=1"
  goto :end
)

call :EnsureDir "C:\Tools" || goto :end
call :EnsureDir "%TARGET_ROOT%" || goto :end
call :EnsureDir "%STAGE_DIR%" || goto :end

echo Stopping SmartInspectConsole if running...
taskkill /IM SmartInspectConsole.exe /F >nul 2>&1

call :SafeClear "%STAGE_DIR%" "stage" || goto :end

echo Extracting package...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Expand-Archive -LiteralPath '%SOURCE_ARCHIVE%' -DestinationPath '%STAGE_DIR%' -Force"
if errorlevel 1 (
  echo [FAIL] Expand-Archive failed
  call :Log "[FAIL] Expand-Archive failed"
  set "ERR=1"
  goto :end
)

if not exist "%STAGE_DIR%\SmartInspectConsole.exe" (
  echo [FAIL] SmartInspectConsole.exe not found after extract
  call :Log "[FAIL] SmartInspectConsole.exe missing after extract"
  set "ERR=1"
  goto :end
)

echo [OK] Installed to %STAGE_DIR%
call :Log "[OK] Installed to %STAGE_DIR%"
goto :end

:EnsureDir
set "D=%~1"
if not exist "%D%" (
  md "%D%" 2>nul
)
if not exist "%D%" (
  echo [FAIL] Cannot create directory: %D%
  call :Log "[FAIL] Cannot create directory: %D%"
  set "ERR=1"
  exit /b 1
)
exit /b 0

:SafeClear
set "TARGET=%~1"
set "LABEL=%~2"
for %%I in ("%TARGET%") do set "TARGET_FULL=%%~fI"
if /I "%TARGET_FULL%"=="C:\" (
  echo [FAIL] Refusing to clear C:\
  call :Log "[FAIL] Refusing to clear C:\"
  set "ERR=1"
  exit /b 1
)
if /I not "%TARGET_FULL:~0,9%"=="C:\Tools\" (
  if /I not "%TARGET_FULL%"=="C:\Tools" (
    echo [FAIL] Target must be under C:\Tools\
    call :Log "[FAIL] Target must be under C:\Tools\: %TARGET_FULL%"
    set "ERR=1"
    exit /b 1
  )
)
cd /d "%TARGET%" || (
  echo [FAIL] Cannot enter %LABEL%: %TARGET%
  call :Log "[FAIL] Cannot enter %LABEL%: %TARGET%"
  set "ERR=1"
  exit /b 1
)
if /I not "%CD%"=="%TARGET_FULL%" (
  echo [FAIL] Safe clear mismatch. Expected %TARGET_FULL%, got %CD%
  call :Log "[FAIL] Safe clear mismatch. Expected %TARGET_FULL%, got %CD%"
  set "ERR=1"
  exit /b 1
)
del /f /q *.* >nul 2>&1
for /d %%D in (*) do rd /s /q "%%D" >nul 2>&1
exit /b 0

:Log
>>"%LOG_FILE%" echo %~1
exit /b 0

:end
if not "%ERR%"=="0" (
  echo.
  echo Deploy FAILED.
  call :Log "FAILED %DATE% %TIME%"
  exit /b 1
)
echo.
echo Deploy SUCCESS.
call :Log "SUCCESS %DATE% %TIME%"
exit /b 0
