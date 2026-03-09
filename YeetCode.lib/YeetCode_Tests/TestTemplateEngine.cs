using System.Text.Json;
using YeetJson;
using YeetCode.Template;
using Xunit;

namespace YeetCode_Tests;

/// <summary>
/// Tests for the template engine - lexer, parser, and evaluator.
/// </summary>
public class TestTemplateEngine
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
  public void TestSimpleTemplateWithEachAndPascal()
  {
    string dataHjsonText = File.ReadAllText(GetTestDataPath("proto_valid_data.hjson"));
    var dataDocument = ParseHjson(dataHjsonText);

    string templateText = File.ReadAllText(GetTestDataPath("simple.yt"));
    var templateParser = new TemplateParser();
    var templateAst = templateParser.Parse(templateText);

    var evaluator = new TemplateEvaluator(dataDocument);
    string output = evaluator.Evaluate(templateAst);

    Assert.NotEmpty(output);
    Assert.Contains("Person", output); // Should have PascalCase message name
  }

  [Fact]
  public void TestIfElseTemplate()
  {
    string dataHjsonText = File.ReadAllText(GetTestDataPath("proto_valid_data.hjson"));
    var dataDocument = ParseHjson(dataHjsonText);

    string templateText = """
        <?yt delim="<% %>" ?>
        <% each messages as msg_name, msg %>
        <% each msg.fields as fname, f %>
        <% if f.label == "required" %>
        [Required] <% pascal fname %>
        <% elif f.label == "optional" %>
        <% pascal fname %>?
        <% else %>
        <% pascal fname %>
        <% /if %>
        <% /each %>
        <% /each %>
        """;

    var templateParser = new TemplateParser();
    var templateAst = templateParser.Parse(templateText);
    var evaluator = new TemplateEvaluator(dataDocument);
    string output = evaluator.Evaluate(templateAst);

    Assert.NotEmpty(output);
    Assert.Contains("[Required]", output);
  }

  [Fact]
  public void TestSeparatorSupport()
  {
    string dataHjsonText = File.ReadAllText(GetTestDataPath("proto_valid_data.hjson"));
    var dataDocument = ParseHjson(dataHjsonText);

    string templateText = """
        <?yt delim="<% %>" ?>
        <% each messages as msg_name, msg %>
        Fields: <% each msg.fields as fname, f separator=", " %><% pascal fname %><% /each %>
        <% /each %>
        """;

    var templateParser = new TemplateParser();
    var templateAst = templateParser.Parse(templateText);
    var evaluator = new TemplateEvaluator(dataDocument);
    string output = evaluator.Evaluate(templateAst);

    Assert.NotEmpty(output);
    Assert.Contains("Fields:", output);
    Assert.Contains(",", output); // Should have separator
  }

  [Fact]
  public void TestLookupTable()
  {
    string dataHjsonText = File.ReadAllText(GetTestDataPath("proto_valid_data.hjson"));
    var dataDocument = ParseHjson(dataHjsonText);

    string functionsHjson = """
        {
          csharp_type: {
            string: string
            int32: int
            bool: bool
            float: float
          }
        }
        """;
    var functionsDocument = ParseHjson(functionsHjson);

    string templateText = """
        <?yt delim="<% %>" ?>
        <% each messages as msg_name, msg %>
        <% each msg.fields as fname, f %>
        public <% csharp_type[f.type] %> <% pascal fname %>;
        <% /each %>
        <% /each %>
        """;

    var templateParser = new TemplateParser();
    var templateAst = templateParser.Parse(templateText);
    var evaluator = new TemplateEvaluator(dataDocument, functionsDocument);
    string output = evaluator.Evaluate(templateAst);

    Assert.NotEmpty(output);
    Assert.Contains("public string", output);
    Assert.Contains("public int", output);
  }
}