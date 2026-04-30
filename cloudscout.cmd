@echo off
rem Wrapper that publishes CloudScout.Cli once into dist\ on first run, then executes
rem the published binary directly on subsequent runs. This avoids the ~1s MSBuild
rem up-to-date check that `dotnet run` performs on every invocation.
rem
rem To pick up source changes, delete the dist\ folder (or run: dotnet publish
rem src\CloudScout.Cli -c Release -o dist --no-self-contained).
setlocal
set "SCRIPT_DIR=%~dp0"
set "EXE=%SCRIPT_DIR%dist\CloudScout.Cli.exe"
if not exist "%EXE%" (
    echo Publishing CloudScout.Cli ^(first-run setup^)...
    dotnet publish "%SCRIPT_DIR%src\CloudScout.Cli" -c Release -o "%SCRIPT_DIR%dist" --no-self-contained --verbosity quiet --nologo
    if errorlevel 1 exit /b %ERRORLEVEL%
)
"%EXE%" %*
exit /b %ERRORLEVEL%
