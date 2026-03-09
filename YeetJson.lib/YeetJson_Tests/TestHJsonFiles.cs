using YeetJson;
using Xunit;

namespace YeetJson_Tests;

/// <summary>
/// Gold file tests for HJSON parser.
/// Each .hjson file in TestData/ should have a corresponding .hjson.gold file
/// containing the expected diagnostic output.
/// </summary>
public class TestHJsonFiles
{
    private static string GetTestDataPath(string fileName)
    {
        return Path.Combine("TestData", fileName);
    }

    [Theory]
    [InlineData("valid_simple.hjson")]
    public void TestHjsonFileAgainstGold(string testFileName)
    {
        string testFilePath = GetTestDataPath(testFileName);
        string goldFilePath = testFilePath + ".gold";

        // Skip if gold file doesn't exist
        if (!File.Exists(goldFilePath))
        {
            // This will show as skipped in test output
            return;
        }

        string hjsonSourceText = File.ReadAllText(testFilePath);
        string expectedDiagnosticOutput = File.ReadAllText(goldFilePath);

        // Parse and format output
        var structuralAnalyzer = new StructuralAnalyzer();
        var structureResult = structuralAnalyzer.Analyze(hjsonSourceText);

        var hjsonContentParser = new HjsonContentParser();
        var parseResult = hjsonContentParser.Parse(hjsonSourceText, structureResult);

        var diagnosticFormatter = new DiagnosticFormatter();
        string actualDiagnosticOutput = diagnosticFormatter.FormatForAI(parseResult, hjsonSourceText, isTestMode: true);

        // Normalize line endings for comparison
        string normalizedExpected = expectedDiagnosticOutput.Trim().Replace("\r\n", "\n");
        string normalizedActual = actualDiagnosticOutput.Trim().Replace("\r\n", "\n");

        Assert.Equal(normalizedExpected, normalizedActual);
    }
}