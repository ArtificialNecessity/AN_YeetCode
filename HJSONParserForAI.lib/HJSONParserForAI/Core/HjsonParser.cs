namespace HJSONParserForAI.Core;

using System.Buffers;
using System.Text;
using System.Text.Json;

/// <summary>
/// Configuration options for the HJSON content parser.
/// </summary>
public class HjsonParserOptions
{
    /// <summary>
    /// When true, key attributes like key [optional, lang:en]: value
    /// are emitted into the JSON tree as a sibling "__keyAttributes" node
    /// within each object that has attributed keys.
    /// When false (default), attributes are parsed and silently discarded.
    /// </summary>
    public bool EmitKeyAttributes { get; init; } = false;

    /// <summary>
    /// The JSON property name used for the key attributes node.
    /// Default: "__keyAttributes"
    /// </summary>
    public string KeyAttributesNodeName { get; init; } = "__keyAttributes";
}

/// <summary>
/// Phase 2: Recursive descent HJSON parser that produces JsonDocument output.
/// Uses Utf8JsonWriter internally to build valid JSON from HJSON input,
/// then wraps it in JsonDocument.Parse().
///
/// HJSON features supported:
/// - Unquoted keys
/// - Unquoted string values (to end of line)
/// - Multiline strings with """
/// - Comments: //, /* */, #
/// - Trailing commas
/// - Optional root braces
/// - Standard JSON values: strings, numbers, booleans, null, objects, arrays
/// - Key attributes: key [attr, attr:value]: value (HJSON extension)
/// </summary>
public class HjsonContentParser
{
    private readonly HjsonParserOptions _parserOptions;
    private string _sourceText = "";
    private int _position;
    private int _lineNumber;
    private int _columnNumber;
    private List<SemanticError> _semanticErrors = new();

    public HjsonContentParser() : this(new HjsonParserOptions()) { }

    public HjsonContentParser(HjsonParserOptions parserOptions)
    {
        _parserOptions = parserOptions;
    }

    public ParseResult Parse(string sourceText, StructureResult structureAnalysisResult)
    {
        _semanticErrors = new List<SemanticError>();

        // If structural errors exist, we can't reliably parse content
        if (structureAnalysisResult.StructuralErrors.Count > 0)
        {
            var primaryStructuralError = structureAnalysisResult.StructuralErrors.First();
            _semanticErrors.Add(new SemanticError(
                "StructuralDependency",
                1, 1,
                "Cannot parse HJSON content until structural errors are resolved",
                primaryStructuralError,
                "Fix structural issues first, then re-parse to check for semantic errors"
            ));

            return new ParseResult(
                null,
                _semanticErrors,
                structureAnalysisResult.StructuralErrors
            );
        }

        _sourceText = sourceText;
        _position = 0;
        _lineNumber = 1;
        _columnNumber = 1;

        var jsonOutputBuffer = new ArrayBufferWriter<byte>();
        using var jsonWriter = new Utf8JsonWriter(jsonOutputBuffer, new JsonWriterOptions
        {
            Indented = false,
            SkipValidation = false
        });

        SkipWhitespaceAndComments();

        // Determine if root has braces or is a bare object
        if (_position < _sourceText.Length && _sourceText[_position] == '{')
        {
            WriteValue(jsonWriter);
        }
        else
        {
            // Bare root object (no braces) — treat entire content as object body
            WriteObjectBody(jsonWriter);
        }

        jsonWriter.Flush();

        var jsonBytes = jsonOutputBuffer.WrittenSpan.ToArray();
        var parsedDocument = JsonDocument.Parse(jsonBytes);

        return new ParseResult(
            parsedDocument,
            _semanticErrors,
            structureAnalysisResult.StructuralErrors
        );
    }

