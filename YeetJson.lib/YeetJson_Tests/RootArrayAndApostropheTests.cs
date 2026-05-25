using System.Text.Json;
using YeetJson;
using Xunit;

namespace YeetJson_Tests;

/// <summary>
/// Tests for root arrays and apostrophes in unquoted values.
/// Regression tests for:
///   1. Root-level arrays (HJSON spec allows [...] at root)
///   2. Apostrophes in unquoted values (e.g. "Zoe's Farm") should NOT be treated as string delimiters
///   3. Values with commas must be quoted per HJSON spec
/// </summary>
public class RootArrayAndApostropheTests
{
    [Fact]
    public void RootArray_ParsesCorrectly()
    {
        string hjson = """
            [
              {
                path: ./
                type: narrative/chapters/prose
              }
            ]
            """;

        var structuralAnalyzer = new StructuralAnalyzer();
        var structureResult = structuralAnalyzer.Analyze(hjson);

        Assert.Empty(structureResult.StructuralErrors);

        var contentParser = new HjsonContentParser();
        var parseResult = contentParser.Parse(hjson, structureResult);

        Assert.Empty(parseResult.SemanticErrors);
        Assert.NotNull(parseResult.ParsedDocument);
        Assert.Equal(JsonValueKind.Array, parseResult.ParsedDocument.RootElement.ValueKind);
        Assert.Equal(1, parseResult.ParsedDocument.RootElement.GetArrayLength());

        var entry = parseResult.ParsedDocument.RootElement[0];
        Assert.Equal("./", entry.GetProperty("path").GetString());
        Assert.Equal("narrative/chapters/prose", entry.GetProperty("type").GetString());
    }

    [Fact]
    public void ApostropheInUnquotedValue_NoStructuralErrors()
    {
        // Apostrophe in unquoted value (no comma, so it's valid unquoted)
        string hjson = """
            [
              {
                path: ./
                type: narrative/chapters/prose
                title: Zoe's Farm
                description: Prose-format chapter files
              }
            ]
            """;

        var structuralAnalyzer = new StructuralAnalyzer();
        var structureResult = structuralAnalyzer.Analyze(hjson);

        // The apostrophe in "Zoe's" must NOT be treated as a string delimiter
        Assert.Empty(structureResult.StructuralErrors);

        var contentParser = new HjsonContentParser();
        var parseResult = contentParser.Parse(hjson, structureResult);

        Assert.Empty(parseResult.SemanticErrors);
        Assert.NotNull(parseResult.ParsedDocument);

        var entry = parseResult.ParsedDocument.RootElement[0];
        Assert.Equal("Zoe's Farm", entry.GetProperty("title").GetString());
    }

    [Fact]
    public void QuotedValueWithCommaAndApostrophe_ParsesCorrectly()
    {
        // Values with commas MUST be quoted per HJSON spec
        string hjson = """
            [
              {
                path: ./
                type: narrative/chapters/prose
                title: "Zoe's Farm, Book 1"
                description: Prose-format chapter files
              }
            ]
            """;

        var structuralAnalyzer = new StructuralAnalyzer();
        var structureResult = structuralAnalyzer.Analyze(hjson);
        Assert.Empty(structureResult.StructuralErrors);

        var contentParser = new HjsonContentParser();
        var parseResult = contentParser.Parse(hjson, structureResult);

        Assert.Empty(parseResult.SemanticErrors);
        Assert.NotNull(parseResult.ParsedDocument);
        Assert.Equal("Zoe's Farm, Book 1", parseResult.ParsedDocument.RootElement[0].GetProperty("title").GetString());
    }

    [Fact]
    public void RootArrayMultipleEntries_ParsesCorrectly()
    {
        string hjson = """
            [
              {
                path: ./
                type: narrative/chapters/prose
                title: Book 1
              }
              {
                path: ./
                type: narrative/chapters/screenplay_lite
                title: Book 1 (Screenplay)
              }
            ]
            """;

        var structuralAnalyzer = new StructuralAnalyzer();
        var structureResult = structuralAnalyzer.Analyze(hjson);
        Assert.Empty(structureResult.StructuralErrors);

        var contentParser = new HjsonContentParser();
        var parseResult = contentParser.Parse(hjson, structureResult);

        Assert.Empty(parseResult.SemanticErrors);
        Assert.NotNull(parseResult.ParsedDocument);
        Assert.Equal(2, parseResult.ParsedDocument.RootElement.GetArrayLength());
        Assert.Equal("Book 1", parseResult.ParsedDocument.RootElement[0].GetProperty("title").GetString());
        Assert.Equal("Book 1 (Screenplay)", parseResult.ParsedDocument.RootElement[1].GetProperty("title").GetString());
    }
}
