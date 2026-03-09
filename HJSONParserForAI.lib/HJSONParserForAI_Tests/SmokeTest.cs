#!/usr/bin/env dotnet run
// Quick smoke test for the HJSON content parser
// Run: dotnet run --project HJSONParserForAI.lib/HJSONParserForAI_Tests/SmokeTest.cs

#:project ../HJSONParserForAI/HJSONParserForAI.csproj

using System;
using System.IO;
using System.Text.Json;
using HJSONParserForAI.Core;

string scriptDirectory = Path.GetDirectoryName(GetScriptPath())!;
string testDataDirectory = Path.Combine(scriptDirectory, "TestData");

static string GetScriptPath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

Console.WriteLine("=== HJSON Content Parser Smoke Test ===\n");

// Test 1: valid_simple.hjson
TestFile("valid_simple.hjson");

// Test 2: devnull.crucible.hjson
TestFile("devnull.crucible.hjson");

// Test 3: key_attributes.hjson — with EmitKeyAttributes enabled
TestFileWithKeyAttributes("key_attributes.hjson");

void TestFile(string fileName)
{
    string filePath = Path.Combine(testDataDirectory, fileName);
    Console.WriteLine($"--- Testing: {fileName} ---");

    string hjsonSourceText = File.ReadAllText(filePath);

    // Phase 1: Structural analysis
    var structuralAnalyzer = new StructuralAnalyzer();
    var structureResult = structuralAnalyzer.Analyze(hjsonSourceText);

    Console.WriteLine($"  Structural errors: {structureResult.StructuralErrors.Count}");
    Console.WriteLine($"  Regions: {structureResult.Regions.Count}");

    // Phase 2: Content parsing
    var hjsonContentParser = new HjsonContentParser();
    var parseResult = hjsonContentParser.Parse(hjsonSourceText, structureResult);

    Console.WriteLine($"  Semantic errors: {parseResult.SemanticErrors.Count}");
    Console.WriteLine($"  ParsedDocument: {(parseResult.ParsedDocument != null ? "YES" : "NULL")}");

    if (parseResult.ParsedDocument != null)
    {
        // Pretty-print the JSON output using Utf8JsonWriter (no reflection needed)
        using var prettyPrintStream = new MemoryStream();
        using (var prettyPrintWriter = new Utf8JsonWriter(prettyPrintStream, new JsonWriterOptions { Indented = true }))
        {
            parseResult.ParsedDocument.WriteTo(prettyPrintWriter);
        }
        string prettyJson = System.Text.Encoding.UTF8.GetString(prettyPrintStream.ToArray());
        Console.WriteLine($"  JSON output ({prettyJson.Length} chars):");
        Console.WriteLine(prettyJson);
    }

    foreach (var semanticError in parseResult.SemanticErrors)
    {
        Console.WriteLine($"  ERROR: [{semanticError.Kind}] {semanticError.Message} at line {semanticError.Line}");
    }

    Console.WriteLine();
}

void TestFileWithKeyAttributes(string fileName)
{
    string filePath = Path.Combine(testDataDirectory, fileName);
    Console.WriteLine($"--- Testing (with key attributes): {fileName} ---");

    string hjsonSourceText = File.ReadAllText(filePath);

    // Phase 1: Structural analysis
    var structuralAnalyzer = new StructuralAnalyzer();
    var structureResult = structuralAnalyzer.Analyze(hjsonSourceText);

    Console.WriteLine($"  Structural errors: {structureResult.StructuralErrors.Count}");

    // Phase 2: Content parsing WITH key attributes enabled
    var attributeParserOptions = new HjsonParserOptions { EmitKeyAttributes = true };
    var hjsonContentParser = new HjsonContentParser(attributeParserOptions);
    var parseResult = hjsonContentParser.Parse(hjsonSourceText, structureResult);

    Console.WriteLine($"  Semantic errors: {parseResult.SemanticErrors.Count}");
    Console.WriteLine($"  ParsedDocument: {(parseResult.ParsedDocument != null ? "YES" : "NULL")}");

    if (parseResult.ParsedDocument != null) {
        using var prettyPrintStream = new MemoryStream();
        using (var prettyPrintWriter = new Utf8JsonWriter(prettyPrintStream, new JsonWriterOptions { Indented = true })) {
            parseResult.ParsedDocument.WriteTo(prettyPrintWriter);
        }
        string prettyJson = System.Text.Encoding.UTF8.GetString(prettyPrintStream.ToArray());
        Console.WriteLine($"  JSON output ({prettyJson.Length} chars):");
        Console.WriteLine(prettyJson);
    }

    foreach (var semanticError in parseResult.SemanticErrors) {
        Console.WriteLine($"  ERROR: [{semanticError.Kind}] {semanticError.Message} at line {semanticError.Line}");
    }

    Console.WriteLine();
}