    private void WriteValue(Utf8JsonWriter jsonWriter)
    {
        SkipWhitespaceAndComments();

        if (_position >= _sourceText.Length)
        {
            AddSemanticError("UnexpectedEndOfInput", "Expected a value but reached end of input");
            jsonWriter.WriteNullValue();
            return;
        }

        char currentChar = _sourceText[_position];

        switch (currentChar)
        {
            case '{':
                WriteObject(jsonWriter);
                break;
            case '[':
                WriteArray(jsonWriter);
                break;
            case '"':
                WriteQuotedString(jsonWriter);
                break;
            default:
                // Could be: number, boolean, null, or unquoted string
                WriteUnquotedValue(jsonWriter);
                break;
        }
    }

    private void WriteObject(Utf8JsonWriter jsonWriter)
    {
        jsonWriter.WriteStartObject();
        Advance(); // skip '{'
        SkipWhitespaceAndComments();

        WriteObjectMembers(jsonWriter);

        if (_position < _sourceText.Length && _sourceText[_position] == '}')
        {
            Advance(); // skip '}'
        }
        else
        {
            AddSemanticError("UnclosedObject", "Expected '}' to close object");
        }

        jsonWriter.WriteEndObject();
    }

    /// <summary>
    /// Write a bare root object (no surrounding braces) as a JSON object
    /// </summary>
    private void WriteObjectBody(Utf8JsonWriter jsonWriter)
    {
        jsonWriter.WriteStartObject();
        WriteObjectMembers(jsonWriter);
        jsonWriter.WriteEndObject();
    }

    private void WriteObjectMembers(Utf8JsonWriter jsonWriter)
    {
        // Collect key attributes for this object scope
        Dictionary<string, Dictionary<string, string>>? collectedKeyAttributes = null;

        while (_position < _sourceText.Length)
        {
            SkipWhitespaceAndComments();

            if (_position >= _sourceText.Length) break;
            if (_sourceText[_position] == '}') break;

            // Parse key
            string memberKey = ReadKey();
            if (string.IsNullOrEmpty(memberKey)) break;

            SkipWhitespaceAndComments();

            // Check for key attributes: key [attr1, attr2:value]
            var keyAttributes = TryReadKeyAttributes();
            if (keyAttributes != null) {
                collectedKeyAttributes ??= new Dictionary<string, Dictionary<string, string>>();
                collectedKeyAttributes[memberKey] = keyAttributes;
            }

            SkipWhitespaceAndComments();

            // Expect ':' separator
            if (_position < _sourceText.Length && _sourceText[_position] == ':')
            {
                Advance(); // skip ':'
            }
            else
            {
                AddSemanticError("MissingColon", $"Expected ':' after key '{memberKey}'");
            }

            SkipWhitespaceAndComments();

            // Write key and value
            jsonWriter.WritePropertyName(memberKey);
            WriteValue(jsonWriter);

            SkipWhitespaceAndComments();

            // Optional comma
            if (_position < _sourceText.Length && _sourceText[_position] == ',')
            {
                Advance(); // skip ','
            }
        }

        // Emit __keyAttributes node if any keys had attributes and emission is enabled
        if (collectedKeyAttributes != null && _parserOptions.EmitKeyAttributes) {
            jsonWriter.WritePropertyName(_parserOptions.KeyAttributesNodeName);
            jsonWriter.WriteStartObject();
            foreach (var (attributedKeyName, attributeMap) in collectedKeyAttributes) {
                jsonWriter.WritePropertyName(attributedKeyName);
                jsonWriter.WriteStartObject();
                foreach (var (attrName, attrValue) in attributeMap) {
                    jsonWriter.WritePropertyName(attrName);
                    // Write "true" as boolean true, everything else as string
                    if (attrValue == "true") {
                        jsonWriter.WriteBooleanValue(true);
                    } else if (attrValue == "false") {
                        jsonWriter.WriteBooleanValue(false);
                    } else {
                        jsonWriter.WriteStringValue(attrValue);
                    }
                }
                jsonWriter.WriteEndObject();
            }
            jsonWriter.WriteEndObject();
        }
    }

