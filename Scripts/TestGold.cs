#!/usr/bin/env dotnet run
// YeetJson Gold File Tester — standalone CLI for comparing diagnostic output against gold files
//
// Usage:
//   dotnet run Scripts/TestGold.cs                    # compare all gold files
//   dotnet run Scripts/TestGold.cs --diff             # show detailed diff on mismatch
//   dotnet run Scripts/TestGold.cs --update           # regenerate all gold files
//   dotnet run Scripts/TestGold.cs --help             # show this help
//
// Gold files live in: YeetJson.lib/YeetJson_Tests/TestData/*.hjson.gold

#:project ../YeetJson.lib/YeetJson/YeetJson.csproj

using YeetJson;

string testDataDirectory = Path.Combine("YeetJson.lib", "YeetJson_Tests", "TestData");

// Parse command-line args
bool showDiff = args.Contains("--diff");
bool updateGold = args.Contains("--update");
bool showHelp = args.Contains("--help") || args.Contains("-h");

if (showHelp)
{
    Console.WriteLine("YeetJson Gold File Tester");
    Console.WriteLine();
    Console.WriteLine("USAGE:");
    Console.WriteLine("  dotnet run Scripts/TestGold.cs              Compare all .hjson files against .gold files");
    Console.WriteLine("  dotnet run Scripts/TestGold.cs --diff       Show detailed diff when gold files don't match");
    Console.WriteLine("  dotnet run Scripts/TestGold.cs --update     Regenerate all .gold files from current output");
    Console.WriteLine("  dotnet run Scripts/TestGold.cs --help       Show this help");
    Console.WriteLine();
    Console.WriteLine($"Test data directory: {testDataDirectory}");
    return;
}

if (!Directory.Exists(testDataDirectory))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"ERROR: Test data directory not found: {testDataDirectory}");
    Console.WriteLine("Run this script from the repository root.");
    Console.ResetColor();
    Environment.Exit(1);
}

// Find all .hjson test files
string[] hjsonTestFiles = Directory.GetFiles(testDataDirectory, "*.hjson")
    .Where(filePath => !filePath.EndsWith(".gold"))
    .OrderBy(filePath => filePath)
    .ToArray();

if (hjsonTestFiles.Length == 0)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"No .hjson test files found in {testDataDirectory}");
    Console.ResetColor();
    Environment.Exit(1);
}

Console.WriteLine($"YeetJson Gold File Tester -- {hjsonTestFiles.Length} test files");
Console.WriteLine($"Mode: {(updateGold ? "UPDATE" : showDiff ? "DIFF" : "COMPARE")}");
Console.WriteLine();

int passedCount = 0;
int failedCount = 0;
int updatedCount = 0;
int skippedCount = 0;

foreach (string hjsonFilePath in hjsonTestFiles)
{
    string fileName = Path.GetFileName(hjsonFilePath);
    string goldFilePath = hjsonFilePath + ".gold";

    // Parse the HJSON file
    string hjsonSourceText = File.ReadAllText(hjsonFilePath);
    var structuralAnalyzer = new StructuralAnalyzer();
    var structureResult = structuralAnalyzer.Analyze(hjsonSourceText);
    var hjsonContentParser = new HjsonContentParser();
    var parseResult = hjsonContentParser.Parse(hjsonSourceText, structureResult);

    // Format diagnostic output
    var diagnosticFormatter = new DiagnosticFormatter();
    string actualOutput = diagnosticFormatter.FormatForAI(parseResult, hjsonSourceText, isTestMode: true, sourceFilePath: hjsonFilePath);
    string normalizedActual = actualOutput.Trim().Replace("\r\n", "\n");

    if (updateGold)
    {
        // Write actual output to gold file
        File.WriteAllBytes(goldFilePath, System.Text.Encoding.ASCII.GetBytes(normalizedActual + "\n"));
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"  UPDATED  {fileName}.gold");
        Console.ResetColor();
        updatedCount++;
        continue;
    }

    if (!File.Exists(goldFilePath))
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  SKIP     {fileName}  (no .gold file -- run with --update to create)");
        Console.ResetColor();
        skippedCount++;
        continue;
    }

    // Compare against gold file using raw bytes to avoid encoding issues
    byte[] goldFileBytes = File.ReadAllBytes(goldFilePath);
    byte[] actualBytes = System.Text.Encoding.ASCII.GetBytes(normalizedActual + "\n");

    if (goldFileBytes.SequenceEqual(actualBytes))
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  PASS     {fileName}");
        Console.ResetColor();
        passedCount++;
    }
    else
    {
        // Write actual output to .testout for easy diffing
        string testoutFilePath = hjsonFilePath + ".testout";
        File.WriteAllBytes(testoutFilePath, System.Text.Encoding.ASCII.GetBytes(normalizedActual + "\n"));

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  FAIL     {fileName}");
        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"    Wrote:  {testoutFilePath}");
        Console.WriteLine($"    Diff:   diff \"{goldFilePath}\" \"{testoutFilePath}\"");
        Console.ResetColor();
        failedCount++;
    }
}

// Summary
Console.WriteLine();
Console.WriteLine("-------------------------------------");
if (updateGold)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"Updated {updatedCount} gold files.");
    Console.ResetColor();
}
else
{
    string summaryColor = failedCount > 0 ? "Red" : "Green";
    Console.ForegroundColor = failedCount > 0 ? ConsoleColor.Red : ConsoleColor.Green;
    Console.WriteLine($"Results: {passedCount} passed, {failedCount} failed, {skippedCount} skipped");
    Console.ResetColor();

    if (failedCount > 0)
    {
        Console.WriteLine();
        Console.WriteLine("USAGE:");
        Console.WriteLine("  dotnet run Scripts/TestGold.cs --diff       Show what changed");
        Console.WriteLine("  dotnet run Scripts/TestGold.cs --update     Accept current output as new gold");
        Environment.Exit(1);
    }
}