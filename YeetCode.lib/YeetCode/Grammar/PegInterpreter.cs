using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace YeetCode.Grammar;

/// <summary>
/// Executes a parsed PEG grammar against input text to produce a JsonDocument.
/// Uses recursive descent with backtracking for PEG semantics.
/// </summary>
public class PegInterpreter
{
    private readonly ParsedGrammar _parsedGrammar;
    private readonly Dictionary<string, Regex> _compiledTokenPatterns;
    private readonly Regex? _compiledSkipPattern;

    private string _inputText = "";
    private int _inputPosition;
    private int _currentLine;
    private int _currentColumn;

    public PegInterpreter(ParsedGrammar parsedGrammar)
    {
        _parsedGrammar = parsedGrammar;

        // Pre-compile all token regex patterns
        _compiledTokenPatterns = new Dictionary<string, Regex>();
        foreach (var (tokenName, tokenDef) in parsedGrammar.LexerTokens)
        {
            var regexOptions = RegexOptions.None;
            if (tokenDef.RegexFlags != null)
            {
                if (tokenDef.RegexFlags.Contains('s'))
                {
                    regexOptions |= RegexOptions.Singleline; // dotall
                }
                if (tokenDef.RegexFlags.Contains('i'))
                {
                    regexOptions |= RegexOptions.IgnoreCase;
                }
            }
            _compiledTokenPatterns[tokenName] = new Regex(tokenDef.RegexPattern, regexOptions);
        }

        // Compile skip pattern if present
        if (parsedGrammar.SkipPattern != null)
        {
            _compiledSkipPattern = new Regex(parsedGrammar.SkipPattern);
        }
    }

    /// <summary>
    /// Parse input text using the grammar, starting from the first rule.
    /// Returns a JsonDocument containing the captured data.
    /// </summary>
    public JsonDocument Parse(string inputText, string? startRuleName = null)
    {
        _inputText = inputText;
        _inputPosition = 0;
        _currentLine = 1;
        _currentColumn = 1;

        // Determine start rule (first rule in grammar if not specified)
        string actualStartRule = startRuleName ?? _parsedGrammar.ParserRules.Keys.First();

        if (!_parsedGrammar.ParserRules.ContainsKey(actualStartRule))
        {
            throw new PegInterpreterException(
                $"Start rule '{actualStartRule}' not found in grammar. " +
                $"Available rules: {string.Join(", ", _parsedGrammar.ParserRules.Keys)}"
            );
        }

        // Parse using the start rule
        var capturedData = new Dictionary<string, object?>();
        var parseContext = new ParseContext { CapturedData = capturedData };

        bool matchSucceeded = TryMatchRule(actualStartRule, parseContext);

        if (!matchSucceeded)
        {
            throw new PegInterpreterException(
                $"Failed to match start rule '{actualStartRule}' at line {_currentLine}, column {_currentColumn}. " +
                $"Remaining input: {PeekInput(50)}"
            );
        }

        // Skip trailing whitespace
        SkipWhitespace();

        // Verify we consumed all input
        if (_inputPosition < _inputText.Length)
        {
            throw new PegInterpreterException(
                $"Unexpected input after parse completed at line {_currentLine}, column {_currentColumn}. " +
                $"Remaining: {PeekInput(50)}"
            );
        }

        // Convert captured data to JsonDocument
        return ConvertToJsonDocument(capturedData);
    }

    // ── Rule and Expression Matching ─────────────────────

    private bool TryMatchRule(string ruleName, ParseContext parseContext)
    {
        if (!_parsedGrammar.ParserRules.TryGetValue(ruleName, out var grammarRule))
        {
            throw new PegInterpreterException($"Rule '{ruleName}' not found in grammar");
        }

        var savedPosition = SavePosition();
        var ruleContext = new ParseContext { CapturedData = new Dictionary<string, object?>() };

        bool matchSucceeded = TryMatchExpression(grammarRule.Expression, ruleContext);

        if (!matchSucceeded)
        {
            RestorePosition(savedPosition);
            return false;
        }

        // Apply type mappings if present
        foreach (var typeMapping in grammarRule.TypeMappings)
        {
            ApplyTypeMapping(typeMapping, ruleContext, parseContext);
        }

        // Merge rule captures into parent context
        foreach (var (captureName, captureValue) in ruleContext.CapturedData)
        {
            parseContext.CapturedData[captureName] = captureValue;
        }

        return true;
    }

    private bool TryMatchExpression(PegExpression expression, ParseContext parseContext)
    {
        return expression switch
        {
            LiteralExpression lit => TryMatchLiteral(lit.LiteralText),
            TokenRefExpression tok => TryMatchToken(tok.TokenName, parseContext),
            RuleRefExpression rule => TryMatchRule(rule.RuleName, parseContext),
            CaptureExpression cap => TryMatchCapture(cap, parseContext),
            SequenceExpression seq => TryMatchSequence(seq, parseContext),
            ChoiceExpression choice => TryMatchChoice(choice, parseContext),
            RepeatExpression rep => TryMatchRepeat(rep, parseContext),
            GroupExpression grp => TryMatchExpression(grp.GroupedExpression, parseContext),
            _ => throw new PegInterpreterException($"Unknown expression type: {expression.GetType().Name}")
        };
    }

