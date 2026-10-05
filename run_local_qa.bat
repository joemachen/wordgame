@echo off
setlocal
rem ============================================================
rem  wordgame local QA: build -> test -> launch CLI.
rem  Double-click to run. Pass --ci to skip pauses (non-interactive).
rem  Any extra args after --ci are not supported; to pick a seed use
rem  the 'new <seed>' command inside the CLI.
rem ============================================================
cd /d "%~dp0"

set "NOPAUSE="
if /i "%~1"=="--ci" set "NOPAUSE=1"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [QA] ERROR: 'dotnet' not found on PATH. Install the .NET 8 SDK.
    goto :fail
)

echo.
echo [QA] Step 1/3: Building solution...
dotnet build wordgame.sln -c Debug --nologo
if errorlevel 1 (
    echo.
    echo [QA] BUILD FAILED. See errors above.
    goto :fail
)

echo.
echo [QA] Step 2/3: Running unit tests...
dotnet test wordgame.sln -c Debug --no-build --nologo
if errorlevel 1 (
    echo.
    echo [QA] TESTS FAILED. Fix failing tests before playtesting.
    goto :fail
)

echo.
echo [QA] Step 3/3: Launching CLI...
echo.
dotnet run --project src\Crossword.Cli -c Debug --no-build
echo.
echo [QA] CLI exited.
if not defined NOPAUSE pause
exit /b 0

:fail
if not defined NOPAUSE pause
exit /b 1
