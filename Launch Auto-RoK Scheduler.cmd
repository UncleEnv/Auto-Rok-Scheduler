@echo off
rem ============================================================================
rem  Auto-RoK Scheduler launcher
rem  Double-click this to open the app without hunting through build folders.
rem  Works from the repo root (finds the built/published exe) and, if copied
rem  next to a published exe, launches that. Falls back to `dotnet run`.
rem ============================================================================
setlocal
set "HERE=%~dp0"
set "APP=AutoRokScheduler"

rem 1) Published exe sitting next to this launcher (e.g. inside a release zip)
if exist "%HERE%%APP%.exe" (
    start "" "%HERE%%APP%.exe"
    goto :eof
)

rem 2) Self-contained publish output
if exist "%HERE%%APP%\bin\Release\net10.0-windows\win-x64\publish\%APP%.exe" (
    start "" "%HERE%%APP%\bin\Release\net10.0-windows\win-x64\publish\%APP%.exe"
    goto :eof
)

rem 3) Plain Release build
if exist "%HERE%%APP%\bin\Release\net10.0-windows\%APP%.exe" (
    start "" "%HERE%%APP%\bin\Release\net10.0-windows\%APP%.exe"
    goto :eof
)

rem 4) Debug build
if exist "%HERE%%APP%\bin\Debug\net10.0-windows\%APP%.exe" (
    start "" "%HERE%%APP%\bin\Debug\net10.0-windows\%APP%.exe"
    goto :eof
)

rem 5) Nothing built yet — run from source (needs the .NET SDK)
echo No built app found. Building and running from source (requires .NET 10 SDK)...
where dotnet >nul 2>nul
if errorlevel 1 (
    echo.
    echo ERROR: 'dotnet' is not on your PATH and no built app was found.
    echo Install the .NET 10 SDK, or download a release from the Releases page.
    echo.
    pause
    goto :eof
)
dotnet run --project "%HERE%%APP%\%APP%.csproj" -c Release
endlocal
