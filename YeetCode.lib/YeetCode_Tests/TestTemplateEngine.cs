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

  [Fact]
  public void TestDoubleSlashCommentsArePreservedInOutput()
  {
    string dataHjsonText = """
        {
          name: Widget
        }
        """;
    var dataDocument = ParseHjson(dataHjsonText);

    string templateText = """
        <?yt delim="<% %>" ?>
        // This is a C# comment
        public class <% name %> {
            // Another C# comment
            public string Name { get; set; }
        }
        """;

    var templateParser = new TemplateParser();
    var templateAst = templateParser.Parse(templateText);
    var evaluator = new TemplateEvaluator(dataDocument);
    string output = evaluator.Evaluate(templateAst);

    // The // comments MUST be preserved in the output — they are C# code, not YeetCode comments
    Assert.Contains("// This is a C# comment", output);
    Assert.Contains("// Another C# comment", output);
  }

  [Fact]
  public void TestYeetCommentTagProducesNoOutput()
  {
    string dataHjsonText = """
        {
          name: Widget
        }
        """;
    var dataDocument = ParseHjson(dataHjsonText);

    string templateText = """
        <?yt delim="<% %>" ?>
        <% # This is a YeetCode comment — should not appear in output %>
        Hello <% name %>
        <% # Another comment %>
        Goodbye
        """;

    var templateParser = new TemplateParser();
    var templateAst = templateParser.Parse(templateText);
    var evaluator = new TemplateEvaluator(dataDocument);
    string output = evaluator.Evaluate(templateAst);

    // Comment tags should produce no output
    Assert.DoesNotContain("This is a YeetCode comment", output);
    Assert.DoesNotContain("Another comment", output);
    // But the actual content should be there
    Assert.Contains("Hello Widget", output);
    Assert.Contains("Goodbye", output);
  }

  [Fact]
  public void TestStandaloneDirectiveLinesAreTrimmmed()
  {
    string dataHjsonText = """
        {
          items: [
            { name: alpha }
            { name: beta }
          ]
        }
        """;
    var dataDocument = ParseHjson(dataHjsonText);

    // Each directive is alone on its line — those lines should be trimmed entirely
    string templateText = "<?yt delim=\"<% %>\" ?>\n" +
        "Header\n" +
        "<% each items as item %>\n" +
        "- <% item.name %>\n" +
        "<% /each %>\n" +
        "Footer\n";

    var templateParser = new TemplateParser();
    var templateAst = templateParser.Parse(templateText);
    var evaluator = new TemplateEvaluator(dataDocument);
    string output = evaluator.Evaluate(templateAst);

    // The each/end lines should NOT produce blank lines
    string expectedOutput = "Header\n- alpha\n- beta\nFooter\n";
    Assert.Equal(expectedOutput, output);
  }

  [Fact]
  public void TestInlineDirectivesAreNotTrimmed()
  {
    string dataHjsonText = """
        {
          name: Widget
          active: true
        }
        """;
    var dataDocument = ParseHjson(dataHjsonText);

    // Value expression on its own line should NOT be trimmed (it's not a control directive)
    string templateText = "<?yt delim=\"<% %>\" ?>\n" +
        "Name: <% name %>\n" +
        "Done\n";

    var templateParser = new TemplateParser();
    var templateAst = templateParser.Parse(templateText);
    var evaluator = new TemplateEvaluator(dataDocument);
    string output = evaluator.Evaluate(templateAst);

    // Value expressions inline should be preserved as-is
    Assert.Equal("Name: Widget\nDone\n", output);
  }

  [Fact]
  public void TestStandaloneCommentLinesAreTrimmed()
  {
    string dataHjsonText = """
        {
          name: Widget
        }
        """;
    var dataDocument = ParseHjson(dataHjsonText);

    // Comment on its own line should be trimmed entirely (no blank line)
    string templateText = "<?yt delim=\"<% %>\" ?>\n" +
        "Before\n" +
        "<% # This comment should vanish completely %>\n" +
        "After\n";

    var templateParser = new TemplateParser();
    var templateAst = templateParser.Parse(templateText);
    var evaluator = new TemplateEvaluator(dataDocument);
    string output = evaluator.Evaluate(templateAst);

    Assert.Equal("Before\nAfter\n", output);
  }
}