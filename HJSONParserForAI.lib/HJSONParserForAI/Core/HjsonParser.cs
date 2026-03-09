namespace HJSONParserForAI.Core;

using System.Buffers;
using System.Text;
using System.Text.Json;

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
/// </summary>
public class HjsonContentParser
{
    private string _sourceText = "";
    private int _position;
    private int _lineNumber;
    private int _columnNumber;
    private List<SemanticError> _semanticErrors = new();

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
        while (_position < _sourceText.Length)
        {
            SkipWhitespaceAndComments();

            if (_position >= _sourceText.Length) break;
            if (_sourceText[_position] == '}') break;

            // Parse key
            string memberKey = ReadKey();
            if (string.IsNullOrEmpty(memberKey)) break;

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
        if (TryParseDouble(rawValue, out double doubleValue))
        {
            ConsumeChars(rawValue.Length);
            jsonWriter.WriteNumberValue(doubleValue);
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