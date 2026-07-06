#!/usr/bin/env -S dotnet run
// publish-local.cs — Cross-platform build+pack+deploy to local NuGet feed
//
// Versioning is timestamp-based (v2) — every build gets a unique version
// automatically via YeetCode.shared.Build.props. No version files to manage.
// The timestamp is captured once here and passed to MSBuild so all packages
// in this run get the exact same version (no inter-project skew).
//
// Requires: LOCAL_NUGET_REPO environment variable must be set
//
// Usage:
//   dotnet run cmd/publish-local.cs              # Debug build + pack + deploy
//   dotnet run cmd/publish-local.cs --release    # Release configuration
//   dotnet run cmd/publish-local.cs --dry-run    # show what would happen, don't build
//
// Windows wrapper: cmd\publish-local.cmd

using System.Diagnostics;

// ── Disable MSBuild node reuse to prevent zombie worker nodes ────────────
// MSBuild nodes with --nodeReuse:true stay alive between builds to "speed up"
// subsequent builds. On Linux, if the parent dies (Ctrl+C), these nodes become
// immortal zombies that hold locks and corrupt future restore/build operations.
Environment.SetEnvironmentVariable("MSBUILDDISABLENODEREUSE", "1");

// ── Ctrl+C handling — kill child processes to prevent zombies ────────────
// .NET's Process.Start on Linux uses posix_spawn, which does NOT place the
// child in the parent's process group. Ctrl+C (SIGINT) only reaches the
// foreground pgroup, leaving children alive as zombies. We track the active
// child and kill its entire process tree on cancellation.
Process? _activeChild = null;
var _cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true; // prevent immediate exit so we can clean up
    _cts.Cancel();
    var proc = _activeChild;
    if (proc is { HasExited: false })
    {
        WriteColor($"\n[Ctrl+C] Killing child process tree (PID {proc.Id})...", ConsoleColor.Yellow);
        try
        {
            proc.Kill(entireProcessTree: true);
        }
        catch { /* best effort */ }
    }
    // Also shut down any MSBuild nodes that may have been spawned with node reuse
    try
    {
        using var shutdown = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "build-server shutdown",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });
        shutdown?.WaitForExit(5000);
    }
    catch { /* best effort */ }

    Environment.Exit(130); // Standard Ctrl+C exit code
};

// ── Parse arguments ─────────────────────────────────────────────────────
bool dryRun = args.Any(a => a is "--dry-run" or "-n");
bool release = args.Any(a => a is "--release" or "-r");
string configuration = release ? "Release" : "Debug";

// ── Resolve project root (the script lives in cmd/) ─────────────────────
// When invoked via `dotnet run cmd/publish-local.cs` from the repo root,
// the working directory is the repo root. But handle running from cmd/ too.
string projectRoot = Directory.GetCurrentDirectory();
if (!File.Exists(Path.Combine(projectRoot, "AN_YeetCode.sln")))
    projectRoot = Path.GetFullPath(Path.Combine(projectRoot, ".."));

if (!File.Exists(Path.Combine(projectRoot, "AN_YeetCode.sln")))
{
    WriteColor("ERROR: Cannot locate AN_YeetCode.sln. Run from the repo root:", ConsoleColor.Red);
    WriteColor("  dotnet run cmd/publish-local.cs", ConsoleColor.Yellow);
    Environment.Exit(1);
}

string solutionPath = Path.Combine(projectRoot, "AN_YeetCode.sln");
string packageOutputDir = Path.Combine(projectRoot, "bin", "Packages", configuration);

// ── Require LOCAL_NUGET_REPO environment variable ────────────────────────
string? localNuGetFeedPath = Environment.GetEnvironmentVariable("LOCAL_NUGET_REPO");
if (string.IsNullOrEmpty(localNuGetFeedPath))
{
    WriteColor("ERROR: LOCAL_NUGET_REPO environment variable is not set!", ConsoleColor.Red);
    WriteColor("  Set it to your local NuGet feed directory, e.g.:", ConsoleColor.Yellow);
    WriteColor("    export LOCAL_NUGET_REPO=/path/to/LocalNuGet", ConsoleColor.Yellow);
    WriteColor("    $env:LOCAL_NUGET_REPO = \"C:\\PROJECTS\\LocalNuGet\"", ConsoleColor.Yellow);
    Environment.Exit(1);
}

// ── Capture timestamp ONCE so all packages get the same version ─────────
var now = DateTime.Now;
string buildYYMM   = now.ToString("yyMM");
string buildDDHH   = now.ToString("ddHH");
string buildmmss   = now.ToString("mmss");
string buildYYMMDD = now.ToString("yyMMdd");
string buildHHmmss = now.ToString("HHmmss");
string[] versionProps = [$"/p:_BuildYYMM={buildYYMM}", $"/p:_BuildDDHH={buildDDHH}", $"/p:_Buildmmss={buildmmss}", $"/p:_BuildYYMMDD={buildYYMMDD}", $"/p:_BuildHHmmss={buildHHmmss}"];
// NuGet normalizes version numbers by stripping leading zeros from numeric segments
string packageVersion = $"0.{int.Parse(buildYYMMDD)}.{int.Parse(buildHHmmss)}";

