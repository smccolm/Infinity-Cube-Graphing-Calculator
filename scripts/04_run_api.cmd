@echo off
setlocal
cd /d "%~dp0.."
set GRAPHCALC_TEST_MODE=true

echo Starting GraphCalc.Api on http://127.0.0.1:8765
echo Leave this window open while using the app.
dotnet run --project .\src\GraphCalc.Api\GraphCalc.Api.csproj
endlocal
