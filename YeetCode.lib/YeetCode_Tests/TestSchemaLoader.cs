using YeetCode.Schema;
using Xunit;

namespace YeetCode_Tests;

/// <summary>
/// Tests for SchemaLoader - loading and parsing .ytson schema files.
/// </summary>
public class TestSchemaLoader
{
    private static string GetTestDataPath(string fileName)
    {
        return Path.Combine("TestData", fileName);
    }

    [Fact]
    public void TestLoadProtoSchema()
    {
        string schemaPath = GetTestDataPath("proto.schema.ytson");
        var loadedSchema = SchemaLoader.LoadFromFile(schemaPath);

        // Verify @Field type exists
        Assert.True(loadedSchema.HasTypeDefinition("Field"));
        var fieldTypeDef = loadedSchema.GetTypeDefinition("Field");

        // Verify @Field has expected fields
        Assert.True(fieldTypeDef.FieldDefinitions.ContainsKey("type"));
        Assert.True(fieldTypeDef.FieldDefinitions.ContainsKey("tag"));
        Assert.True(fieldTypeDef.FieldDefinitions.ContainsKey("label"));
        Assert.True(fieldTypeDef.FieldDefinitions.ContainsKey("deprecated"));

        // Verify defaults from key attributes
        Assert.Equal("optional", fieldTypeDef.FieldDefinitions["label"].DefaultValueText);
        Assert.Equal("false", fieldTypeDef.FieldDefinitions["deprecated"].DefaultValueText);

        // Verify root fields
        Assert.True(loadedSchema.RootFieldDefinitions.ContainsKey("package"));
        Assert.True(loadedSchema.RootFieldDefinitions.ContainsKey("syntax"));
        Assert.True(loadedSchema.RootFieldDefinitions.ContainsKey("messages"));
        Assert.True(loadedSchema.RootFieldDefinitions.ContainsKey("enums"));

        // Verify optionality
        Assert.True(loadedSchema.RootFieldDefinitions["package"].IsOptional);
        Assert.False(loadedSchema.RootFieldDefinitions["syntax"].IsOptional);
        Assert.True(loadedSchema.RootFieldDefinitions["messages"].IsOptional);
        Assert.True(loadedSchema.RootFieldDefinitions["enums"].IsOptional);

        // Verify syntax has default
        Assert.Equal("proto3", loadedSchema.RootFieldDefinitions["syntax"].DefaultValueText);
    }

    [Fact]
    public void TestFieldTypeToString()
    {
        string schemaPath = GetTestDataPath("proto.schema.ytson");
        var loadedSchema = SchemaLoader.LoadFromFile(schemaPath);

        var fieldTypeDef = loadedSchema.GetTypeDefinition("Field");

        // Verify type field is string
        Assert.Equal("string", fieldTypeDef.FieldDefinitions["type"].FieldType.ToString());

        // Verify tag field is int
        Assert.Equal("int", fieldTypeDef.FieldDefinitions["tag"].FieldType.ToString());
    }
}