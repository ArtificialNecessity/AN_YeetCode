# How to Write C# Scripts (.NET 9/10)

## Overview

This project uses modern .NET 9/10 scripting capabilities that allow running `.cs` files directly without creating a full project. This is cleaner than the legacy `#r` directive pattern.

## Running a Script

```powershell
dotnet run Scripts/YourScript.cs [args...]
```

---

## Modern Directives (C# 14 / .NET 10)

**NOT the old `#r` style!** Use these modern directives:

| Directive | Usage | Example |
|-----------|-------|---------|
| `#:package` | NuGet Packages | `#:package SixLabors.ImageSharp@3.1.12` |
| `#:project` | Local Projects | `#:project "../Core/Core.csproj"` |
| `#:reference` | Local DLLs | `#:reference "libs/Legacy.dll"` |
| `#:sdk` | Change SDK | `#:sdk Microsoft.NET.Sdk.Web` (for APIs) |

### `#:package` - NuGet Package Reference

```csharp
#:package Google.Cloud.Firestore@3.9.0
#:package SixLabors.ImageSharp@3.1.12
#:package Newtonsoft.Json@13.0.3
```

### `#:project` - Reference Existing .csproj

```csharp
#:project apiserver/apiserver.csproj
#:project ../Core/Core.csproj
```

### `#:reference` - Reference Local DLLs

```csharp
#:reference "libs/Legacy.dll"
#:reference "bin/MyLib.dll"
```

### `#:sdk` - Change SDK Type

```csharp
#:sdk Microsoft.NET.Sdk.Web
```

---

## Authentication with GCP Secret Manager

### Preferred: Application Default Credentials (ADC)

Scripts should use `gcloud` Application Default Credentials rather than hardcoded credential files:

```csharp
// No explicit credential setup needed - uses gcloud auth
var builder = new FirestoreDbBuilder
{
    ProjectId = "your-project-id",
    DatabaseId = "your-database-id"
};
FirestoreDb db = builder.Build();
```

**Setup (one-time):**
```powershell
gcloud auth application-default login
```

### Fallback: Environment Variable (same as apiserver)

For consistency with the apiserver, check `FIREBASE_KEY_JSON` env var:

```csharp
GoogleCredential? credential = null;
var firebaseKeyJson = Environment.GetEnvironmentVariable("FIREBASE_KEY_JSON");
if (!string.IsNullOrEmpty(firebaseKeyJson))
{
    credential = GoogleCredential.FromJson(firebaseKeyJson);
}

var builder = new FirestoreDbBuilder
{
    ProjectId = projectId,
    DatabaseId = databaseId
};
if (credential != null) builder.Credential = credential;
FirestoreDb db = builder.Build();
```

---

## ⚠️ CRITICAL: Script Location

### DO NOT place scripts inside directories containing a `.csproj` file!

**Why?** The `dotnet run` command inherits the `.csproj` environment from parent directories, which causes conflicts with the script's `#:package` and `#:project` directives.

**✅ Good locations:**
```
Scripts/MyScript.cs           # Top-level Scripts folder (no .csproj here)
tools/migration.cs            # Dedicated tools folder (no .csproj here)
```

**❌ Bad locations:**
```
apiserver/Scripts/MyScript.cs     # apiserver.csproj exists in parent
utils/MyLib/migrate.cs            # MyLib.csproj exists in same directory
```

---

## Troubleshooting: Scripts Inside Project Directories

If you **must** place a script inside a directory that contains (or is a child of) a `.csproj`, you can enable file-based programs support:

### Option 1: Add `Directory.Build.props`

Create a `Directory.Build.props` file in the directory where your script lives:

```xml
<Project>
  <PropertyGroup>
    <LangVersion>preview</LangVersion>
    <Features>FileBasedPrograms</Features>
  </PropertyGroup>
</Project>
```

### Option 2: Update Parent `.csproj` Files

Ensure any parent `.csproj` is targeting `net9.0` or `net10.0` and has the Features flag:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Features>FileBasedPrograms</Features>
    <!-- ... other settings ... -->
  </PropertyGroup>
</Project>
```

### Common Symptoms

If scripts won't run inside project directories, you'll see errors like:
- `error CS8805: Program using top-level statements must be an executable.`
- `error CS5001: Program does not contain a static 'Main' method suitable for an entry point`
- Package restoration failures despite correct `#:package` directives

---

## Script Template

```csharp
#!/usr/bin/env dotnet run
// Description: What this script does
// Usage: dotnet run Scripts/YourScript.cs [arg1] [arg2]
// Example: dotnet run Scripts/YourScript.cs "(default)" an-prod

#:package Google.Cloud.Firestore@3.9.0

using Google.Cloud.Firestore;
using Google.Apis.Auth.OAuth2;

// Parse args with defaults
string arg1 = args.Length > 0 ? args[0] : "default-value";
string arg2 = args.Length > 1 ? args[1] : "another-default";

Console.WriteLine($"Running with: {arg1}, {arg2}");

// Your script logic here...
```

---

## User Confirmation Pattern

For destructive operations, require explicit user confirmation:

```csharp
Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine("╔════════════════════════════════════════════╗");
Console.WriteLine("║  WARNING: This will modify your database!  ║");
Console.WriteLine($"║  Target: {targetDatabase,-30}  ║");
Console.WriteLine("╚════════════════════════════════════════════╝");
Console.ResetColor();
Console.WriteLine();
Console.Write("Type 'yes' to confirm and proceed: ");

var userConfirmation = Console.ReadLine()?.Trim().ToLowerInvariant();
if (userConfirmation != "yes")
{
    Console.WriteLine("\nOperation cancelled by user.");
    return;
}
```

---

## Batching Firestore Writes

For large data migrations, batch writes (max 500 per batch):

```csharp
const int BATCH_SIZE = 500;
var documents = snapshot.Documents.ToList();
int processed = 0;

while (processed < documents.Count)
{
    var batch = targetDb.StartBatch();
    int batchItemCount = 0;

    while (processed < documents.Count && batchItemCount < BATCH_SIZE)
    {
        var doc = documents[processed];
        var targetRef = targetDb.Collection("target").Document(doc.Id);
        batch.Set(targetRef, doc.ToDictionary());
        processed++;
        batchItemCount++;
    }

    await batch.CommitAsync();
    Console.WriteLine($"Batch committed: {processed}/{documents.Count}");
}
```

---

## Example Scripts in This Repository

| Script | Purpose |
|--------|---------|
| `MigrateDevnullMessages.cs` | Copy messages within same database |
| `MigrateCrossDatabase.cs` | Copy data between Firestore databases with batching |
| `ValidateApiserverHJSON.cs` | Validate HJSON files in apiserver |
| `CartesiaDocsScraper.cs` | Web scraping utility |

---

## IDE Errors are Expected

Your IDE (VS Code, Rider, Visual Studio) will show errors like:
- `The type or namespace name 'Google' could not be found`
- `The type or namespace name 'FirestoreDb' could not be found`

**This is normal!** The IDE doesn't understand `#:package` directives. The script will compile and run correctly with `dotnet run`.