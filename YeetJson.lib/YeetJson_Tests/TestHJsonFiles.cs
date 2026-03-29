using YeetJson;
using Xunit;
using Xunit.Abstractions;

namespace YeetJson_Tests;

/// <summary>
/// Gold file tests for HJSON parser.
/// Each .hjson file in TestData/ should have a corresponding .hjson.gold file
/// containing the expected diagnostic output.
///
/// Command-line flags (via dotnet test -e):
///   --diff-gold:     dotnet test [project] -e YEETJSON_DIFF_GOLD=true
///   --update-gold:   dotnet test [project] -e YEETJSON_UPDATE_GOLD=true
///
/// Examples:
///   dotnet test YeetJson.lib/YeetJson_Tests                                          # normal comparison
///   dotnet test YeetJson.lib/YeetJson_Tests -e YEETJSON_DIFF_GOLD=true               # show diffs on failure
///   dotnet test YeetJson.lib/YeetJson_Tests -e YEETJSON_UPDATE_GOLD=true             # regenerate gold files
/// </summary>
public class TestHJsonFiles
{
    private readonly ITestOutputHelper _testOutput;

    public TestHJsonFiles(ITestOutputHelper testOutput)
    {
        _testOutput = testOutput;
    }

    private static string GetTestDataPath(string fileName)
    {
        return Path.Combine("TestData", fileName);
    }

    /// <summary>
    /// Find the source TestData directory (in the project tree, not bin/Debug output).
    /// Needed for --update-gold to write back to the correct location.
    /// </summary>
    private static string FindSourceTestDataDirectory()
    {
        string? searchDirectory = AppContext.BaseDirectory;
        while (searchDirectory != null)
        {
            string candidatePath = Path.Combine(searchDirectory, "YeetJson_Tests.csproj");
            if (File.Exists(candidatePath))
            {
                return Path.Combine(searchDirectory, "TestData");
            }
            searchDirectory = Path.GetDirectoryName(searchDirectory);
        }
        // Fallback: assume bin/Debug/net10.0 — go up 3 levels
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestData"));
    }

    [Theory]
    [InlineData("valid_simple.hjson")]
    [InlineData("broken_mismatch.hjson")]
    [InlineData("broken_unclosed.hjson")]
    [InlineData("broken_multiple.hjson")]
    public void TestHjsonFileAgainstGold(string testFileName)
    {
        string testFilePath = GetTestDataPath(testFileName);
        string goldFilePath = testFilePath + ".gold";
        bool shouldUpdateGold = Environment.GetEnvironmentVariable("YEETJSON_UPDATE_GOLD") == "true";
        bool shouldShowDiff = Environment.GetEnvironmentVariable("YEETJSON_DIFF_GOLD") == "true";

        string hjsonSourceText = File.ReadAllText(testFilePath);

        // Parse and format output
        var structuralAnalyzer = new StructuralAnalyzer();
        var structureResult = structuralAnalyzer.Analyze(hjsonSourceText);

        var hjsonContentParser = new HjsonContentParser();
        var parseResult = hjsonContentParser.Parse(hjsonSourceText, structureResult);

        var diagnosticFormatter = new DiagnosticFormatter();
        string actualDiagnosticOutput = diagnosticFormatter.FormatForAI(parseResult, hjsonSourceText, isTestMode: true);

        // Normalize line endings
        string normalizedActual = actualDiagnosticOutput.Trim().Replace("\r\n", "\n");

        // Update gold mode: write actual output to source gold file
        if (shouldUpdateGold)
        {
            string sourceTestDataDir = FindSourceTestDataDirectory();
            string sourceGoldFilePath = Path.Combine(sourceTestDataDir, testFileName + ".gold");
            File.WriteAllText(sourceGoldFilePath, normalizedActual + "\n");
            _testOutput.WriteLine($"UPDATED gold file: {sourceGoldFilePath}");

            // Also update the output copy so subsequent comparisons pass
            File.WriteAllText(goldFilePath, normalizedActual + "\n");
            return; // Don't assert — we just updated
        }

        // Skip if gold file doesn't exist (new test file without gold yet)
        if (!File.Exists(goldFilePath))
        {
            _testOutput.WriteLine($"SKIPPED: No gold file at {goldFilePath}");
            _testOutput.WriteLine($"Run with YEETJSON_UPDATE_GOLD=true to generate it.");
            return;
        }

        string expectedDiagnosticOutput = File.ReadAllText(goldFilePath);
        string normalizedExpected = expectedDiagnosticOutput.Trim().Replace("\r\n", "\n");

        if (normalizedExpected != normalizedActual)
        {
            // Always show usage help on gold file mismatch
            _testOutput.WriteLine($"");
            _testOutput.WriteLine($"===[ Gold file mismatch: {testFileName} ]===");
            _testOutput.WriteLine($"");

            if (shouldShowDiff)
            {
                _testOutput.WriteLine("--- EXPECTED (gold file) ---");
                _testOutput.WriteLine(normalizedExpected);
                _testOutput.WriteLine("");
                _testOutput.WriteLine("--- ACTUAL (current output) ---");
                _testOutput.WriteLine(normalizedActual);
                _testOutput.WriteLine("");
            }

            _testOutput.WriteLine("USAGE: Gold file management commands:");
            _testOutput.WriteLine("  Show diff:      dotnet test YeetJson.lib/YeetJson_Tests -e YEETJSON_DIFF_GOLD=true");
            _testOutput.WriteLine("  Update gold:    dotnet test YeetJson.lib/YeetJson_Tests -e YEETJSON_UPDATE_GOLD=true");
            _testOutput.WriteLine("");
        }

        Assert.Equal(normalizedExpected, normalizedActual);
    }
}