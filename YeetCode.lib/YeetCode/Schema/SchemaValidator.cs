namespace YeetCode.Schema;

using System.Buffers;
using System.Text.Json;

/// <summary>
/// Validates a JsonDocument against a LoadedSchema.
/// Checks required fields, type correctness, and fills in defaults.
/// Returns a new JsonDocument with defaults applied.
/// </summary>
public static class SchemaValidator
{
    /// <summary>
    /// Validate data against a schema and return a new JsonDocument with defaults filled in.
    /// Throws on validation errors with descriptive messages.
    /// </summary>
    public static JsonDocument ValidateAndApplyDefaults(
        JsonDocument dataDocument, LoadedSchema schema)
    {
        var validationErrors = new List<string>();

        var outputBuffer = new ArrayBufferWriter<byte>();
        using var jsonWriter = new Utf8JsonWriter(outputBuffer, new JsonWriterOptions
        {
            Indented = false,
            SkipValidation = false
        });

        ValidateObject(
            dataDocument.RootElement,
            schema.RootFieldDefinitions,
            schema,
            "$",
            validationErrors,
            jsonWriter
        );

        jsonWriter.Flush();

        if (validationErrors.Count > 0) {
            throw new SchemaValidationException(validationErrors);
        }

        var validatedBytes = outputBuffer.WrittenSpan.ToArray();
        return JsonDocument.Parse(validatedBytes);
    }

    /// <summary>
    /// Validate data against a schema without applying defaults.
    /// Returns a list of validation error messages (empty = valid).
    /// </summary>
    public static List<string> Validate(JsonDocument dataDocument, LoadedSchema schema)
    {
        var validationErrors = new List<string>();

        // Use a null writer — we only care about errors, not output
        var outputBuffer = new ArrayBufferWriter<byte>();
        using var jsonWriter = new Utf8JsonWriter(outputBuffer);

        ValidateObject(
            dataDocument.RootElement,
            schema.RootFieldDefinitions,
            schema,
            "$",
            validationErrors,
            jsonWriter
        );

        jsonWriter.Flush();
        return validationErrors;
    }

    // ── Object validation ────────────────────────────────────

    private static void ValidateObject(
        JsonElement dataElement,
        Dictionary<string, SchemaFieldDefinition> expectedFieldDefinitions,
        LoadedSchema schema,
        string currentPath,
        List<string> validationErrors,
        Utf8JsonWriter jsonWriter)
    {
        if (dataElement.ValueKind != JsonValueKind.Object) {
            validationErrors.Add(
                $"{currentPath}: Expected object, got {dataElement.ValueKind}"
            );
            dataElement.WriteTo(jsonWriter);
            return;
        }

        jsonWriter.WriteStartObject();

        // Track which fields we've seen in the data
        var seenFieldNames = new HashSet<string>();

        foreach (var dataProperty in dataElement.EnumerateObject())
        {
            string fieldName = dataProperty.Name;
            seenFieldNames.Add(fieldName);

            if (expectedFieldDefinitions.TryGetValue(fieldName, out var fieldDefinition)) {
                jsonWriter.WritePropertyName(fieldName);
                ValidateField(
                    dataProperty.Value,
                    fieldDefinition,
                    schema,
                    $"{currentPath}.{fieldName}",
                    validationErrors,
                    jsonWriter
                );
            } else {
                // Unknown field — pass through (schemas don't forbid extra fields)
                jsonWriter.WritePropertyName(fieldName);
                dataProperty.Value.WriteTo(jsonWriter);
            }
        }

        // Check for missing required fields and apply defaults
        foreach (var (fieldName, fieldDefinition) in expectedFieldDefinitions)
        {
            if (seenFieldNames.Contains(fieldName)) continue;

            if (fieldDefinition.DefaultValueText != null) {
                // Apply default value
                jsonWriter.WritePropertyName(fieldName);
                WriteDefaultValue(fieldDefinition, jsonWriter);
            } else if (!fieldDefinition.IsOptional) {
                validationErrors.Add(
                    $"{currentPath}.{fieldName}: Required field is missing"
                );
            }
            // Optional fields without defaults are simply absent — that's fine
        }

        jsonWriter.WriteEndObject();
    }

    // ── Field validation ─────────────────────────────────────

