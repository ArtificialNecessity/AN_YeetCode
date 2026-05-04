#!/usr/bin/env dotnet run
// publish-local.cs — Build and pack YeetCode packages to local NuGet feed
//
// Cross-platform replacement for publish-local.ps1.
// Increments buildNumberOffset in version.jsonc using System.Text.Json,
// then packs all packable projects with clean (non-prerelease) version numbers.
// The MSBuild DeployToLocalNuGet target handles copying .nupkg to LOCAL_NUGET_REPO.
//
// Versioning is handled entirely by MSBuild targets in YeetCode.shared.Build.props:
//   - Stable (default): auto-increments buildNumberOffset in version.jsonc → 0.1.1, 0.1.2, ...
//   - Prerelease (--prerelease): uses git height suffix → 0.1.0-27.0.g3be210cd
//
// Usage:
//   dotnet run cmd/publish-local.cs                    # stable build + pack + deploy
//   dotnet run cmd/publish-local.cs --release          # Release configuration
//   dotnet run cmd/publish-local.cs --prerelease       # prerelease versions (no auto-increment)
//   dotnet run cmd/publish-local.cs --dry-run          # show what would happen, don't build
//
// Requires: LOCAL_NUGET_REPO environment variable set to local feed path

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

// ── Parse arguments ─────────────────────────────────────────────────────
bool dryRun = args.Any(a => a is "--dry-run" or "-n");
bool release = args.Any(a => a is "--release" or "-r");
bool prerelease = args.Any(a => a is "--prerelease" or "-p");

string configuration = release ? "Release" : "Debug";
string versionLabel = prerelease ? "prerelease" : "stable";

// ── Resolve project root (the script lives in cmd/) ─────────────────────
// When invoked via `dotnet run cmd/publish-local.cs` from the repo root,
// the working directory is the repo root. Try current directory first,
// then fall back to navigating up from the script's known location.
string projectRoot = Directory.GetCurrentDirectory();

// If version.jsonc isn't in cwd, we might be running from cmd/ — go up one level
if (!File.Exists(Path.Combine(projectRoot, "version.jsonc")))
{
    projectRoot = Path.GetFullPath(Path.Combine(projectRoot, ".."));
}

// Final check
if (!File.Exists(Path.Combine(projectRoot, "version.jsonc")))
{
    WriteColor($"ERROR: Cannot locate version.jsonc. Run from the repo root:", ConsoleColor.Red);
    WriteColor("  dotnet run cmd/publish-local.cs", ConsoleColor.Yellow);
    Environment.Exit(1);
}

string versionJsoncPath = Path.Combine(projectRoot, "version.jsonc");
string slnPath = Path.Combine(projectRoot, "AN_YeetCode.sln");

Console.WriteLine();
WriteColor($"=== YeetCode publish-local ({configuration}, {versionLabel}) ===", ConsoleColor.Cyan);

// ── Require LOCAL_NUGET_REPO environment variable ───────────────────────
string? localNuGetFeedPath = Environment.GetEnvironmentVariable("LOCAL_NUGET_REPO");
if (string.IsNullOrEmpty(localNuGetFeedPath))
{
    WriteColor("ERROR: LOCAL_NUGET_REPO environment variable not set.", ConsoleColor.Red);
    WriteColor("  Set it to your local NuGet feed path, e.g.:", ConsoleColor.Yellow);
    WriteColor("    export LOCAL_NUGET_REPO=\"/path/to/LocalNuGet\"", ConsoleColor.Yellow);
    WriteColor("    $env:LOCAL_NUGET_REPO = \"C:\\PROJECTS\\LocalNuGet\"", ConsoleColor.Yellow);
    Environment.Exit(1);
}

WriteColor($"Local NuGet feed: {localNuGetFeedPath}", ConsoleColor.DarkGray);

// ── Read and parse version.jsonc ────────────────────────────────────────
if (!File.Exists(versionJsoncPath))
{
    WriteColor($"ERROR: version.jsonc not found at: {versionJsoncPath}", ConsoleColor.Red);
    Environment.Exit(1);
}

string versionJsoncText = File.ReadAllText(versionJsoncPath);
// Strip JSONC comments (// line comments) for System.Text.Json parsing
string jsonWithoutComments = Regex.Replace(versionJsoncText, @"//.*$", "", RegexOptions.Multiline);
var versionDoc = JsonNode.Parse(jsonWithoutComments)!;

string baseVersion = versionDoc["version"]!.GetValue<string>();
int currentBuildNumberOffset = versionDoc["buildNumberOffset"]!.GetValue<int>();

// ── Increment buildNumberOffset for stable versions ─────────────────────
int newBuildNumberOffset = currentBuildNumberOffset;
string prereleaseFlag = "";