    private void WriteArray(Utf8JsonWriter jsonWriter)
    {
        jsonWriter.WriteStartArray();
        Advance(); // skip '['
        SkipWhitespaceAndComments();

        while (_position < _sourceText.Length && _sourceText[_position] != ']')
        {
            WriteValue(jsonWriter);
            SkipWhitespaceAndComments();

            // Optional comma
            if (_position < _sourceText.Length && _sourceText[_position] == ',')
            {
                Advance(); // skip ','
                SkipWhitespaceAndComments();
            }
        }

        if (_position < _sourceText.Length && _sourceText[_position] == ']')
        {
            Advance(); // skip ']'
        }
        else
        {
            AddSemanticError("UnclosedArray", "Expected ']' to close array");
        }

        jsonWriter.WriteEndArray();
    }

    private void WriteQuotedString(Utf8JsonWriter jsonWriter)
    {
        // Check for multiline string """
        if (_position + 2 < _sourceText.Length &&
            _sourceText[_position + 1] == '"' &&
            _sourceText[_position + 2] == '"')
        {
            string multilineContent = ReadMultilineString();
            jsonWriter.WriteStringValue(multilineContent);
            return;
        }

        // Regular quoted string
        string quotedContent = ReadQuotedString();
        jsonWriter.WriteStringValue(quotedContent);
    }

    private void WriteUnquotedValue(Utf8JsonWriter jsonWriter)
    {
        // Try to identify the value type from the first characters
        string rawValue = PeekUnquotedToken();

        // Check for keywords
        if (rawValue == "true")
        {
            ConsumeChars(4);
            jsonWriter.WriteBooleanValue(true);
            return;
        }
        if (rawValue == "false")
        {
            ConsumeChars(5);
            jsonWriter.WriteBooleanValue(false);
            return;
        }
        if (rawValue == "null")
        {
            ConsumeChars(4);
            jsonWriter.WriteNullValue();
            return;
        }

        // Try to parse as number
        if (TryParseNumber(rawValue, out long longValue))
        {
            ConsumeChars(rawValue.Length);
            jsonWriter.WriteNumberValue(longValue);
            return;
        }
        if (TryParseDouble(rawValue, out double parsedDoubleValue))
        {
            ConsumeChars(rawValue.Length);
            // Format the double to always include at least one decimal place.
            // Utf8JsonWriter.WriteNumberValue(1.0) writes "1" (drops decimal),
            // so we format it ourselves and use WriteRawValue.
            string formattedDouble = FormatDoubleWithDecimal(parsedDoubleValue);
            jsonWriter.WriteRawValue(formattedDouble);
            return;
        }

        // It's an unquoted string — read to end of line
        string unquotedStringValue = ReadUnquotedString();
        jsonWriter.WriteStringValue(unquotedStringValue);
    }

    // ── Key reading ─────────────────────────────────────────

    private string ReadKey()
    {
        SkipWhitespaceAndComments();

        if (_position >= _sourceText.Length) return "";

        if (_sourceText[_position] == '"')
        {
            return ReadQuotedString();
        }

        // Unquoted key — read until ':' or whitespace
        int keyStartPosition = _position;
        while (_position < _sourceText.Length)
        {
            char keyChar = _sourceText[_position];
            if (keyChar == ':' || keyChar == '\n' || keyChar == '\r' ||
                keyChar == '{' || keyChar == '}' || keyChar == '[' || keyChar == ']')
            {
                break;
            }
            Advance();
        }

        return _sourceText[keyStartPosition.._position].Trim();
    }

