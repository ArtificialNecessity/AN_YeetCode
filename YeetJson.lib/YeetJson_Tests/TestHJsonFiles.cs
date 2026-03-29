using YeetJson;
using Xunit;

namespace YeetJson_Tests;

/// <summary>
/// Basic gold file smoke test — verifies valid_simple.hjson parses without errors.
/// For full gold file testing with --diff and --update, use the standalone CLI:
///   dotnet run YeetJson.lib/YeetJson_Tests/Scripts/TestGold.cs [--diff] [--update]
/// </summary>
public class TestHJsonFiles
{
    [Fact]
    public void TestValidSimpleHjsonParsesWithoutErrors()
    {
        string testFilePath = Path.Combine("TestData", "valid_simple.hjson");
        string hjsonSourceText = File.ReadAllText(testFilePath);

        var structuralAnalyzer = new StructuralAnalyzer();
        var structureResult = structuralAnalyzer.Analyze(hjsonSourceText);

        var hjsonContentParser = new HjsonContentParser();
        var parseResult = hjsonContentParser.Parse(hjsonSourceText, structureResult);

        Assert.NotNull(parseResult.ParsedDocument);
        Assert.Empty(parseResult.StructuralErrors);
    }
}