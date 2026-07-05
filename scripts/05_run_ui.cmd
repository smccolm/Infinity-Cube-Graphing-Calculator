@echo off
setlocal
cd /d "%~dp0.."
set GRAPHCALC_TEST_MODE=true

echo Starting GraphCalc.UI...
dotnet run --project .\src\GraphCalc.UI\GraphCalc.UI.csproj
endlocal
