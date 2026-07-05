@echo off
setlocal

REM ============================================================
REM GraphCalc / Infinity Cube Launcher
REM Place this file in:
REM   E:\Graphing Calculator R12
REM Then double-click it.
REM ============================================================

set "ROOT=%~dp0"

echo.
echo Starting GraphCalc from:
echo %ROOT%
echo.

if not exist "%ROOT%scripts\04_run_api.cmd" (
    echo ERROR: Could not find scripts\04_run_api.cmd
    echo Make sure this launcher is in the project root folder.
    echo Expected location: E:\Graphing Calculator R12
    pause
    exit /b 1
)

if not exist "%ROOT%scripts\05_run_ui.cmd" (
    echo ERROR: Could not find scripts\05_run_ui.cmd
    echo Make sure this launcher is in the project root folder.
    echo Expected location: E:\Graphing Calculator R12
    pause
    exit /b 1
)

REM Optional: uncomment the next 3 lines if you want every launch to rebuild first.
REM echo Building GraphCalc...
REM call "%ROOT%scripts\03_build.cmd"
REM if errorlevel 1 pause & exit /b 1

echo Launching API window...
start "GraphCalc API" cmd /k "cd /d "%ROOT%" && scripts\04_run_api.cmd"

echo Waiting for API to start...
timeout /t 2 /nobreak >nul

echo Launching UI window...
start "GraphCalc UI" cmd /k "cd /d "%ROOT%" && scripts\05_run_ui.cmd"

echo.
echo GraphCalc launch commands sent.
echo You can close this launcher window.
echo Keep the API window open while using the UI.
echo.

endlocal