    /// <summary>
    /// Parse key attributes: [attr1, attr2:value, attr3]
    /// Called when position is at '[' after a key name.
    /// Bare attributes like [optional] get value "true".
    /// Key:value attributes like [lang:en] get the specified value.
    /// Returns null if position is not at '['.
    /// </summary>
    private Dictionary<string, string>? TryReadKeyAttributes()
    {
        if (_position >= _sourceText.Length || _sourceText[_position] != '[') {
            return null;
        }

        Advance(); // skip '['
        var parsedAttributes = new Dictionary<string, string>();

        while (_position < _sourceText.Length && _sourceText[_position] != ']')
        {
            SkipWhitespaceAndComments();
            if (_position >= _sourceText.Length || _sourceText[_position] == ']') break;

            // Read attribute name
            int attrNameStart = _position;
            while (_position < _sourceText.Length)
            {
                char attrChar = _sourceText[_position];
                if (attrChar == ':' || attrChar == ',' || attrChar == ']' ||
                    attrChar == '\n' || attrChar == '\r') {
                    break;
                }
                Advance();
            }
            string attributeName = _sourceText[attrNameStart.._position].Trim();

            if (string.IsNullOrEmpty(attributeName)) break;

            // Check for :value
            if (_position < _sourceText.Length && _sourceText[_position] == ':')
            {
                Advance(); // skip ':'
                int attrValueStart = _position;
                while (_position < _sourceText.Length)
                {
                    char valChar = _sourceText[_position];
                    if (valChar == ',' || valChar == ']' ||
                        valChar == '\n' || valChar == '\r') {
                        break;
                    }
                    Advance();
                }
                string attributeValue = _sourceText[attrValueStart.._position].Trim();
                parsedAttributes[attributeName] = attributeValue;
            }
            else
            {
                // Bare flag — value is "true"
                parsedAttributes[attributeName] = "true";
            }

            SkipWhitespaceAndComments();

            // Optional comma between attributes
            if (_position < _sourceText.Length && _sourceText[_position] == ',')
            {
                Advance(); // skip ','
            }
        }

        if (_position < _sourceText.Length && _sourceText[_position] == ']')
        {
            Advance(); // skip ']'
        }
        else
        {
            AddSemanticError("UnclosedKeyAttributes",
                "Key attribute list '[' was never closed with ']'");
        }

        return parsedAttributes.Count > 0 ? parsedAttributes : null;
    }

    // ── String reading ──────────────────────────────────────

    private string ReadQuotedString()
    {
        Advance(); // skip opening '"'
        var quotedStringBuilder = new StringBuilder();

        while (_position < _sourceText.Length && _sourceText[_position] != '"')
        {
            if (_sourceText[_position] == '\\' && _position + 1 < _sourceText.Length)
            {
                char escapedChar = _sourceText[_position + 1];
                switch (escapedChar)
                {
                    case '"': quotedStringBuilder.Append('"'); break;
                    case '\\': quotedStringBuilder.Append('\\'); break;
                    case '/': quotedStringBuilder.Append('/'); break;
                    case 'b': quotedStringBuilder.Append('\b'); break;
                    case 'f': quotedStringBuilder.Append('\f'); break;
                    case 'n': quotedStringBuilder.Append('\n'); break;
                    case 'r': quotedStringBuilder.Append('\r'); break;
                    case 't': quotedStringBuilder.Append('\t'); break;
                    case 'u':
                        if (_position + 5 < _sourceText.Length)
                        {
                            string hexDigits = _sourceText.Substring(_position + 2, 4);
                            if (int.TryParse(hexDigits, System.Globalization.NumberStyles.HexNumber, null, out int unicodeCodePoint))
                            {
                                quotedStringBuilder.Append((char)unicodeCodePoint);
                                Advance(); Advance(); Advance(); Advance(); // skip 4 hex digits
                            }
                            else
                            {
                                quotedStringBuilder.Append("\\u");
                            }
                        }
                        break;
                    default:
                        quotedStringBuilder.Append('\\');
                        quotedStringBuilder.Append(escapedChar);
                        break;
                }
                Advance(); Advance(); // skip escape sequence
            }
            else
            {
                quotedStringBuilder.Append(_sourceText[_position]);
                Advance();
            }
        }

        if (_position < _sourceText.Length && _sourceText[_position] == '"')
        {
            Advance(); // skip closing '"'
        }

        return quotedStringBuilder.ToString();
    }

