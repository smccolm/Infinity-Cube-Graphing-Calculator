@echo off
setlocal
cd /d "%~dp0.."

echo Restoring API packages...
dotnet restore .\src\GraphCalc.Api\GraphCalc.Api.csproj
if errorlevel 1 exit /b 1

echo Restoring UI packages...
dotnet restore .\src\GraphCalc.UI\GraphCalc.UI.csproj
if errorlevel 1 exit /b 1

echo Restore completed.
endlocal
