@echo off
setlocal
set LOGDIR=%LOCALAPPDATA%\GraphCalc\logs
if exist "%LOGDIR%" (
  del /q "%LOGDIR%\*.log"
  echo Deleted logs from %LOGDIR%
) else (
  echo No log folder found at %LOGDIR%
)
endlocal
