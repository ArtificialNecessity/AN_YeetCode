@echo off
REM publish-nuget.cmd — Thin wrapper that launches the cross-platform C# publish script.
REM Usage: cmd\publish-nuget.cmd [--dry-run]
dotnet run --file "%~dp0publish-nuget.cs" -- %*