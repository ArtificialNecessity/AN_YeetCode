namespace YeetCode.Schema;

using System.Text.Json;
using YeetJson;

/// <summary>
/// Loads a .ytson schema file and produces a LoadedSchema containing
/// type definitions and root field definitions.
///
/// Schema syntax (.ytson — HJSON with key attributes):
///   @TypeName: { field: type, ... }     — type definition
///   fieldName: type                      — root field
///   fieldName [optional]: type           — optional field (key attribute)
///   fieldName [default:value]: type      — field with default (key attribute)
///   [type]                               — array of type
///   {type}                               — map with value type
///   {}                                   — freeform object
///   @:                                   — anonymous map entry type (in maps)
/// </summary>
public static class SchemaLoader
{
    public static LoadedSchema LoadFromFile(string schemaFilePath)
    {
        string schemaHjsonText = File.ReadAllText(schemaFilePath);
        return LoadFromString(schemaHjsonText);
    }

    public static LoadedSchema LoadFromString(string schemaHjsonText)
    {
        // Parse HJSON to JsonDocument
        var structuralAnalyzer = new StructuralAnalyzer();
        var structureResult = structuralAnalyzer.Analyze(schemaHjsonText);

        if (structureResult.StructuralErrors.Count > 0)
        {
            var diagnosticFormatter = new DiagnosticFormatter();
            var diagnosticOutput = diagnosticFormatter.FormatForAI(
                new ParseResult(null, new(), structureResult.StructuralErrors),
                schemaHjsonText
            );
            throw new InvalidOperationException(
                $"Schema file has structural errors:\n{diagnosticOutput}"
            );
        }

        var hjsonContentParser = new HjsonContentParser(new HjsonParserOptions
        {
            EmitKeyAttributes = true
        });
        var parseResult = hjsonContentParser.Parse(schemaHjsonText, structureResult);

        if (parseResult.ParsedDocument == null)
        {
            throw new InvalidOperationException(
                "Schema file parsed to null — check for structural errors in the HJSON"
            );
        }

        return LoadFromJsonDocument(parseResult.ParsedDocument);
    }

    public static LoadedSchema LoadFromJsonDocument(JsonDocument schemaDocument)
    {
        var typeDefinitions = new Dictionary<string, SchemaTypeDefinition>();
        var rootFieldDefinitions = new Dictionary<string, SchemaFieldDefinition>();

        var rootElement = schemaDocument.RootElement;
        if (rootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                "Schema root must be an HJSON object, got: " + rootElement.ValueKind
            );
        }

        // Extract __keyAttributes if present (emitted by HJSON parser for [optional] etc.)
        var rootKeyAttributes = ExtractKeyAttributes(rootElement);

        // First pass: collect all @Type definitions and root fields
        foreach (var schemaProperty in rootElement.EnumerateObject())
        {
            string propertyName = schemaProperty.Name;

            // Skip the __keyAttributes node itself
            if (propertyName == "__keyAttributes") continue;

            if (propertyName.StartsWith('@'))
            {
                // This is a @Type definition
                string typeName = propertyName[1..]; // strip @
                var typeFieldDefinitions = ParseObjectFieldDefinitions(schemaProperty.Value);
                typeDefinitions[typeName] = new SchemaTypeDefinition
                {
                    TypeName = typeName,
                    FieldDefinitions = typeFieldDefinitions
                };
            }
            else
            {
                var fieldDefinition = ParseFieldDefinitionFromValue(schemaProperty.Value, propertyName);

                // Check key attributes for [optional] and [default:value]
                if (rootKeyAttributes.TryGetValue(propertyName, out var fieldAttributes))
                {
                    if (fieldAttributes.ContainsKey("optional"))
                    {
                        fieldDefinition.IsOptional = true;
                    }
                    if (fieldAttributes.TryGetValue("default", out var defaultValue))
                    {
                        fieldDefinition.DefaultValueText = defaultValue;
                    }
                }

                rootFieldDefinitions[propertyName] = fieldDefinition;
            }
        }

