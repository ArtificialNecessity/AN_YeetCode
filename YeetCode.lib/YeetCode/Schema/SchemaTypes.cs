namespace YeetCode.Schema;

/// <summary>
/// Represents a primitive type in the schema: string, int, float, bool
/// </summary>
public enum SchemaPrimitiveType
{
    String,
    Int,
    Float,
    Bool
}

/// <summary>
/// Represents the kind of a schema field type.
/// A field can be a primitive, a type reference, an array, a map, or a freeform object.
/// </summary>
public enum SchemaFieldTypeKind
{
    Primitive,      // string, int, float, bool
    TypeRef,        // @TypeName
    Array,          // [elementType]
    Map,            // {valueType}
    FreeformObject, // {} — arbitrary key-value pairs
    InlineObject    // { field: type, ... } — anonymous inline object definition
}

/// <summary>
/// Describes the type of a schema field. Recursive — arrays and maps contain element types.
/// </summary>
public class SchemaFieldType
{
    public SchemaFieldTypeKind FieldTypeKind { get; init; }

    /// <summary>For Primitive: which primitive type</summary>
    public SchemaPrimitiveType? PrimitiveType { get; init; }

    /// <summary>For TypeRef: the referenced @TypeName (without @)</summary>
    public string? ReferencedTypeName { get; init; }

    /// <summary>For Array: the element type</summary>
    public SchemaFieldType? ArrayElementType { get; init; }

    /// <summary>For Map: the value type (keys are always strings)</summary>
    public SchemaFieldType? MapValueType { get; init; }

    /// <summary>For InlineObject: the fields of the anonymous object</summary>
    public Dictionary<string, SchemaFieldDefinition>? InlineObjectFields { get; init; }

    public override string ToString()
    {
        return FieldTypeKind switch
        {
            SchemaFieldTypeKind.Primitive => PrimitiveType?.ToString().ToLowerInvariant() ?? "?",
            SchemaFieldTypeKind.TypeRef => $"@{ReferencedTypeName}",
            SchemaFieldTypeKind.Array => $"[{ArrayElementType}]",
            SchemaFieldTypeKind.Map => $"{{{MapValueType}}}",
            SchemaFieldTypeKind.FreeformObject => "{}",
            SchemaFieldTypeKind.InlineObject => "{...}",
            _ => "?"
        };
    }
}

/// <summary>
/// A single field definition within a schema type or document root.
/// </summary>
public class SchemaFieldDefinition
{
    /// <summary>The type of this field</summary>
    public required SchemaFieldType FieldType { get; init; }

    /// <summary>Whether this field is optional (marked with ? in schema)</summary>
    public bool IsOptional { get; set; }

    /// <summary>Default value as a string (from [default:value] key attribute). Null if no default.</summary>
    public string? DefaultValueText { get; set; }
}

/// <summary>
/// A named @Type definition in the schema. Contains a set of named fields.
/// </summary>
public class SchemaTypeDefinition
{
    /// <summary>The type name (without @), e.g. "MessageField", "Expression"</summary>
    public required string TypeName { get; init; }

    /// <summary>The fields defined in this type, keyed by field name</summary>
    public required Dictionary<string, SchemaFieldDefinition> FieldDefinitions { get; init; }
}

/// <summary>
/// A fully loaded schema — contains type definitions and document root fields.
/// This is the output of SchemaLoader.Load().
/// </summary>
public class LoadedSchema
{
    /// <summary>Named @Type definitions, keyed by type name (without @)</summary>
    public required Dictionary<string, SchemaTypeDefinition> TypeDefinitions { get; init; }

    /// <summary>Document root fields (the non-@Type entries in the schema)</summary>
    public required Dictionary<string, SchemaFieldDefinition> RootFieldDefinitions { get; init; }

    /// <summary>Look up a type definition by name. Throws if not found.</summary>
    public SchemaTypeDefinition GetTypeDefinition(string typeName)
    {
        if (!TypeDefinitions.TryGetValue(typeName, out var typeDefinition))
        {
            throw new InvalidOperationException(
                $"Schema type '@{typeName}' not found. " +
                $"Available types: {string.Join(", ", TypeDefinitions.Keys.Select(k => $"@{k}"))}"
            );
        }
        return typeDefinition;
    }

    /// <summary>Check if a type name exists in the schema</summary>
    public bool HasTypeDefinition(string typeName)
    {
        return TypeDefinitions.ContainsKey(typeName);
    }
}