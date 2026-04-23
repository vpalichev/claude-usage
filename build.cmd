@echo off
REM Build claude-usage.exe (framework-dependent) into dist\ alongside the
REM extension files. Requires the .NET 9 SDK on PATH (dotnet --version).
REM On success, dist\ contains the runnable app; run dist\claude-usage.exe.

setlocal
pushd "%~dp0harness" || exit /b 1

dotnet publish -c Release -r win-x64 --self-contained false -o "%~dp0dist"
set RC=%ERRORLEVEL%

popd
if %RC% neq 0 (
    echo.
    echo Build failed with code %RC%.
    exit /b %RC%
)

echo.
echo Build succeeded. Run dist\claude-usage.exe
endlocal
