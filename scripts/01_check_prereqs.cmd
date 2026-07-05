@echo off
setlocal

echo Checking .NET SDK...
where dotnet >nul 2>nul
if errorlevel 1 (
  echo dotnet was not found.
  echo Install the .NET 8 SDK, then open a new terminal.
  exit /b 1
)

dotnet --version
if errorlevel 1 exit /b 1

echo.
echo Installed SDKs:
dotnet --list-sdks

echo.
echo If you see an 8.x SDK, you are ready for this starter project.
endlocal
