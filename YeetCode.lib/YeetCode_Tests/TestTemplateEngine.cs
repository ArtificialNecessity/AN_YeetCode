#!/usr/bin/env dotnet run
// Template Engine smoke test
// Run: dotnet run YeetCode.lib/YeetCode_Tests/TestTemplateEngine.cs

#:project ../YeetCode/YeetCode.csproj

using System;
using System.IO;
using System.Text.Json;
using HJSONParserForAI.Core;
using YeetCode.Template;

string scriptDirectory = Path.GetDirectoryName(GetScriptPath())!;
string testDataDirectory = Path.Combine(scriptDirectory, "TestData");

static string GetScriptPath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

Console.WriteLine("=== Template Engine Smoke Test ===\n");

// Parse the proto data
string dataHjsonText = File.ReadAllText(Path.Combine(testDataDirectory, "proto_valid_data.hjson"));
var structuralAnalyzer = new StructuralAnalyzer();
var structureResult = structuralAnalyzer.Analyze(dataHjsonText);
var hjsonContentParser = new HjsonContentParser();
var parseResult = hjsonContentParser.Parse(dataHjsonText, structureResult);
var dataDocument = parseResult.ParsedDocument!;

Console.WriteLine("Data loaded successfully\n");

// Test 1: Simple template
Console.WriteLine("--- Test 1: Simple template (each + pascal) ---");
string simpleTemplate = File.ReadAllText(Path.Combine(testDataDirectory, "simple.yt"));
var templateParser = new TemplateParser();
var templateAst = templateParser.Parse(simpleTemplate);
Console.WriteLine($"  AST nodes: {templateAst.Count}");

var evaluator = new TemplateEvaluator(dataDocument);
string simpleOutput = evaluator.Evaluate(templateAst);
Console.WriteLine($"  Output ({simpleOutput.Length} chars):");
Console.WriteLine(simpleOutput);

// Test 2: Inline template with if/else
Console.WriteLine("--- Test 2: If/else template ---");
string ifElseTemplate = """
<?yt delim="<% %>" ?>
<% each messages as msg_name, msg %>
<% each msg.fields as fname, f %>
<% if f.label == "required" %>
[Required] <% pascal fname %>: <% f.type %>
<% elif f.label == "optional" %>
<% pascal fname %>?: <% f.type %>
<% else %>
<% pascal fname %>: <% f.type %>
<% /if %>
<% /each %>
<% /each %>
""";
var ifElseAst = templateParser.Parse(ifElseTemplate);
var ifElseEvaluator = new TemplateEvaluator(dataDocument);
string ifElseOutput = ifElseEvaluator.Evaluate(ifElseAst);
Console.WriteLine($"  Output:");
Console.WriteLine(ifElseOutput);

// Test 3: Separator support
Console.WriteLine("--- Test 3: Separator ---");
string separatorTemplate = """
<?yt delim="<% %>" ?>
<% each messages as msg_name, msg %>
Fields: <% each msg.fields as fname, f separator=", " %><% pascal fname %><% /each %>
<% /each %>
""";
var separatorAst = templateParser.Parse(separatorTemplate);
var separatorEvaluator = new TemplateEvaluator(dataDocument);
string separatorOutput = separatorEvaluator.Evaluate(separatorAst);
Console.WriteLine($"  Output:");
Console.WriteLine(separatorOutput);

// Test 4: Lookup table (functions)
Console.WriteLine("--- Test 4: Lookup table ---");
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
var funcStructure = structuralAnalyzer.Analyze(functionsHjson);
var funcParse = hjsonContentParser.Parse(functionsHjson, funcStructure);
var functionsDocument = funcParse.ParsedDocument!;

string lookupTemplate = """
<?yt delim="<% %>" ?>
<% each messages as msg_name, msg %>
public class <% pascal msg_name %> {
    <% each msg.fields as fname, f %>
    public <% csharp_type[f.type] %> <% pascal fname %> { get; set; }
    <% /each %>
}
<% /each %>
""";
var lookupAst = templateParser.Parse(lookupTemplate);
var lookupEvaluator = new TemplateEvaluator(dataDocument, functionsDocument);
string lookupOutput = lookupEvaluator.Evaluate(lookupAst);
Console.WriteLine($"  Output:");
Console.WriteLine(lookupOutput);

Console.WriteLine("✅ Template engine test complete!");