        return new LoadedSchema
        {
            TypeDefinitions = typeDefinitions,
            RootFieldDefinitions = rootFieldDefinitions
        };
    }

    /// <summary>
    /// Parse the fields of an object type definition.
    /// Each property in the JSON object is a field name → type descriptor.
    /// </summary>
    private static Dictionary<string, SchemaFieldDefinition> ParseObjectFieldDefinitions(
        JsonElement objectElement)
    {
        var fieldDefinitions = new Dictionary<string, SchemaFieldDefinition>();

        if (objectElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"Expected object for type definition, got: {objectElement.ValueKind}"
            );
        }

        // Extract __keyAttributes if present
        var objectKeyAttributes = ExtractKeyAttributes(objectElement);

        foreach (var fieldProperty in objectElement.EnumerateObject())
        {
            string fieldName = fieldProperty.Name;

            // Skip @: entries (anonymous map entry type marker) and __keyAttributes
            if (fieldName == "@" || fieldName == "__keyAttributes") continue;

            var fieldDefinition = ParseFieldDefinitionFromValue(fieldProperty.Value, fieldName);

            // Check key attributes for [optional] and [default:value]
            if (objectKeyAttributes.TryGetValue(fieldName, out var fieldAttributes))
            {
                if (fieldAttributes.ContainsKey("optional"))
                {
                    fieldDefinition.IsOptional = true;
                }
                if (fieldAttributes.TryGetValue("default", out var defaultValue))
                {
                    fieldDefinition.DefaultValueText = defaultValue;
                }
            }

            fieldDefinitions[fieldName] = fieldDefinition;
        }

        return fieldDefinitions;
    }

    /// <summary>
    /// Extract __keyAttributes from a JSON object element.
    /// Returns a dictionary of keyName → { attrName → attrValue }.
    /// Returns empty dictionary if no __keyAttributes node exists.
    /// </summary>
    private static Dictionary<string, Dictionary<string, string>> ExtractKeyAttributes(
        JsonElement objectElement)
    {
        var extractedAttributes = new Dictionary<string, Dictionary<string, string>>();

        if (objectElement.ValueKind != JsonValueKind.Object) return extractedAttributes;

        if (!objectElement.TryGetProperty("__keyAttributes", out var keyAttributesElement))
        {
            return extractedAttributes;
        }

        if (keyAttributesElement.ValueKind != JsonValueKind.Object) return extractedAttributes;

        foreach (var attributedKeyProperty in keyAttributesElement.EnumerateObject())
        {
            string attributedKeyName = attributedKeyProperty.Name;
            var perKeyAttributes = new Dictionary<string, string>();

            if (attributedKeyProperty.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var attrProperty in attributedKeyProperty.Value.EnumerateObject())
                {
                    string attrValue = attrProperty.Value.ValueKind switch
                    {
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        JsonValueKind.String => attrProperty.Value.GetString()!,
                        _ => attrProperty.Value.ToString()
                    };
                    perKeyAttributes[attrProperty.Name] = attrValue;
                }
            }

            extractedAttributes[attributedKeyName] = perKeyAttributes;
        }

        return extractedAttributes;
    }

    /// <summary>
    /// Parse a field definition from a JSON value.
    /// The value can be:
    ///   - A string like "string", "int", "string?", "string = default", "@TypeName", "@TypeName?"
    ///   - An array like ["string"], ["@TypeName"], [{ ... }]
    ///   - An object like { field: type } (inline object or map)
    /// </summary>
    private static SchemaFieldDefinition ParseFieldDefinitionFromValue(
        JsonElement fieldValueElement, string fieldNameForErrors)
    {
        switch (fieldValueElement.ValueKind)
        {
            case JsonValueKind.String:
                return ParseFieldDefinitionFromTypeString(fieldValueElement.GetString()!);

            case JsonValueKind.Array:
                return ParseArrayFieldDefinition(fieldValueElement);

            case JsonValueKind.Object:
                return ParseObjectOrMapFieldDefinition(fieldValueElement, fieldNameForErrors);

            default:
                throw new InvalidOperationException(
                    $"Unexpected value kind '{fieldValueElement.ValueKind}' for field '{fieldNameForErrors}' in schema. " +
                    "Expected a type string (e.g. 'string'), array ([type]), or object ({...})"
                );
        }
    }

    /// <summary>
    /// Parse a type string like "string", "int", "@TypeName"
    /// Optionality and defaults are handled via key attributes: [optional], [default:value]
    /// </summary>
    private static SchemaFieldDefinition ParseFieldDefinitionFromTypeString(string typeDescriptorText)
    {
        string typeNameText = typeDescriptorText.Trim();
        var fieldType = ParseTypeReference(typeNameText);

        return new SchemaFieldDefinition
        {
            FieldType = fieldType,
            IsOptional = false
        };
    }

    /// <summary>
    /// Parse a type reference string: "string", "int", "float", "bool", "@TypeName"
    /// </summary>
    private static SchemaFieldType ParseTypeReference(string typeNameText)
    {
        // Check for @TypeRef
        if (typeNameText.StartsWith('@'))
        {
            return new SchemaFieldType
            {
                FieldTypeKind = SchemaFieldTypeKind.TypeRef,
                ReferencedTypeName = typeNameText[1..]
            };
        }

        // Check for primitives
        return typeNameText.ToLowerInvariant() switch
        {
            "string" => new SchemaFieldType { FieldTypeKind = SchemaFieldTypeKind.Primitive, PrimitiveType = SchemaPrimitiveType.String },
            "int" => new SchemaFieldType { FieldTypeKind = SchemaFieldTypeKind.Primitive, PrimitiveType = SchemaPrimitiveType.Int },
            "float" => new SchemaFieldType { FieldTypeKind = SchemaFieldTypeKind.Primitive, PrimitiveType = SchemaPrimitiveType.Float },
            "bool" => new SchemaFieldType { FieldTypeKind = SchemaFieldTypeKind.Primitive, PrimitiveType = SchemaPrimitiveType.Bool },
            _ => throw new InvalidOperationException(
                $"Unknown type '{typeNameText}' in schema. " +
                "Expected: string, int, float, bool, or @TypeName"
            )
        };
    }

    /// <summary>
    /// Parse an array field definition: [type], [@TypeName], [{ ... }]
    /// The JSON array should have exactly one element describing the element type.
    /// </summary>
    private static SchemaFieldDefinition ParseArrayFieldDefinition(JsonElement arrayElement)
    {
        if (arrayElement.GetArrayLength() == 0)
        {
            throw new InvalidOperationException(
                "Schema array type must have exactly one element describing the element type, got empty array"
            );
        }

        var firstArrayElement = arrayElement[0];
        SchemaFieldType elementType;

        if (firstArrayElement.ValueKind == JsonValueKind.String)
        {
            string elementTypeText = firstArrayElement.GetString()!;
            elementType = ParseTypeReference(elementTypeText);
        }
        else if (firstArrayElement.ValueKind == JsonValueKind.Object)
        {
            // Inline object type: [{ field: type, ... }]
            var inlineFieldDefinitions = ParseObjectFieldDefinitions(firstArrayElement);
            elementType = new SchemaFieldType
            {
                FieldTypeKind = SchemaFieldTypeKind.InlineObject,
                InlineObjectFields = inlineFieldDefinitions
            };
        }
        else
        {
            throw new InvalidOperationException(
                $"Schema array element type must be a string or object, got: {firstArrayElement.ValueKind}"
            );
        }

        return new SchemaFieldDefinition
        {
            FieldType = new SchemaFieldType
            {
                FieldTypeKind = SchemaFieldTypeKind.Array,
                ArrayElementType = elementType
            },
            IsOptional = false
        };
    }

    /// <summary>
    /// Parse an object field definition. This could be:
    ///   - A freeform object: {} (empty object)
    ///   - A map: { @: { ... } } or { typeName } — object with @: entry or single type value
    ///   - An inline object: { field: type, field: type }
    ///
    /// Heuristic: if the object has an @: key, it's a map with that value type.
    /// If the object is empty, it's a freeform object.
    /// Otherwise, it's an inline object definition.
    /// </summary>
    private static SchemaFieldDefinition ParseObjectOrMapFieldDefinition(
        JsonElement objectElement, string fieldNameForErrors)
    {
        int propertyCount = 0;
        bool hasAnonymousMapEntry = false;
        JsonElement anonymousMapEntryValue = default;

        foreach (var objectProperty in objectElement.EnumerateObject())
        {
            propertyCount++;
            if (objectProperty.Name == "@")
            {
                hasAnonymousMapEntry = true;
                anonymousMapEntryValue = objectProperty.Value;
            }
        }

        // Empty object → freeform
        if (propertyCount == 0)
        {
            return new SchemaFieldDefinition
            {
                FieldType = new SchemaFieldType { FieldTypeKind = SchemaFieldTypeKind.FreeformObject },
                IsOptional = false
            };
        }

        // Has @: entry → it's a map
        if (hasAnonymousMapEntry)
        {
            SchemaFieldType mapValueType;

            if (anonymousMapEntryValue.ValueKind == JsonValueKind.Object)
            {
                // Map with inline object value type: { @: { field: type } }
                var mapValueFieldDefinitions = ParseObjectFieldDefinitions(anonymousMapEntryValue);
                mapValueType = new SchemaFieldType
                {
                    FieldTypeKind = SchemaFieldTypeKind.InlineObject,
                    InlineObjectFields = mapValueFieldDefinitions
                };
            }
            else if (anonymousMapEntryValue.ValueKind == JsonValueKind.String)
            {
                // Map with simple value type: { @: "string" }
                mapValueType = ParseTypeReference(anonymousMapEntryValue.GetString()!);
            }
            else
            {
                throw new InvalidOperationException(
                    $"Map value type for '{fieldNameForErrors}' must be a string or object, " +
                    $"got: {anonymousMapEntryValue.ValueKind}"
                );
            }

            return new SchemaFieldDefinition
            {
                FieldType = new SchemaFieldType
                {
                    FieldTypeKind = SchemaFieldTypeKind.Map,
                    MapValueType = mapValueType
                },
                IsOptional = false
            };
        }

        // Otherwise it's an inline object definition
        var inlineFieldDefinitions = ParseObjectFieldDefinitions(objectElement);
        return new SchemaFieldDefinition
        {
            FieldType = new SchemaFieldType
            {
                FieldTypeKind = SchemaFieldTypeKind.InlineObject,
                InlineObjectFields = inlineFieldDefinitions
            },
            IsOptional = false
        };
    }
}