    private static void ValidateField(
        JsonElement fieldValue,
        SchemaFieldDefinition fieldDefinition,
        LoadedSchema schema,
        string fieldPath,
        List<string> validationErrors,
        Utf8JsonWriter jsonWriter)
    {
        // Null check for optional fields
        if (fieldValue.ValueKind == JsonValueKind.Null) {
            if (!fieldDefinition.IsOptional) {
                validationErrors.Add(
                    $"{fieldPath}: Field is null but not marked as optional"
                );
            }
            jsonWriter.WriteNullValue();
            return;
        }

        ValidateFieldType(
            fieldValue,
            fieldDefinition.FieldType,
            schema,
            fieldPath,
            validationErrors,
            jsonWriter
        );
    }

    private static void ValidateFieldType(
        JsonElement fieldValue,
        SchemaFieldType expectedType,
        LoadedSchema schema,
        string fieldPath,
        List<string> validationErrors,
        Utf8JsonWriter jsonWriter)
    {
        switch (expectedType.FieldTypeKind)
        {
            case SchemaFieldTypeKind.Primitive:
                ValidatePrimitive(fieldValue, expectedType.PrimitiveType!.Value, fieldPath, validationErrors, jsonWriter);
                break;

            case SchemaFieldTypeKind.TypeRef:
                ValidateTypeRef(fieldValue, expectedType.ReferencedTypeName!, schema, fieldPath, validationErrors, jsonWriter);
                break;

            case SchemaFieldTypeKind.Array:
                ValidateArray(fieldValue, expectedType.ArrayElementType!, schema, fieldPath, validationErrors, jsonWriter);
                break;

            case SchemaFieldTypeKind.Map:
                ValidateMap(fieldValue, expectedType.MapValueType!, schema, fieldPath, validationErrors, jsonWriter);
                break;

            case SchemaFieldTypeKind.FreeformObject:
                // Any object is valid for freeform
                if (fieldValue.ValueKind != JsonValueKind.Object) {
                    validationErrors.Add(
                        $"{fieldPath}: Expected object (freeform), got {fieldValue.ValueKind}"
                    );
                }
                fieldValue.WriteTo(jsonWriter);
                break;

            case SchemaFieldTypeKind.InlineObject:
                ValidateObject(fieldValue, expectedType.InlineObjectFields!, schema, fieldPath, validationErrors, jsonWriter);
                break;
        }
    }

    // ── Primitive validation ─────────────────────────────────

    private static void ValidatePrimitive(
        JsonElement fieldValue,
        SchemaPrimitiveType expectedPrimitive,
        string fieldPath,
        List<string> validationErrors,
        Utf8JsonWriter jsonWriter)
    {
        bool isValid = expectedPrimitive switch
        {
            SchemaPrimitiveType.String => fieldValue.ValueKind == JsonValueKind.String,
            SchemaPrimitiveType.Int => fieldValue.ValueKind == JsonValueKind.Number && IsInteger(fieldValue),
            SchemaPrimitiveType.Float => fieldValue.ValueKind == JsonValueKind.Number,
            SchemaPrimitiveType.Bool => fieldValue.ValueKind == JsonValueKind.True || fieldValue.ValueKind == JsonValueKind.False,
            _ => false
        };

        if (!isValid) {
            validationErrors.Add(
                $"{fieldPath}: Expected {expectedPrimitive.ToString().ToLowerInvariant()}, " +
                $"got {DescribeJsonValue(fieldValue)}"
            );
        }

        fieldValue.WriteTo(jsonWriter);
    }

    private static bool IsInteger(JsonElement numberElement)
    {
        return numberElement.TryGetInt64(out _);
    }

    // ── TypeRef validation ───────────────────────────────────

    private static void ValidateTypeRef(
        JsonElement fieldValue,
        string referencedTypeName,
        LoadedSchema schema,
        string fieldPath,
        List<string> validationErrors,
        Utf8JsonWriter jsonWriter)
    {
        if (!schema.HasTypeDefinition(referencedTypeName)) {
            validationErrors.Add(
                $"{fieldPath}: References unknown type @{referencedTypeName}"
            );
            fieldValue.WriteTo(jsonWriter);
            return;
        }

        var referencedTypeDefinition = schema.GetTypeDefinition(referencedTypeName);
        ValidateObject(fieldValue, referencedTypeDefinition.FieldDefinitions, schema, fieldPath, validationErrors, jsonWriter);
    }

