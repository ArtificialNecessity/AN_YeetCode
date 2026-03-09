#!/usr/bin/env dotnet run
// Schema Loader smoke test
// Run: dotnet run YeetCode.lib/YeetCode_Tests/TestSchemaLoader.cs

#:project ../YeetCode/YeetCode.csproj

using System;
using System.IO;
using YeetCode.Schema;

string scriptDirectory = Path.GetDirectoryName(GetScriptPath())!;
string testDataDirectory = Path.Combine(scriptDirectory, "TestData");

static string GetScriptPath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

Console.WriteLine("=== Schema Loader Smoke Test ===\n");

// Test: Load proto.schema.ytson
string protoSchemaPath = Path.Combine(testDataDirectory, "proto.schema.ytson");
Console.WriteLine($"Loading: {protoSchemaPath}");

var loadedSchema = SchemaLoader.LoadFromFile(protoSchemaPath);

Console.WriteLine($"\nType definitions: {loadedSchema.TypeDefinitions.Count}");
foreach (var (typeName, typeDefinition) in loadedSchema.TypeDefinitions)
{
    Console.WriteLine($"  @{typeName}:");
    foreach (var (fieldName, fieldDefinition) in typeDefinition.FieldDefinitions)
    {
        string optionalMarker = fieldDefinition.IsOptional ? "?" : "";
        string defaultMarker = fieldDefinition.DefaultValueText != null
            ? $" = {fieldDefinition.DefaultValueText}"
            : "";
        Console.WriteLine($"    {fieldName}: {fieldDefinition.FieldType}{optionalMarker}{defaultMarker}");
    }
}

Console.WriteLine($"\nRoot field definitions: {loadedSchema.RootFieldDefinitions.Count}");
foreach (var (fieldName, fieldDefinition) in loadedSchema.RootFieldDefinitions)
{
    string optionalMarker = fieldDefinition.IsOptional ? "?" : "";
    string defaultMarker = fieldDefinition.DefaultValueText != null
        ? $" = {fieldDefinition.DefaultValueText}"
        : "";
    Console.WriteLine($"  {fieldName}: {fieldDefinition.FieldType}{optionalMarker}{defaultMarker}");
}

// Verify expected structure
Console.WriteLine("\n--- Verification ---");

// Should have @Field type
if (loadedSchema.HasTypeDefinition("Field"))
{
    Console.WriteLine("✅ @Field type exists");
    var fieldType = loadedSchema.GetTypeDefinition("Field");
    if (fieldType.FieldDefinitions.ContainsKey("type"))
        Console.WriteLine("✅ @Field.type field exists");
    if (fieldType.FieldDefinitions.ContainsKey("tag"))
        Console.WriteLine("✅ @Field.tag field exists");
    if (fieldType.FieldDefinitions["label"].DefaultValueText == "optional")
        Console.WriteLine("✅ @Field.label has default 'optional'");
    if (fieldType.FieldDefinitions["deprecated"].DefaultValueText == "false")
        Console.WriteLine("✅ @Field.deprecated has default 'false'");
}
else
{
    Console.WriteLine("❌ @Field type NOT found");
}

// Should have root fields
if (loadedSchema.RootFieldDefinitions.ContainsKey("package"))
    Console.WriteLine("✅ Root field 'package' exists (optional: " +
        loadedSchema.RootFieldDefinitions["package"].IsOptional + ")");
if (loadedSchema.RootFieldDefinitions.ContainsKey("syntax"))
    Console.WriteLine("✅ Root field 'syntax' exists (default: " +
        loadedSchema.RootFieldDefinitions["syntax"].DefaultValueText + ")");

Console.WriteLine("\n✅ Schema loader test complete!");