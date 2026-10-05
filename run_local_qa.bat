@echo off
setlocal
rem ============================================================
rem  wordgame local QA: build -> test -> launch CLI.
rem  Double-click to run (random seed).
rem  Usage: run_local_qa.bat [--ci] [seed]
rem    --ci   skip pauses (non-interactive / scripted runs)
rem    seed   start the CLI on a fixed seed for reproducible playtests
rem ============================================================
cd /d "%~dp0"

set "NOPAUSE="
set "SEED="
:parse_args
if "%~1"=="" goto :args_done
if /i "%~1"=="--ci" (set "NOPAUSE=1") else (set "SEED=%~1")
shift
goto :parse_args
:args_done

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
dotnet run --project src\Crossword.Cli -c Debug --no-build -- %SEED%
echo.
echo [QA] CLI exited.
if not defined NOPAUSE pause
exit /b 0

:fail
if not defined NOPAUSE pause
exit /b 1
