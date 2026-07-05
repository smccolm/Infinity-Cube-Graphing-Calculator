@echo off
setlocal
cd /d "%~dp0.."

echo Building API...
dotnet build .\src\GraphCalc.Api\GraphCalc.Api.csproj -c Debug
if errorlevel 1 exit /b 1

echo Building UI...
dotnet build .\src\GraphCalc.UI\GraphCalc.UI.csproj -c Debug
if errorlevel 1 exit /b 1

echo Build completed.
echo API exe: .\src\GraphCalc.Api\bin\Debug\net8.0\GraphCalc.Api.exe
echo UI exe:  .\src\GraphCalc.UI\bin\Debug\net8.0-windows\GraphCalc.UI.exe
endlocal