if (prerelease)
{
    prereleaseFlag = "-p:Prerelease=true";
    WriteColor($"Version: {baseVersion}.{currentBuildNumberOffset} (prerelease, no increment)", ConsoleColor.Yellow);
}
else
{
    newBuildNumberOffset = currentBuildNumberOffset + 1;

    // Write back to version.jsonc, preserving the original format (comments, whitespace)
    string updatedVersionJsoncText = Regex.Replace(
        versionJsoncText,
        @"(""buildNumberOffset"":\s*)\d+",
        $"${{1}}{newBuildNumberOffset}");
    File.WriteAllText(versionJsoncPath, updatedVersionJsoncText);

    WriteColor($"Version: {baseVersion}.{newBuildNumberOffset} (buildNumberOffset incremented)", ConsoleColor.Yellow);
}

string newVersion = $"{baseVersion}.{newBuildNumberOffset}";

if (dryRun)
{
    Console.WriteLine();
    WriteColor($"[DRY RUN] Would build {configuration} version {newVersion} and deploy to {localNuGetFeedPath}", ConsoleColor.Yellow);
    if (!prerelease)
    {
        // Revert the increment
        File.WriteAllText(versionJsoncPath, versionJsoncText);
        WriteColor($"[DRY RUN] Reverted buildNumberOffset back to {currentBuildNumberOffset}", ConsoleColor.Yellow);
    }
    Environment.Exit(0);
}

// ── Capture timestamp before build/pack so we can identify newly deployed packages ──
var deployStartTime = DateTime.Now;

// ── Build and pack ──────────────────────────────────────────────────────
Environment.SetEnvironmentVariable("LOCAL_NUGET_REPO", localNuGetFeedPath);

var failedSteps = new List<string>();

// Step 1: Build the solution
Console.WriteLine();
WriteColor("[1/5] Building solution...", ConsoleColor.Green);
int buildResult = RunProcess("dotnet",
    $"build \"{slnPath}\" -c {configuration} {prereleaseFlag}".Trim());
if (buildResult != 0)
{
    failedSteps.Add($"dotnet build failed with exit code {buildResult}");
    WriteColor($"ERROR: Build failed with exit code {buildResult}", ConsoleColor.Red);
}

// Step 2-5: Pack libraries in dependency order (YeetJson → YeetCode → CLI → MSBuild)
if (failedSteps.Count == 0)
{
    (string label, string csprojRelPath)[] packSteps =
    [
        ("YeetJson library",  Path.Combine("YeetJson.lib", "YeetJson", "YeetJson.csproj")),
        ("YeetCode library",  Path.Combine("YeetCode.lib", "YeetCode", "YeetCode.csproj")),
        ("CLI tool",          Path.Combine("YeetCode.CLI", "YeetCode.CLI.csproj")),
        ("MSBuild task",      Path.Combine("YeetCode.MSBuild", "YeetCode.MSBuild.csproj")),
    ];

    for (int i = 0; i < packSteps.Length; i++)
    {
        var (label, csprojRelPath) = packSteps[i];
        string csprojFullPath = Path.Combine(projectRoot, csprojRelPath);

        Console.WriteLine();
        WriteColor($"[{i + 2}/5] Packing {label}...", ConsoleColor.Green);

        int packResult = RunProcess("dotnet",
            $"pack \"{csprojFullPath}\" -c {configuration} {prereleaseFlag}".Trim());

        if (packResult != 0)
        {
            string msg = $"dotnet pack ({label}) failed with exit code {packResult}";
            failedSteps.Add(msg);
            WriteColor($"ERROR: {msg}", ConsoleColor.Red);
            break; // Stop packing on first failure (dependency order matters)
        }
    }
}

// ── Show deployed packages ──────────────────────────────────────────────
if (failedSteps.Count == 0 && Directory.Exists(localNuGetFeedPath))
{
    Console.WriteLine();
    WriteColor("=== Verifying deployment to local NuGet feed ===", ConsoleColor.Cyan);

    var deployedPackages = Directory.GetFiles(localNuGetFeedPath, "*.nupkg")
        .Select(f => new FileInfo(f))
        .Where(f => f.LastWriteTime >= deployStartTime)
        .OrderBy(f => f.Name)
        .ToList();

    if (deployedPackages.Count > 0)
    {
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
}

// ── Final status banner ─────────────────────────────────────────────────
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
    WriteColor("╔══════════════════════════════════════════════════════════════╗", ConsoleColor.Green);
    WriteColor("║                   PUBLISH SUCCEEDED                         ║", ConsoleColor.Green);
    WriteColor("╚══════════════════════════════════════════════════════════════╝", ConsoleColor.Green);
    WriteColor($"  Version:  {newVersion}", ConsoleColor.Green);
    WriteColor($"  Config:   {configuration}", ConsoleColor.Green);
    WriteColor($"  Feed:     {localNuGetFeedPath}", ConsoleColor.Green);
    Console.WriteLine();
}

// ── Helper functions ────────────────────────────────────────────────────

static void WriteColor(string message, ConsoleColor color)
{
    var prev = Console.ForegroundColor;
    Console.ForegroundColor = color;
    Console.WriteLine(message);
    Console.ForegroundColor = prev;
}

static int RunProcess(string fileName, string arguments)
{
    var psi = new ProcessStartInfo
    {
        FileName = fileName,
        Arguments = arguments,
        UseShellExecute = false,
    };

    using var process = Process.Start(psi);
    process!.WaitForExit();
    return process.ExitCode;
}
