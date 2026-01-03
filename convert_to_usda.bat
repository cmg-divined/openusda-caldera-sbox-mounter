@echo off
setlocal enabledelayedexpansion

REM ============================================
REM   USD to USDA Batch Converter
REM   Converts binary .usd files to ASCII .usda
REM   Files are converted in-place (same folder)
REM ============================================

REM Path to your USD installation (where you extracted the Nvidia tools)
set USD_PATH=C:\Users\James\Desktop\usd.py312.windows-x86_64.usdview.release-v25.08.71e038c1

REM Path to your Caldera dataset
set TARGET_DIR=C:\USD

REM Add USD to PATH
set PATH=%USD_PATH%\bin;%USD_PATH%\lib;%PATH%
set PYTHONPATH=%USD_PATH%\lib\python;%PYTHONPATH%

echo.
echo ============================================
echo   USD to USDA Batch Converter
echo ============================================
echo.
echo USD Tools: %USD_PATH%
echo Target: %TARGET_DIR%
echo.

REM Verify usdcat exists
if not exist "%USD_PATH%\bin\usdcat.exe" (
    echo ERROR: usdcat.exe not found at %USD_PATH%\bin\usdcat.exe
    echo Please update USD_PATH in this script to point to your USD installation.
    echo.
    pause
    exit /b 1
)

REM Verify target directory exists
if not exist "%TARGET_DIR%" (
    echo ERROR: Target directory not found: %TARGET_DIR%
    echo Please update TARGET_DIR in this script.
    echo.
    pause
    exit /b 1
)

REM Count files first
echo Counting .usd files...
set /a count=0
for /r "%TARGET_DIR%" %%f in (*.usd) do (
    set /a count+=1
)
echo Found %count% .usd files to process
echo.

set /a processed=0
set /a skipped=0
set /a failed=0

for /r "%TARGET_DIR%" %%f in (*.usd) do (
    set "input=%%f"
    set "output=%%~dpnf.usda"
    
    if exist "!output!" (
        set /a skipped+=1
    ) else (
        echo Converting: %%~nxf
        "%USD_PATH%\bin\usdcat.exe" "!input!" -o "!output!" 2>nul
        if errorlevel 1 (
            echo   FAILED: %%~nxf
            set /a failed+=1
        ) else (
            set /a processed+=1
        )
    )
    
    set /a total=!processed!+!skipped!+!failed!
    set /a mod=!total! %% 100
    if !mod! equ 0 (
        if !total! gtr 0 (
            echo   Progress: !total! / %count% files...
        )
    )
)

echo.
echo ============================================
echo   Conversion Complete
echo ============================================
echo   Converted: %processed%
echo   Skipped (already exist): %skipped%
echo   Failed: %failed%
echo   Total: %count%
echo ============================================
echo.

pause