    // ── Array validation ─────────────────────────────────────

    private static void ValidateArray(
        JsonElement fieldValue,
        SchemaFieldType elementType,
        LoadedSchema schema,
        string fieldPath,
        List<string> validationErrors,
        Utf8JsonWriter jsonWriter)
    {
        if (fieldValue.ValueKind != JsonValueKind.Array) {
            validationErrors.Add(
                $"{fieldPath}: Expected array, got {fieldValue.ValueKind}"
            );
            fieldValue.WriteTo(jsonWriter);
            return;
        }

        jsonWriter.WriteStartArray();
        int elementIndex = 0;
        foreach (var arrayElement in fieldValue.EnumerateArray())
        {
            ValidateFieldType(
                arrayElement,
                elementType,
                schema,
                $"{fieldPath}[{elementIndex}]",
                validationErrors,
                jsonWriter
            );
            elementIndex++;
        }
        jsonWriter.WriteEndArray();
    }

    // ── Map validation ───────────────────────────────────────

    private static void ValidateMap(
        JsonElement fieldValue,
        SchemaFieldType mapValueType,
        LoadedSchema schema,
        string fieldPath,
        List<string> validationErrors,
        Utf8JsonWriter jsonWriter)
    {
        if (fieldValue.ValueKind != JsonValueKind.Object) {
            validationErrors.Add(
                $"{fieldPath}: Expected object (map), got {fieldValue.ValueKind}"
            );
            fieldValue.WriteTo(jsonWriter);
            return;
        }

        jsonWriter.WriteStartObject();
        foreach (var mapEntry in fieldValue.EnumerateObject())
        {
            jsonWriter.WritePropertyName(mapEntry.Name);
            ValidateFieldType(
                mapEntry.Value,
                mapValueType,
                schema,
                $"{fieldPath}.{mapEntry.Name}",
                validationErrors,
                jsonWriter
            );
        }
        jsonWriter.WriteEndObject();
    }

    // ── Default value writing ────────────────────────────────

    private static void WriteDefaultValue(
        SchemaFieldDefinition fieldDefinition,
        Utf8JsonWriter jsonWriter)
    {
        string defaultText = fieldDefinition.DefaultValueText!;

        // Try to write the default as the appropriate type
        switch (fieldDefinition.FieldType.FieldTypeKind)
        {
            case SchemaFieldTypeKind.Primitive when fieldDefinition.FieldType.PrimitiveType == SchemaPrimitiveType.Bool:
                jsonWriter.WriteBooleanValue(defaultText.Equals("true", StringComparison.OrdinalIgnoreCase));
                break;

            case SchemaFieldTypeKind.Primitive when fieldDefinition.FieldType.PrimitiveType == SchemaPrimitiveType.Int:
                if (long.TryParse(defaultText, out long defaultLong)) {
                    jsonWriter.WriteNumberValue(defaultLong);
                } else {
                    jsonWriter.WriteStringValue(defaultText);
                }
                break;

            case SchemaFieldTypeKind.Primitive when fieldDefinition.FieldType.PrimitiveType == SchemaPrimitiveType.Float:
                if (double.TryParse(defaultText, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double defaultDouble)) {
                    jsonWriter.WriteNumberValue(defaultDouble);
                } else {
                    jsonWriter.WriteStringValue(defaultText);
                }
                break;

            default:
                // String or unknown — write as string
                jsonWriter.WriteStringValue(defaultText);
                break;
        }
    }

    // ── Helpers ──────────────────────────────────────────────

    private static string DescribeJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => $"string \"{element.GetString()}\"",
            JsonValueKind.Number => $"number {element.GetRawText()}",
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            JsonValueKind.Object => "object",
            JsonValueKind.Array => "array",
            _ => element.ValueKind.ToString()
        };
    }
}

/// <summary>
/// Exception thrown when schema validation fails.
/// Contains all validation errors found.
/// </summary>
public class SchemaValidationException : Exception
{
    public List<string> ValidationErrors { get; }

    public SchemaValidationException(List<string> validationErrors)
        : base($"Schema validation failed with {validationErrors.Count} error(s):\n" +
               string.Join("\n", validationErrors.Select(e => $"  • {e}")))
    {
        ValidationErrors = validationErrors;
    }
}