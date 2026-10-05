@echo off
setlocal
rem ============================================================
rem  wordgame local QA: build -> test -> launch the game.
rem  Double-click to run (random seed) - opens the Godot game window.
rem  Usage: run_local_qa.bat [--cli] [--ci] [seed]
rem    --cli  launch the text console instead of the game window
rem    --ci   skip pauses (non-interactive / scripted runs)
rem    seed   start on a fixed seed for reproducible playtests
rem  Godot location: set GODOT in qa.local.bat (see qa.local.bat.example)
rem  or as an environment variable.
rem ============================================================
cd /d "%~dp0"

set "NOPAUSE="
set "SEED="
set "USE_CLI="
:parse_args
if "%~1"=="" goto :args_done
if /i "%~1"=="--ci" (
    set "NOPAUSE=1"
) else if /i "%~1"=="--cli" (
    set "USE_CLI=1"
) else (
    set "SEED=%~1"
)
shift
goto :parse_args
:args_done

if exist "%~dp0qa.local.bat" call "%~dp0qa.local.bat"

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

if defined USE_CLI goto :cli
if not defined GODOT goto :no_godot
if not exist "%GODOT%" goto :no_godot

echo.
echo [QA] Step 3/3: Launching the game window...
set "SEEDARG="
if defined SEED set "SEEDARG=--seed=%SEED%"
start "" "%GODOT%" --path "%~dp0game" -- %SEEDARG%
exit /b 0

:no_godot
echo.
echo [QA] Godot not found (GODOT="%GODOT%").
echo [QA] Copy qa.local.bat.example to qa.local.bat and set your Godot 4.7 .NET path.
echo [QA] Falling back to the text console.

:cli
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
