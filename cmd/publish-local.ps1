#!/usr/bin/env pwsh
# publish-local.ps1 — Build and pack YeetCode packages to local NuGet feed
#
# Versioning is handled entirely by MSBuild targets in YeetCode.shared.Build.props:
#   - Stable (default): auto-increments buildNumberOffset in version.jsonc → 0.1.1, 0.1.2, ...
#   - Prerelease (-Prerelease): uses git height suffix → 0.1.0-27.0.g3be210cd
#
# Usage:
#   ./cmd/publish-local.ps1                    # stable build + pack + deploy
#   ./cmd/publish-local.ps1 -Release           # Release configuration
#   ./cmd/publish-local.ps1 -Prerelease        # prerelease versions (no auto-increment)
#
# Requires: LOCAL_NUGET_REPO environment variable set to local feed path

param(
    [switch]$Release,
    [switch]$Prerelease
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$configuration = if ($Release) { "Release" } else { "Debug" }
$prereleaseFlag = if ($Prerelease) { "-p:Prerelease=true" } else { "" }
$versionLabel = if ($Prerelease) { "prerelease" } else { "stable" }

Write-Host "=== YeetCode publish-local ($configuration, $versionLabel) ===" -ForegroundColor Cyan

if (-not $env:LOCAL_NUGET_REPO) {
    Write-Host "ERROR: LOCAL_NUGET_REPO environment variable not set." -ForegroundColor Red
    Write-Host 'Set it to your local NuGet feed path, e.g.: $env:LOCAL_NUGET_REPO = "C:\PROJECTS\LocalNuGet"' -ForegroundColor Yellow
    exit 1
}

Write-Host "Local NuGet feed: $env:LOCAL_NUGET_REPO" -ForegroundColor Gray

# Increment buildNumberOffset for stable versions (before any build/pack)
# Versioning is computed by MSBuild targets in YeetCode.shared.Build.props,
# but the increment must happen exactly once before all projects build.
if (-not $Prerelease) {
    $jsonPeekExePath = Join-Path $env:USERPROFILE ".nuget\packages\artificialnecessity.codeanalyzers\0.1.13\tools\net8.0\any\JsonPeek.exe"
    $versionJsoncPath = Join-Path $repoRoot "version.jsonc"
    $newBuildOffset = & $jsonPeekExePath --inc-integer $versionJsoncPath buildNumberOffset
    if ($LASTEXITCODE -ne 0) { Write-Host "ERROR: Failed to increment buildNumberOffset" -ForegroundColor Red; exit 1 }
    $baseVersion = & $jsonPeekExePath $versionJsoncPath version
    Write-Host "Version: $baseVersion.$newBuildOffset (buildNumberOffset incremented)" -ForegroundColor Yellow
}

# Build the solution
Write-Host "`n[1/5] Building solution..." -ForegroundColor Green
dotnet build "$repoRoot\AN_YeetCode.sln" -c $configuration $prereleaseFlag
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Pack libraries first (dependency order: YeetJson → YeetCode → CLI/MSBuild)
Write-Host "`n[2/5] Packing YeetJson library..." -ForegroundColor Green
dotnet pack "$repoRoot\YeetJson.lib\YeetJson\YeetJson.csproj" -c $configuration $prereleaseFlag
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "`n[3/5] Packing YeetCode library..." -ForegroundColor Green
dotnet pack "$repoRoot\YeetCode.lib\YeetCode\YeetCode.csproj" -c $configuration $prereleaseFlag
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Pack the CLI tool
Write-Host "`n[4/5] Packing CLI tool..." -ForegroundColor Green
dotnet pack "$repoRoot\YeetCode.CLI\YeetCode.CLI.csproj" -c $configuration $prereleaseFlag
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Pack the MSBuild task
Write-Host "`n[5/5] Packing MSBuild task..." -ForegroundColor Green
dotnet pack "$repoRoot\YeetCode.MSBuild\YeetCode.MSBuild.csproj" -c $configuration $prereleaseFlag
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Show deployed packages
Write-Host "`nDeployed packages:" -ForegroundColor Cyan
Get-ChildItem "$env:LOCAL_NUGET_REPO\ArtificialNecessity.Yeet*" -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 8 |
    ForEach-Object { Write-Host "  $($_.FullName)" -ForegroundColor Green }