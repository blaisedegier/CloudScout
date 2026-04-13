@echo off
rem Thin wrapper around `dotnet run` so you can type `cloudscout <command>` instead of
rem `dotnet run --project src/CloudScout.Cli -- <command>` every time. Pass-through for all args.
rem
rem --no-restore skips the NuGet check on every invocation (faster); run `dotnet restore`
rem manually or use `dotnet build` if you've just changed package references.
rem --verbosity quiet suppresses the build summary lines so the CLI's own output comes through cleanly.
setlocal
set "SCRIPT_DIR=%~dp0"
dotnet run --project "%SCRIPT_DIR%src\CloudScout.Cli" --no-restore --verbosity quiet -- %*
exit /b %ERRORLEVEL%
