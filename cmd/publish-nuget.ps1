#!/usr/bin/env pwsh
# publish-nuget.ps1 — Build release packages and push to NuGet.org
#
# Versioning is timestamp-based (v2) — every build gets a unique version
# automatically via YeetCode.shared.Build.props. The timestamp is captured
# once here and passed to MSBuild so all packages in this run get the exact
# same version (no inter-project skew). No version files to manage.
#
# Publishes two product families:
#   1. ArtificialNecessity.YeetJson          — standalone HJSON parser library
#   2. ArtificialNecessity.YeetCode          — YeetCode library (depends on YeetJson)
#      ArtificialNecessity.YeetCode.CLI      — CLI dotnet tool
#      ArtificialNecessity.YeetCode.MSBuild  — MSBuild task (self-contained)
#
# Usage:
#   .\cmd\publish-nuget.ps1          # pack + push all packages
#   .\cmd\publish-nuget.ps1 -DryRun  # pack only, show what would be pushed

param(
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Resolve paths
$repoRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repoRoot 'AN_YeetCode.sln'
$packageOutputDir = Join-Path $repoRoot 'bin\Packages\Release'
$localNuGetFeedPath = $env:LOCAL_NUGET_REPO

# Package IDs in dependency order
$packageIds = @(
    'ArtificialNecessity.YeetJson',
    'ArtificialNecessity.YeetCode',
    'ArtificialNecessity.YeetCode.CLI',
    'ArtificialNecessity.YeetCode.MSBuild'
)

# Project paths in dependency order (pack order matters)
$projectPaths = @(
    (Join-Path $repoRoot 'YeetJson.lib\YeetJson\YeetJson.csproj'),
    (Join-Path $repoRoot 'YeetCode.lib\YeetCode\YeetCode.csproj'),
    (Join-Path $repoRoot 'YeetCode.CLI\YeetCode.CLI.csproj'),
    (Join-Path $repoRoot 'YeetCode.MSBuild\YeetCode.MSBuild.csproj')
)

Write-Host "`n=== YeetCode publish-nuget (Release) ===" -ForegroundColor Cyan

# Capture timestamp ONCE so all packages in this run get the same version
$now = [System.DateTime]::Now
$buildYYMM   = $now.ToString('yyMM')
$buildDDHH   = $now.ToString('ddHH')
$buildmmss   = $now.ToString('mmss')
$buildYYMMDD = $now.ToString('yyMMdd')
$buildHHmmss = $now.ToString('HHmmss')
$versionProps = "/p:_BuildYYMM=$buildYYMM", "/p:_BuildDDHH=$buildDDHH", "/p:_Buildmmss=$buildmmss", "/p:_BuildYYMMDD=$buildYYMMDD", "/p:_BuildHHmmss=$buildHHmmss"

$major = 0
Write-Host "Version stamp: $major.$buildYYMM.$buildDDHH.$buildmmss (pkg: $major.$buildYYMMDD.$buildHHmmss)" -ForegroundColor Gray

# ── Step 1: Build solution ─────────────────────────────────────────
Write-Host "`n[1/3] Building solution (Release)..." -ForegroundColor Green
dotnet build $solutionPath -c Release @versionProps
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: dotnet build failed" -ForegroundColor Red
    exit $LASTEXITCODE
}

# ── Step 2: Pack all projects in dependency order ──────────────────
Write-Host "`n[2/3] Packing packages..." -ForegroundColor Green
$packTimestamp = Get-Date

for ($projectIndex = 0; $projectIndex -lt $projectPaths.Count; $projectIndex++) {
    $projectPath = $projectPaths[$projectIndex]
    $packageId = $packageIds[$projectIndex]
    Write-Host "  Packing $packageId..." -ForegroundColor Gray
    dotnet pack $projectPath -c Release --no-build @versionProps
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: dotnet pack failed for $packageId" -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

# Find all newly created .nupkg files
$newPackages = Get-ChildItem (Join-Path $packageOutputDir '*.nupkg') -ErrorAction SilentlyContinue |
    Where-Object { $_.LastWriteTime -ge $packTimestamp } |
    Sort-Object Name

if (-not $newPackages -or $newPackages.Count -eq 0) {
    Write-Host "ERROR: No .nupkg files found in $packageOutputDir" -ForegroundColor Red
    exit 1
}

Write-Host "`nPackages ready:" -ForegroundColor Cyan
foreach ($packageFile in $newPackages) {
    $sizeKB = [math]::Round($packageFile.Length / 1024, 1)
    Write-Host "  $($packageFile.Name)  (${sizeKB} KB)" -ForegroundColor Green
}

# ── Step 3: Push to NuGet.org ──────────────────────────────────────
if ($DryRun) {
    Write-Host "`n[DRY RUN] Would push $($newPackages.Count) packages to https://api.nuget.org/v3/index.json" -ForegroundColor Yellow
    foreach ($packageFile in $newPackages) {
        Write-Host "  [DRY RUN] $($packageFile.Name)" -ForegroundColor Yellow
    }
} else {
    Write-Host "`n[3/3] Pushing to NuGet.org..." -ForegroundColor Green
    foreach ($packageFile in $newPackages) {
        Write-Host "  Pushing $($packageFile.Name)..." -ForegroundColor Gray
        dotnet nuget push $packageFile.FullName --source https://api.nuget.org/v3/index.json --skip-duplicate
        if ($LASTEXITCODE -ne 0) {
            Write-Host "ERROR: Failed to push $($packageFile.Name)" -ForegroundColor Red
            exit $LASTEXITCODE
        }
    }
}

# Also deploy to local NuGet feed if configured
if ($localNuGetFeedPath -and (Test-Path $localNuGetFeedPath)) {
    foreach ($packageFile in $newPackages) {
        Copy-Item $packageFile.FullName -Destination $localNuGetFeedPath -Force
    }
    Write-Host "Also deployed to local feed: $localNuGetFeedPath" -ForegroundColor DarkGray
}

# ── Summary ────────────────────────────────────────────────────────
Write-Host "`n=== Done! ===" -ForegroundColor Green
Write-Host "Published $($newPackages.Count) packages" -ForegroundColor Green
Write-Host "  https://www.nuget.org/packages/ArtificialNecessity.YeetJson/" -ForegroundColor Gray
Write-Host "  https://www.nuget.org/packages/ArtificialNecessity.YeetCode/" -ForegroundColor Gray
Write-Host "  https://www.nuget.org/packages/ArtificialNecessity.YeetCode.CLI/" -ForegroundColor Gray
Write-Host "  https://www.nuget.org/packages/ArtificialNecessity.YeetCode.MSBuild/" -ForegroundColor Gray