    private bool TryMatchLiteral(string literalText)
    {
        SkipWhitespace();

        if (_inputPosition + literalText.Length > _inputText.Length)
        {
            return false;
        }

        if (_inputText.AsSpan(_inputPosition, literalText.Length).SequenceEqual(literalText.AsSpan()))
        {
            AdvancePosition(literalText.Length);
            return true;
        }

        return false;
    }

    private bool TryMatchToken(string tokenName, ParseContext parseContext)
    {
        SkipWhitespace();

        if (!_compiledTokenPatterns.TryGetValue(tokenName, out var tokenPattern))
        {
            throw new PegInterpreterException($"Token '{tokenName}' not found in grammar");
        }

        var regexMatch = tokenPattern.Match(_inputText, _inputPosition);

        if (regexMatch.Success && regexMatch.Index == _inputPosition)
        {
            string matchedText = regexMatch.Value;
            AdvancePosition(matchedText.Length);
            // Store the matched token text in context (will be used by captures)
            parseContext.CapturedData["__last_token"] = matchedText;
            return true;
        }

        return false;
    }

    private bool TryMatchCapture(CaptureExpression captureExpr, ParseContext parseContext)
    {
        int captureStartPosition = _inputPosition;
        var captureContext = new ParseContext { CapturedData = new Dictionary<string, object?>() };

        bool matchSucceeded = TryMatchExpression(captureExpr.CapturedExpression, captureContext);

        if (!matchSucceeded)
        {
            return false;
        }

        // Determine what to store as the captured value:
        // 1. If the captured expression was a token match, store the matched text
        // 2. If it produced sub-captures (from a rule), store those as a dictionary
        // 3. Otherwise store the raw matched text
        object? capturedValue;

        if (captureContext.CapturedData.TryGetValue("__last_token", out var lastTokenText))
        {
            // Token capture — store the matched text string
            capturedValue = lastTokenText;
        }
        else if (captureContext.CapturedData.Count > 0)
        {
            // Rule capture — store the sub-captures as a dictionary
            var cleanedCaptures = new Dictionary<string, object?>();
            foreach (var (key, value) in captureContext.CapturedData)
            {
                if (!key.StartsWith("__"))
                {
                    cleanedCaptures[key] = value;
                }
            }
            capturedValue = cleanedCaptures.Count > 0 ? cleanedCaptures : _inputText[captureStartPosition.._inputPosition];
        }
        else
        {
            // Literal or other — store the matched text
            capturedValue = _inputText[captureStartPosition.._inputPosition];
        }

        parseContext.CapturedData[captureExpr.CaptureName] = capturedValue;
        return true;
    }

    private bool TryMatchSequence(SequenceExpression seqExpr, ParseContext parseContext)
    {
        var savedPosition = SavePosition();

        foreach (var elementExpr in seqExpr.SequenceElements)
        {
            bool elementMatched = TryMatchExpression(elementExpr, parseContext);
            if (!elementMatched)
            {
                RestorePosition(savedPosition);
                return false;
            }
        }

        return true;
    }

    private bool TryMatchChoice(ChoiceExpression choiceExpr, ParseContext parseContext)
    {
        var savedPosition = SavePosition();

        foreach (var alternativeExpr in choiceExpr.ChoiceAlternatives)
        {
            var alternativeContext = new ParseContext { CapturedData = new Dictionary<string, object?>() };

            bool alternativeMatched = TryMatchExpression(alternativeExpr, alternativeContext);

            if (alternativeMatched)
            {
                // Merge captures from successful alternative
                foreach (var (captureName, captureValue) in alternativeContext.CapturedData)
                {
                    parseContext.CapturedData[captureName] = captureValue;
                }
                return true;
            }

            // Restore position for next alternative
            RestorePosition(savedPosition);
        }

        return false;
    }

    private bool TryMatchRepeat(RepeatExpression repeatExpr, ParseContext parseContext)
    {
        int matchCount = 0;

        while (true)
        {
            var savedPosition = SavePosition();
            var iterationContext = new ParseContext { CapturedData = new Dictionary<string, object?>() };

            bool iterationMatched = TryMatchExpression(repeatExpr.RepeatedExpression, iterationContext);

            if (!iterationMatched)
            {
                RestorePosition(savedPosition);
                break;
            }

            matchCount++;

            // Merge captures from this iteration
            foreach (var (captureName, captureValue) in iterationContext.CapturedData)
            {
                parseContext.CapturedData[captureName] = captureValue;
            }
        }

        // Check if we matched the required number of times
        return repeatExpr.Mode switch
        {
            RepeatMode.ZeroOrMore => true,
            RepeatMode.OneOrMore => matchCount >= 1,
            RepeatMode.Optional => true,
            _ => throw new PegInterpreterException($"Unknown repeat mode: {repeatExpr.Mode}")
        };
    }

    // ── Type Mapping Application ─────────────────────────