    private string ReadMultilineString()
    {
        // Skip opening """
        Advance(); Advance(); Advance();

        // Skip the first newline after """ if present
        if (_position < _sourceText.Length && _sourceText[_position] == '\n')
        {
            Advance();
        }
        else if (_position + 1 < _sourceText.Length &&
                   _sourceText[_position] == '\r' && _sourceText[_position + 1] == '\n')
        {
            Advance(); Advance();
        }

        var multilineBuilder = new StringBuilder();
        int closingTripleQuotePosition = _sourceText.IndexOf("\"\"\"", _position, StringComparison.Ordinal);

        if (closingTripleQuotePosition < 0)
        {
            // No closing """ found — take rest of input
            AddSemanticError("UnclosedMultilineString", "Multiline string opened with \"\"\" but never closed");
            string remainingContent = _sourceText[_position..];
            _position = _sourceText.Length;
            return remainingContent;
        }

        string rawMultilineContent = _sourceText[_position..closingTripleQuotePosition];

        // Move position past closing """
        _position = closingTripleQuotePosition + 3;
        // Update line/col tracking
        foreach (char multilineChar in rawMultilineContent)
        {
            if (multilineChar == '\n')
            {
                _lineNumber++;
                _columnNumber = 1;
            }
            else
            {
                _columnNumber++;
            }
        }
        _lineNumber++; // for the closing """ line
        _columnNumber = 4; // past """

        // HJSON multiline string processing:
        // - Remove common leading whitespace (based on closing """ indentation)
        // - Remove trailing newline before closing """
        return ProcessMultilineString(rawMultilineContent);
    }

    private static string ProcessMultilineString(string rawContent)
    {
        // Remove trailing newline
        if (rawContent.EndsWith("\r\n"))
        {
            rawContent = rawContent[..^2];
        }
        else if (rawContent.EndsWith("\n"))
        {
            rawContent = rawContent[..^1];
        }

        // Find minimum indentation across non-empty lines
        var contentLines = rawContent.Split('\n');
        int minimumIndent = int.MaxValue;
        foreach (string contentLine in contentLines)
        {
            string trimmedLine = contentLine.TrimEnd('\r');
            if (trimmedLine.Length == 0) continue;
            int lineIndent = 0;
            foreach (char indentChar in trimmedLine)
            {
                if (indentChar == ' ') lineIndent++;
                else if (indentChar == '\t') lineIndent += 2;
                else break;
            }
            minimumIndent = Math.Min(minimumIndent, lineIndent);
        }

        if (minimumIndent == int.MaxValue) minimumIndent = 0;

        // Strip common indentation
        var dedentedLines = new List<string>();
        foreach (string contentLine in contentLines)
        {
            string trimmedLine = contentLine.TrimEnd('\r');
            if (trimmedLine.Length == 0)
            {
                dedentedLines.Add("");
            }
            else
            {
                int charsToSkip = 0;
                int indentCounted = 0;
                foreach (char indentChar in trimmedLine)
                {
                    if (indentCounted >= minimumIndent) break;
                    if (indentChar == ' ') { indentCounted++; charsToSkip++; }
                    else if (indentChar == '\t') { indentCounted += 2; charsToSkip++; }
                    else break;
                }
                dedentedLines.Add(trimmedLine[charsToSkip..]);
            }
        }

        return string.Join("\n", dedentedLines);
    }

    private string ReadUnquotedString()
    {
        // HJSON unquoted strings: read to end of line, but also stop at
        // structural delimiters (comma, close brace, close bracket) so
        // inline arrays like [input, conversation_history] work correctly.
        int unquotedStartPosition = _position;

        while (_position < _sourceText.Length)
        {
            char unquotedChar = _sourceText[_position];
            if (unquotedChar == '\n' || unquotedChar == '\r') break;

            // Stop at structural delimiters
            if (unquotedChar == ',' || unquotedChar == '}' || unquotedChar == ']') break;

            // Stop at comment start
            if (unquotedChar == '/' && _position + 1 < _sourceText.Length)
            {
                if (_sourceText[_position + 1] == '/' || _sourceText[_position + 1] == '*') break;
            }
            if (unquotedChar == '#') break;

            Advance();
        }

        return _sourceText[unquotedStartPosition.._position].TrimEnd();
    }

    // ── Token peeking ───────────────────────────────────────