Console.WriteLine();
WriteColor($"=== YeetCode publish-local ({configuration}) ===", ConsoleColor.Cyan);
WriteColor($"Version stamp: 0.{buildYYMM}.{buildDDHH}.{buildmmss} (pkg: {packageVersion})", ConsoleColor.DarkGray);
WriteColor($"Local feed:    {localNuGetFeedPath}", ConsoleColor.DarkGray);

if (dryRun)
{
    Console.WriteLine();
    WriteColor($"[DRY RUN] Would build {configuration} version {packageVersion} and deploy to {localNuGetFeedPath}", ConsoleColor.Yellow);
    Environment.Exit(0);
}

// ── Build and pack ───────────────────────────────────────────────────────
Environment.SetEnvironmentVariable("LOCAL_NUGET_REPO", localNuGetFeedPath);
var failedSteps = new List<string>();
string versionArgs = string.Join(" ", versionProps);

// Capture timestamp before build/pack so we can identify newly deployed packages
var deployStartTime = DateTime.Now;

// Step 1: Build the solution once with the shared version stamp
Console.WriteLine();
WriteColor("[1/5] Building solution...", ConsoleColor.Green);
int buildResult = RunProcess("dotnet",
    $"build \"{solutionPath}\" -c {configuration} /nodeReuse:false {versionArgs}");
if (buildResult != 0)
{
    failedSteps.Add($"dotnet build failed with exit code {buildResult}");
    WriteColor($"ERROR: Build failed with exit code {buildResult}", ConsoleColor.Red);
}

// Steps 2-5: Pack in dependency order (YeetJson → YeetCode → CLI → MSBuild).
// --no-build: the solution was just built with the same version stamp.
// The MSBuild DeployToLocalNuGet target copies each .nupkg to LOCAL_NUGET_REPO.
if (failedSteps.Count == 0)
{
    (string label, string csprojRelPath)[] packSteps =
    [
        ("YeetJson library", Path.Combine("YeetJson.lib", "YeetJson", "YeetJson.csproj")),
        ("YeetCode library", Path.Combine("YeetCode.lib", "YeetCode", "YeetCode.csproj")),
        ("CLI tool",         Path.Combine("YeetCode.CLI", "YeetCode.CLI.csproj")),
        ("MSBuild task",     Path.Combine("YeetCode.MSBuild", "YeetCode.MSBuild.csproj")),
    ];

    for (int packStepIndex = 0; packStepIndex < packSteps.Length; packStepIndex++)
    {
        if (_cts.IsCancellationRequested) break;

        var (label, csprojRelPath) = packSteps[packStepIndex];
        string csprojFullPath = Path.Combine(projectRoot, csprojRelPath);

        Console.WriteLine();
        WriteColor($"[{packStepIndex + 2}/5] Packing {label}...", ConsoleColor.Green);

        int packResult = RunProcess("dotnet",
            $"pack \"{csprojFullPath}\" -c {configuration} --no-build /nodeReuse:false {versionArgs}");

        if (packResult != 0)
        {
            string msg = $"dotnet pack ({label}) failed with exit code {packResult}";
            failedSteps.Add(msg);
            WriteColor($"ERROR: {msg}", ConsoleColor.Red);
            break; // Stop packing on first failure (dependency order matters)
        }
    }
}

// ── Final status ─────────────────────────────────────────────────────────
Console.WriteLine();
if (failedSteps.Count > 0)
{
    WriteColor("╔══════════════════════════════════════════════════════════════╗", ConsoleColor.Red);
    WriteColor("║                    PUBLISH FAILED                           ║", ConsoleColor.Red);
    WriteColor("╚══════════════════════════════════════════════════════════════╝", ConsoleColor.Red);
    foreach (string step in failedSteps)
        WriteColor($"  ✗ {step}", ConsoleColor.Red);
    Console.WriteLine();
    Environment.Exit(1);
}
else
{
    // Show packages deployed during this run
    var deployedPackages = Directory.Exists(localNuGetFeedPath)
        ? Directory.GetFiles(localNuGetFeedPath, "*.nupkg")
            .Select(f => new FileInfo(f))
            .Where(fi => fi.LastWriteTime >= deployStartTime)
            .OrderBy(fi => fi.Name)
            .ToArray()
        : [];

    WriteColor("╔══════════════════════════════════════════════════════════════╗", ConsoleColor.Green);
    WriteColor("║                   PUBLISH SUCCEEDED                         ║", ConsoleColor.Green);
    WriteColor("╚══════════════════════════════════════════════════════════════╝", ConsoleColor.Green);
    WriteColor($"  Version:  {packageVersion}", ConsoleColor.Green);
    WriteColor($"  Config:   {configuration}", ConsoleColor.Green);
    WriteColor($"  Feed:     {localNuGetFeedPath}", ConsoleColor.Green);

    if (deployedPackages.Length > 0)
    {
        Console.WriteLine();
        WriteColor("Deployed packages:", ConsoleColor.Cyan);
        foreach (var pkg in deployedPackages)
        {
            double sizeKB = Math.Round(pkg.Length / 1024.0, 1);
            WriteColor($"  {pkg.Name}  ({sizeKB} KB)", ConsoleColor.Green);
        }
    }
    else
    {
        WriteColor($"WARNING: No packages were deployed to {localNuGetFeedPath}", ConsoleColor.Yellow);
    }
    Console.WriteLine();
}

// ── Helper functions ─────────────────────────────────────────────────────

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