#!/usr/bin/env dotnet run
// Schema Validator smoke test
// Run: dotnet run YeetCode.lib/YeetCode_Tests/TestSchemaValidator.cs

#:project ../YeetCode/YeetCode.csproj

using System;
using System.IO;
using System.Text.Json;
using HJSONParserForAI.Core;
using YeetCode.Schema;

string scriptDirectory = Path.GetDirectoryName(GetScriptPath())!;
string testDataDirectory = Path.Combine(scriptDirectory, "TestData");

static string GetScriptPath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

Console.WriteLine("=== Schema Validator Smoke Test ===\n");

// Load schema
string schemaPath = Path.Combine(testDataDirectory, "proto.schema.ytson");
var loadedSchema = SchemaLoader.LoadFromFile(schemaPath);
Console.WriteLine($"Schema loaded: {loadedSchema.TypeDefinitions.Count} types, {loadedSchema.RootFieldDefinitions.Count} root fields\n");

// Test 1: Valid data with all fields
Console.WriteLine("--- Test 1: Valid data (all fields present) ---");
TestValidation(loadedSchema, Path.Combine(testDataDirectory, "proto_valid_data.hjson"));

// Test 2: Data with defaults applied (missing optional fields)
Console.WriteLine("--- Test 2: Minimal data (defaults should be applied) ---");
string minimalData = """
{
  syntax: proto3
  messages: {
    Empty: {
      fields: {}
    }
  }
}
""";
TestValidationFromString(loadedSchema, minimalData);

// Test 3: Data missing required field 'syntax' (has default, so should work)
Console.WriteLine("--- Test 3: Missing 'syntax' field (has default 'proto3') ---");
string noSyntaxData = """
{
  messages: {
    Test: {
      fields: {}
    }
  }
}
""";
TestValidationFromString(loadedSchema, noSyntaxData);

// Test 4: Invalid data — wrong type
Console.WriteLine("--- Test 4: Invalid data (wrong type for 'syntax') ---");
string wrongTypeData = """
{
  syntax: 42
  messages: {}
}
""";
TestValidationFromString(loadedSchema, wrongTypeData, expectErrors: true);

Console.WriteLine("\n✅ Schema validator test complete!");

void TestValidation(LoadedSchema schema, string dataFilePath)
{
    string hjsonText = File.ReadAllText(dataFilePath);
    TestValidationFromString(schema, hjsonText);
}

void TestValidationFromString(LoadedSchema schema, string hjsonText, bool expectErrors = false)
{
    // Parse HJSON to JsonDocument
    var structuralAnalyzer = new StructuralAnalyzer();
    var structureResult = structuralAnalyzer.Analyze(hjsonText);
    var hjsonContentParser = new HjsonContentParser();
    var parseResult = hjsonContentParser.Parse(hjsonText, structureResult);

    if (parseResult.ParsedDocument == null) {
        Console.WriteLine("  ❌ HJSON parse failed\n");
        return;
    }

    // Validate
    var validationErrors = SchemaValidator.Validate(parseResult.ParsedDocument, schema);

    if (validationErrors.Count > 0) {
        Console.WriteLine($"  Validation errors: {validationErrors.Count}");
        foreach (var validationError in validationErrors) {
            Console.WriteLine($"    • {validationError}");
        }
        if (!expectErrors) {
            Console.WriteLine("  ❌ UNEXPECTED errors\n");
        } else {
            Console.WriteLine("  ✅ Errors expected and found\n");
        }
        return;
    }

    if (expectErrors) {
        Console.WriteLine("  ❌ Expected errors but got none\n");
        return;
    }

    Console.WriteLine("  ✅ Validation passed (no errors)");

    // Also test ValidateAndApplyDefaults
    var validatedDocument = SchemaValidator.ValidateAndApplyDefaults(parseResult.ParsedDocument, schema);
    using var prettyPrintStream = new MemoryStream();
    using (var prettyPrintWriter = new Utf8JsonWriter(prettyPrintStream, new JsonWriterOptions { Indented = true })) {
        validatedDocument.WriteTo(prettyPrintWriter);
    }
    string prettyJson = System.Text.Encoding.UTF8.GetString(prettyPrintStream.ToArray());
    Console.WriteLine($"  Validated JSON ({prettyJson.Length} chars):");
    Console.WriteLine(prettyJson);
    Console.WriteLine();
}