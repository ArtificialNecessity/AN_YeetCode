using System.Text.Json;
using HJSONParserForAI.Core;
using Xunit;

namespace HJSONParserForAI_Tests;

/// <summary>
/// Smoke tests for the HJSON content parser.
/// Tests basic parsing functionality with valid HJSON files.
/// </summary>
public class SmokeTest
{
    private static string GetTestDataPath(string fileName)
    {
        return Path.Combine("TestData", fileName);
    }

    [Fact]
    public void TestValidSimpleHjson()
    {
        string filePath = GetTestDataPath("valid_simple.hjson");
        string hjsonSourceText = File.ReadAllText(filePath);

        // Phase 1: Structural analysis
        var structuralAnalyzer = new StructuralAnalyzer();
        var structureResult = structuralAnalyzer.Analyze(hjsonSourceText);

        Assert.Empty(structureResult.StructuralErrors);

        // Phase 2: Content parsing
        var hjsonContentParser = new HjsonContentParser();
        var parseResult = hjsonContentParser.Parse(hjsonSourceText, structureResult);

        Assert.Empty(parseResult.SemanticErrors);
        Assert.NotNull(parseResult.ParsedDocument);
    }

    [Fact]
    public void TestDevnullCrucibleHjson()
    {
        string filePath = GetTestDataPath("devnull.crucible.hjson");
        string hjsonSourceText = File.ReadAllText(filePath);

        var structuralAnalyzer = new StructuralAnalyzer();
        var structureResult = structuralAnalyzer.Analyze(hjsonSourceText);

        Assert.Empty(structureResult.StructuralErrors);

        var hjsonContentParser = new HjsonContentParser();
        var parseResult = hjsonContentParser.Parse(hjsonSourceText, structureResult);

        Assert.Empty(parseResult.SemanticErrors);
        Assert.NotNull(parseResult.ParsedDocument);
    }

    [Fact]
    public void TestKeyAttributesHjson()
    {
        string filePath = GetTestDataPath("key_attributes.hjson");
        string hjsonSourceText = File.ReadAllText(filePath);

        var structuralAnalyzer = new StructuralAnalyzer();
        var structureResult = structuralAnalyzer.Analyze(hjsonSourceText);

        Assert.Empty(structureResult.StructuralErrors);

        // Parse WITH key attributes enabled
        var attributeParserOptions = new HjsonParserOptions { EmitKeyAttributes = true };
        var hjsonContentParser = new HjsonContentParser(attributeParserOptions);
        var parseResult = hjsonContentParser.Parse(hjsonSourceText, structureResult);

        Assert.Empty(parseResult.SemanticErrors);
        Assert.NotNull(parseResult.ParsedDocument);

        // Verify __keyAttributes node exists
        var rootElement = parseResult.ParsedDocument.RootElement;
        Assert.True(rootElement.TryGetProperty("__keyAttributes", out var keyAttributesElement));
        Assert.Equal(JsonValueKind.Object, keyAttributesElement.ValueKind);
    }

    [Fact]
    public void TestKeyAttributesWithQuotedValues()
    {
        string hjsonWithQuotedAttributes = """
        {
            name [default:"a string with spaces and newlines \n\n", optional]: string
            count [default:"42"]: int
        }
        """;

        var structureResult = new StructuralAnalyzer().Analyze(hjsonWithQuotedAttributes);
        var parser = new HjsonContentParser(new HjsonParserOptions { EmitKeyAttributes = true });
        var parseResult = parser.Parse(hjsonWithQuotedAttributes, structureResult);

        Assert.NotNull(parseResult.ParsedDocument);
        Assert.Empty(parseResult.SemanticErrors);

        var root = parseResult.ParsedDocument.RootElement;
        
        // Verify __keyAttributes exists
        Assert.True(root.TryGetProperty("__keyAttributes", out var keyAttrs));
        
        // Verify name attributes
        Assert.True(keyAttrs.TryGetProperty("name", out var nameAttrs));
        Assert.True(nameAttrs.TryGetProperty("default", out var defaultAttr));
        Assert.Equal("a string with spaces and newlines \n\n", defaultAttr.GetString());
        Assert.True(nameAttrs.TryGetProperty("optional", out var optionalAttr));
        Assert.True(optionalAttr.GetBoolean());
        
        // Verify count attributes
        Assert.True(keyAttrs.TryGetProperty("count", out var countAttrs));
        Assert.True(countAttrs.TryGetProperty("default", out var countDefault));
        Assert.Equal("42", countDefault.GetString());
    }
}