    private void ApplyTypeMapping(TypeMapping typeMapping, ParseContext ruleContext, ParseContext parentContext)
    {
        // Case 1: -> @Type or -> @Type { kind: @Variant }
        if (typeMapping.TargetTypeName != null)
        {
            var typeInstance = new Dictionary<string, object?>();

            // Set kind discriminator if specified
            if (typeMapping.KindVariantName != null)
            {
                typeInstance["kind"] = "@" + typeMapping.KindVariantName;
            }

            // Copy all captures from rule context into the type instance
            foreach (var (captureName, captureValue) in ruleContext.CapturedData)
            {
                typeInstance[captureName] = captureValue;
            }

            // Store in parent context (will be used by schema path mappings or returned as result)
            parentContext.CapturedData["__type_instance"] = typeInstance;
            return;
        }

        // Case 2: -> path[] or -> path[capture]
        if (typeMapping.SchemaPath != null)
        {
            // For now, store the mapping info - actual path navigation happens during final assembly
            // This is a simplified implementation; full implementation would navigate the schema path
            var pathData = new Dictionary<string, object?>
            {
                ["__schema_path"] = typeMapping.SchemaPath,
                ["__is_array_append"] = typeMapping.IsArrayAppend,
                ["__map_key_capture"] = typeMapping.MapKeyCaptureName,
                ["__captured_data"] = new Dictionary<string, object?>(ruleContext.CapturedData)
            };

            parentContext.CapturedData["__path_mapping"] = pathData;
            return;
        }

        throw new PegInterpreterException("Type mapping has no target type or schema path");
    }

    // ── Position Management ──────────────────────────────

    private void SkipWhitespace()
    {
        if (_compiledSkipPattern == null)
        {
            return;
        }

        var skipMatch = _compiledSkipPattern.Match(_inputText, _inputPosition);

        if (skipMatch.Success && skipMatch.Index == _inputPosition)
        {
            AdvancePosition(skipMatch.Length);
        }
    }

    private void AdvancePosition(int characterCount)
    {
        for (int i = 0; i < characterCount && _inputPosition < _inputText.Length; i++)
        {
            if (_inputText[_inputPosition] == '\n')
            {
                _currentLine++;
                _currentColumn = 1;
            }
            else
            {
                _currentColumn++;
            }
            _inputPosition++;
        }
    }

    private (int position, int line, int column) SavePosition()
    {
        return (_inputPosition, _currentLine, _currentColumn);
    }

    private void RestorePosition((int position, int line, int column) savedState)
    {
        _inputPosition = savedState.position;
        _currentLine = savedState.line;
        _currentColumn = savedState.column;
    }

    private string PeekInput(int maxCharacters)
    {
        int remainingLength = Math.Min(maxCharacters, _inputText.Length - _inputPosition);
        return _inputText.Substring(_inputPosition, remainingLength);
    }

    // ── JSON Conversion ──────────────────────────────────

    private JsonDocument ConvertToJsonDocument(Dictionary<string, object?> capturedData)
    {
        using var memoryStream = new MemoryStream();
        using (var jsonWriter = new Utf8JsonWriter(memoryStream))
        {
            WriteObjectToJson(capturedData, jsonWriter);
        }

        memoryStream.Position = 0;
        return JsonDocument.Parse(memoryStream);
    }

    private void WriteObjectToJson(Dictionary<string, object?> dataObject, Utf8JsonWriter jsonWriter)
    {
        jsonWriter.WriteStartObject();

        foreach (var (key, value) in dataObject)
        {
            jsonWriter.WritePropertyName(key);
            WriteValueToJson(value, jsonWriter);
        }

        jsonWriter.WriteEndObject();
    }

    private void WriteValueToJson(object? value, Utf8JsonWriter jsonWriter)
    {
        switch (value)
        {
            case null:
                jsonWriter.WriteNullValue();
                break;
            case string stringValue:
                jsonWriter.WriteStringValue(stringValue);
                break;
            case int intValue:
                jsonWriter.WriteNumberValue(intValue);
                break;
            case long longValue:
                jsonWriter.WriteNumberValue(longValue);
                break;
            case double doubleValue:
                jsonWriter.WriteNumberValue(doubleValue);
                break;
            case bool boolValue:
                jsonWriter.WriteBooleanValue(boolValue);
                break;
            case Dictionary<string, object?> dictValue:
                WriteObjectToJson(dictValue, jsonWriter);
                break;
            case List<object?> listValue:
                jsonWriter.WriteStartArray();
                foreach (var item in listValue)
                {
                    WriteValueToJson(item, jsonWriter);
                }
                jsonWriter.WriteEndArray();
                break;
            default:
                throw new PegInterpreterException($"Cannot convert value of type {value.GetType().Name} to JSON");
        }
    }
}

/// <summary>
/// Parse context for tracking captures during parsing.
/// </summary>
internal class ParseContext
{
    public required Dictionary<string, object?> CapturedData { get; init; }
}

/// <summary>
/// Exception thrown when the PEG interpreter encounters an error.
/// </summary>
public class PegInterpreterException : Exception
{
    public PegInterpreterException(string message) : base(message) { }
}