    /// <summary>
    /// Peek at the next token-like value (for number/keyword detection).
    /// Reads until whitespace, comma, colon, or structural character.
    /// Does NOT advance position.
    /// </summary>
    private string PeekUnquotedToken()
    {
        int peekPosition = _position;
        while (peekPosition < _sourceText.Length)
        {
            char peekChar = _sourceText[peekPosition];
            if (char.IsWhiteSpace(peekChar) || peekChar == ',' || peekChar == ':' ||
                peekChar == '}' || peekChar == ']' || peekChar == '{' || peekChar == '[' ||
                peekChar == '#')
            {
                break;
            }
            // Stop at comment start
            if (peekChar == '/' && peekPosition + 1 < _sourceText.Length &&
                (_sourceText[peekPosition + 1] == '/' || _sourceText[peekPosition + 1] == '*'))
            {
                break;
            }
            peekPosition++;
        }
        return _sourceText[_position..peekPosition];
    }

    // ── Number parsing ──────────────────────────────────────

    private static bool TryParseNumber(string tokenText, out long parsedLong)
    {
        return long.TryParse(tokenText, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out parsedLong);
    }

    private static bool TryParseDouble(string tokenText, out double parsedDouble)
    {
        if (tokenText.Contains('.') || tokenText.Contains('e') || tokenText.Contains('E'))
        {
            return double.TryParse(tokenText, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out parsedDouble);
        }
        parsedDouble = 0;
        return false;
    }

    /// <summary>
    /// Format a double value as a JSON number string, always including at least one decimal place.
    /// Examples: 1.0 → "1.0", 3.14 → "3.14", 0.05 → "0.05", 1e10 → "10000000000.0"
    /// </summary>
    private static string FormatDoubleWithDecimal(double doubleValue)
    {
        string formatted = doubleValue.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
        // If the formatted string doesn't contain a '.' or 'E', append ".0"
        if (!formatted.Contains('.') && !formatted.Contains('E') && !formatted.Contains('e'))
        {
            formatted += ".0";
        }
        return formatted;
    }

    // ── Whitespace and comment skipping ─────────────────────

    private void SkipWhitespaceAndComments()
    {
        while (_position < _sourceText.Length)
        {
            char skipChar = _sourceText[_position];

            // Whitespace
            if (char.IsWhiteSpace(skipChar))
            {
                Advance();
                continue;
            }

            // Line comment: // or #
            if (skipChar == '#' ||
                (skipChar == '/' && _position + 1 < _sourceText.Length && _sourceText[_position + 1] == '/'))
            {
                SkipLineComment();
                continue;
            }

            // Block comment: /* */
            if (skipChar == '/' && _position + 1 < _sourceText.Length && _sourceText[_position + 1] == '*')
            {
                SkipBlockComment();
                continue;
            }

            break;
        }
    }

    private void SkipLineComment()
    {
        while (_position < _sourceText.Length && _sourceText[_position] != '\n')
        {
            Advance();
        }
    }

    private void SkipBlockComment()
    {
        Advance(); Advance(); // skip /*
        while (_position + 1 < _sourceText.Length)
        {
            if (_sourceText[_position] == '*' && _sourceText[_position + 1] == '/')
            {
                Advance(); Advance(); // skip */
                return;
            }
            Advance();
        }
        // Reached end without closing */
        AddSemanticError("UnclosedBlockComment", "Block comment /* was never closed with */");
    }

    // ── Position tracking ───────────────────────────────────

    private void Advance()
    {
        if (_position < _sourceText.Length)
        {
            if (_sourceText[_position] == '\n')
            {
                _lineNumber++;
                _columnNumber = 1;
            }
            else
            {
                _columnNumber++;
            }
            _position++;
        }
    }

    private void ConsumeChars(int charCount)
    {
        for (int consumeIndex = 0; consumeIndex < charCount; consumeIndex++)
        {
            Advance();
        }
    }

    // ── Error reporting ─────────────────────────────────────

    private void AddSemanticError(string errorKind, string errorMessage)
    {
        _semanticErrors.Add(new SemanticError(
            errorKind,
            _lineNumber,
            _columnNumber,
            errorMessage
        ));
    }
}