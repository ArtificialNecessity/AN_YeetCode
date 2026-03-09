using System.Text.Json;
using YeetJson;
using YeetCode.Schema;
using Xunit;

namespace YeetCode_Tests;

/// <summary>
/// Tests for SchemaValidator - validating data against schemas and applying defaults.
/// </summary>
public class TestSchemaValidator
{
  private static string GetTestDataPath(string fileName)
  {
    return Path.Combine("TestData", fileName);
  }

  private static JsonDocument ParseHjson(string hjsonText)
  {
    var structuralAnalyzer = new StructuralAnalyzer();
    var structureResult = structuralAnalyzer.Analyze(hjsonText);
    var hjsonContentParser = new HjsonContentParser();
    var parseResult = hjsonContentParser.Parse(hjsonText, structureResult);
    return parseResult.ParsedDocument!;
  }

  [Fact]
  public void TestValidDataWithAllFields()
  {
    var loadedSchema = SchemaLoader.LoadFromFile(GetTestDataPath("proto.schema.ytson"));
    string dataHjsonText = File.ReadAllText(GetTestDataPath("proto_valid_data.hjson"));
    var dataDocument = ParseHjson(dataHjsonText);

    var validationErrors = SchemaValidator.Validate(dataDocument, loadedSchema);
    Assert.Empty(validationErrors);

    // Should also work with ValidateAndApplyDefaults
    var validatedDocument = SchemaValidator.ValidateAndApplyDefaults(dataDocument, loadedSchema);
    Assert.NotNull(validatedDocument);
  }

  [Fact]
  public void TestMinimalDataWithDefaults()
  {
    var loadedSchema = SchemaLoader.LoadFromFile(GetTestDataPath("proto.schema.ytson"));

    string minimalHjson = """
        {
          messages: {
            Empty: {
              fields: {}
            }
          }
        }
        """;

    var dataDocument = ParseHjson(minimalHjson);
    var validationErrors = SchemaValidator.Validate(dataDocument, loadedSchema);
    Assert.Empty(validationErrors);

    // Validate and apply defaults
    var validatedDocument = SchemaValidator.ValidateAndApplyDefaults(dataDocument, loadedSchema);

    // Should have 'syntax' field filled with default 'proto3'
    Assert.True(validatedDocument.RootElement.TryGetProperty("syntax", out var syntaxElement));
    Assert.Equal("proto3", syntaxElement.GetString());
  }

  [Fact]
  public void TestMissingSyntaxFieldGetsDefault()
  {
    var loadedSchema = SchemaLoader.LoadFromFile(GetTestDataPath("proto.schema.ytson"));

    string noSyntaxHjson = """
        {
          messages: {
            Test: {
              fields: {}
            }
          }
        }
        """;

    var dataDocument = ParseHjson(noSyntaxHjson);
    var validatedDocument = SchemaValidator.ValidateAndApplyDefaults(dataDocument, loadedSchema);

    // Should have 'syntax' filled with default
    Assert.True(validatedDocument.RootElement.TryGetProperty("syntax", out var syntaxElement));
    Assert.Equal("proto3", syntaxElement.GetString());
  }

  [Fact]
  public void TestWrongTypeThrowsValidationError()
  {
    var loadedSchema = SchemaLoader.LoadFromFile(GetTestDataPath("proto.schema.ytson"));

    string wrongTypeHjson = """
        {
          syntax: 42
          messages: {}
        }
        """;

    var dataDocument = ParseHjson(wrongTypeHjson);
    var validationErrors = SchemaValidator.Validate(dataDocument, loadedSchema);

    // Should have validation error about wrong type
    Assert.NotEmpty(validationErrors);
    Assert.Contains(validationErrors, err => err.Contains("syntax") && err.Contains("string"));
  }
}