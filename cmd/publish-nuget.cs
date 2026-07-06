#!/usr/bin/env -S dotnet run
// publish-nuget.cs — Cross-platform: publish release packages to NuGet.org
//
// This script does NOT duplicate build/pack logic — it delegates to
// cmd/publish-local.cs (--release) for build + pack + local-feed deploy,
// then pushes the newly created .nupkg files to NuGet.org.
//
// Publishes two product families:
//   1. ArtificialNecessity.YeetJson          — standalone HJSON parser library
//   2. ArtificialNecessity.YeetCode          — YeetCode library (depends on YeetJson)
//      ArtificialNecessity.YeetCode.CLI      — CLI dotnet tool
//      ArtificialNecessity.YeetCode.MSBuild  — MSBuild task (self-contained)
//
// Usage:
//   dotnet run cmd/publish-nuget.cs              # build + pack + push all packages
//   dotnet run cmd/publish-nuget.cs --dry-run    # build + pack, show what would be pushed
//
// Windows wrapper: cmd\publish-nuget.cmd

using System.Diagnostics;

// ── Ctrl+C handling — kill child processes to prevent zombies ────────────
Process? _activeChild = null;
var _cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    _cts.Cancel();
    var proc = _activeChild;
    if (proc is { HasExited: false })
    {
        WriteColor($"\n[Ctrl+C] Killing child process tree (PID {proc.Id})...", ConsoleColor.Yellow);
        try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
    }
    Environment.Exit(130);
};

// ── Parse arguments ─────────────────────────────────────────────────────
bool dryRun = args.Any(a => a is "--dry-run" or "-n");

// ── Resolve project root (the script lives in cmd/) ─────────────────────
string projectRoot = Directory.GetCurrentDirectory();
if (!File.Exists(Path.Combine(projectRoot, "AN_YeetCode.sln")))
    projectRoot = Path.GetFullPath(Path.Combine(projectRoot, ".."));

if (!File.Exists(Path.Combine(projectRoot, "AN_YeetCode.sln")))
{
    WriteColor("ERROR: Cannot locate AN_YeetCode.sln. Run from the repo root:", ConsoleColor.Red);
    WriteColor("  dotnet run cmd/publish-nuget.cs", ConsoleColor.Yellow);
    Environment.Exit(1);
}

string publishLocalScriptPath = Path.Combine(projectRoot, "cmd", "publish-local.cs");
string packageOutputDir = Path.Combine(projectRoot, "bin", "Packages", "Release");

Console.WriteLine();
WriteColor("=== YeetCode publish-nuget (Release) ===", ConsoleColor.Cyan);

// ── Step 1: Delegate build + pack + local deploy to publish-local.cs ─────
// publish-local.cs captures the version stamp once, so all packages in this
// run get the exact same version. It also deploys to LOCAL_NUGET_REPO.
Console.WriteLine();
WriteColor("[1/2] Running publish-local (--release) for build + pack...", ConsoleColor.Green);

// Capture timestamp before pack so we can identify newly created packages
var packStartTime = DateTime.Now;

int publishLocalResult = RunProcess("dotnet", $"run --file \"{publishLocalScriptPath}\" -- --release");
if (publishLocalResult != 0)
{
    WriteColor($"ERROR: publish-local failed with exit code {publishLocalResult}", ConsoleColor.Red);
    Environment.Exit(publishLocalResult);
}

// ── Find the newly created .nupkg files ────────────────────────────────
var newPackages = Directory.Exists(packageOutputDir)
    ? Directory.GetFiles(packageOutputDir, "*.nupkg")
        .Select(f => new FileInfo(f))
        .Where(fi => fi.LastWriteTime >= packStartTime)
        .OrderBy(fi => fi.Name)
        .ToArray()
    : [];

if (newPackages.Length == 0)
{
    WriteColor($"ERROR: No new .nupkg files found in {packageOutputDir}", ConsoleColor.Red);
    Environment.Exit(1);
}

Console.WriteLine();
WriteColor("Packages ready:", ConsoleColor.Cyan);
foreach (var packageFile in newPackages)
{
    double sizeKB = Math.Round(packageFile.Length / 1024.0, 1);
    WriteColor($"  {packageFile.Name}  ({sizeKB} KB)", ConsoleColor.Green);
}

// ── Step 2: Push to NuGet.org ──────────────────────────────────────
const string nugetSourceUrl = "https://api.nuget.org/v3/index.json";

if (dryRun)
{
    Console.WriteLine();
    WriteColor($"[DRY RUN] Would push {newPackages.Length} packages to {nugetSourceUrl}", ConsoleColor.Yellow);
    foreach (var packageFile in newPackages)
        WriteColor($"  [DRY RUN] {packageFile.Name}", ConsoleColor.Yellow);
    Environment.Exit(0);
}

Console.WriteLine();
WriteColor("[2/2] Pushing to NuGet.org...", ConsoleColor.Green);
foreach (var packageFile in newPackages)
{
    if (_cts.IsCancellationRequested) break;

    WriteColor($"  Pushing {packageFile.Name}...", ConsoleColor.DarkGray);
    int pushResult = RunProcess("dotnet",
        $"nuget push \"{packageFile.FullName}\" --source {nugetSourceUrl} --skip-duplicate");
    if (pushResult != 0)
    {
        WriteColor($"ERROR: Failed to push {packageFile.Name}", ConsoleColor.Red);
        Environment.Exit(pushResult);
    }
}

// ── Summary ────────────────────────────────────────────────────────
Console.WriteLine();
WriteColor("=== Done! ===", ConsoleColor.Green);
WriteColor($"Published {newPackages.Length} packages", ConsoleColor.Green);
WriteColor("  https://www.nuget.org/packages/ArtificialNecessity.YeetJson/", ConsoleColor.DarkGray);
WriteColor("  https://www.nuget.org/packages/ArtificialNecessity.YeetCode/", ConsoleColor.DarkGray);
WriteColor("  https://www.nuget.org/packages/ArtificialNecessity.YeetCode.CLI/", ConsoleColor.DarkGray);
WriteColor("  https://www.nuget.org/packages/ArtificialNecessity.YeetCode.MSBuild/", ConsoleColor.DarkGray);

// ── Helper functions ────────────────────────────────────────────────────

static void WriteColor(string message, ConsoleColor color)
{
    var prev = Console.ForegroundColor;
    Console.ForegroundColor = color;
    Console.WriteLine(message);
    Console.ForegroundColor = prev;
}

int RunProcess(string fileName, string arguments)
{
    var psi = new ProcessStartInfo
    {
        FileName = fileName,
        Arguments = arguments,
        UseShellExecute = false,
        WorkingDirectory = projectRoot,
    };

    using var process = Process.Start(psi);
    _activeChild = process;
    try
    {
        process!.WaitForExit();
    }
    finally
    {
        _activeChild = null;
    }
    return _cts.IsCancellationRequested ? -1 : process!.ExitCode;
}