namespace YeetCode.Grammar;

/// <summary>
/// Parses a token stream from GrammarLexer into a ParsedGrammar AST.
/// Uses recursive descent parsing with operator precedence for PEG expressions.
/// </summary>
public class GrammarParser
{
    private List<GrammarToken> _allTokens = new();
    private int _currentTokenIndex;

    public ParsedGrammar Parse(List<GrammarToken> tokens)
    {
        _allTokens = tokens;
        _currentTokenIndex = 0;

        var parserRules = new Dictionary<string, GrammarRule>();
        var lexerTokens = new Dictionary<string, TokenDefinition>();
        string? skipPattern = null;
        var definedParameters = new List<string>();

        while (!IsAtEnd())
        {
            // Skip directives for now (preprocessor will handle them)
            if (CurrentToken.Type == TokenType.DirectiveDefine)
            {
                Advance(); // %define
                string paramName = Expect(TokenType.RuleName, "parameter name after %define").Text;
                definedParameters.Add(paramName);
                continue;
            }

            if (CurrentToken.Type == TokenType.DirectiveSkip)
            {
                Advance(); // %skip
                Expect(TokenType.DefinitionAssign, "'::=' after %skip");
                var regexToken = Expect(TokenType.RegexPattern, "regex pattern after %skip ::=");
                skipPattern = regexToken.Text;
                continue;
            }

            // Skip preprocessor conditionals for now
            if (CurrentToken.Type == TokenType.DirectiveIf ||
                CurrentToken.Type == TokenType.DirectiveElse ||
                CurrentToken.Type == TokenType.DirectiveEndif)
            {
                Advance();
                continue;
            }

            // Parse rule or token definition
            if (CurrentToken.Type == TokenType.RuleName)
            {
                var parsedRule = ParseRule();
                parserRules[parsedRule.RuleName] = parsedRule;
            }
            else if (CurrentToken.Type == TokenType.TokenName)
            {
                var parsedToken = ParseTokenDefinition();
                lexerTokens[parsedToken.TokenName] = parsedToken;
            }
            else
            {
                throw new GrammarParserException(
                    $"Unexpected token {CurrentToken.Type} at line {CurrentToken.Line}. " +
                    "Expected rule name (lowercase) or token name (UPPERCASE)"
                );
            }
        }

        return new ParsedGrammar
        {
            ParserRules = parserRules,
            LexerTokens = lexerTokens,
            SkipPattern = skipPattern,
            DefinedParameters = definedParameters
        };
    }

    private GrammarRule ParseRule()
    {
        int ruleLine = CurrentToken.Line;
        string ruleName = Expect(TokenType.RuleName, "rule name").Text;
        Expect(TokenType.DefinitionAssign, "'::=' after rule name");

        var ruleExpression = ParseChoiceExpression();
        var typeMappings = new List<TypeMapping>();

        // Parse type mappings: -> @Type, -> path[], etc.
        while (CurrentToken.Type == TokenType.Arrow)
        {
            Advance(); // ->
            var mapping = ParseTypeMapping();
            typeMappings.Add(mapping);
        }

        return new GrammarRule
        {
            RuleName = ruleName,
            Expression = ruleExpression,
            TypeMappings = typeMappings,
            SourceLine = ruleLine
        };
    }

    private TokenDefinition ParseTokenDefinition()
    {
        int tokenLine = CurrentToken.Line;
        string tokenName = Expect(TokenType.TokenName, "token name").Text;
        Expect(TokenType.DefinitionAssign, "'::=' after token name");

        var regexToken = Expect(TokenType.RegexPattern, "regex pattern after token name");

        return new TokenDefinition
        {
            TokenName = tokenName,
            RegexPattern = regexToken.Text,
            RegexFlags = regexToken.RegexFlags,
            SourceLine = tokenLine
        };
    }

    private TypeMapping ParseTypeMapping()
    {
        // -> @Type
        // -> @Type { kind: @Variant }
        // -> path[]
        // -> path[capture]

        if (CurrentToken.Type == TokenType.TypeReference)
        {
            string targetTypeName = CurrentToken.Text[1..]; // strip @
            Advance();

            // Check for { kind: @Variant }
            if (CurrentToken.Type == TokenType.LeftBrace)
            {
                Advance(); // {
                Expect(TokenType.RuleName, "'kind' keyword"); // Should be "kind"
                Expect(TokenType.Colon, "':' after 'kind'");
                var variantToken = Expect(TokenType.TypeReference, "@Variant after 'kind:'");
                string kindVariantName = variantToken.Text[1..]; // strip @
                Expect(TokenType.RightBrace, "'}' after kind value");

                return new TypeMapping
                {
                    TargetTypeName = targetTypeName,
                    KindVariantName = kindVariantName
                };
            }

            return new TypeMapping
            {
                TargetTypeName = targetTypeName
            };
        }

        // -> path[] or -> path[capture]
        if (CurrentToken.Type == TokenType.RuleName)
        {
            string schemaPath = ParseSchemaPath();

            if (CurrentToken.Type == TokenType.LeftBracket)
            {
                Advance(); // [

                if (CurrentToken.Type == TokenType.RightBracket)
                {
                    // path[] — array append
                    Advance(); // ]
                    return new TypeMapping
                    {
                        SchemaPath = schemaPath,
                        IsArrayAppend = true
                    };
                }
                else
                {
                    // path[capture] — map insert
                    string captureKeyName = Expect(TokenType.RuleName, "capture name in map key").Text;
                    Expect(TokenType.RightBracket, "']' after capture name");
                    return new TypeMapping
                    {
                        SchemaPath = schemaPath,
                        MapKeyCaptureName = captureKeyName,
                        IsArrayAppend = false
                    };
                }
            }

            throw new GrammarParserException(
                $"Expected '[' after schema path '{schemaPath}' at line {CurrentToken.Line}"
            );
        }

        throw new GrammarParserException(
            $"Expected @Type or schema path after '->' at line {CurrentToken.Line}, got {CurrentToken.Type}"
        );
    }

    private string ParseSchemaPath()
    {
        // path or path.subpath or path[].subpath
        var pathSegments = new List<string>();

        pathSegments.Add(Expect(TokenType.RuleName, "path segment").Text);

        while (CurrentToken.Type == TokenType.Dot)
        {
            Advance(); // .
            pathSegments.Add(Expect(TokenType.RuleName, "path segment after '.'").Text);
        }

        return string.Join(".", pathSegments);
    }

    // ── PEG Expression Parsing (precedence climbing) ────────

    /// <summary>
    /// Parse choice expression: a | b | c
    /// Lowest precedence.
    /// </summary>
    private PegExpression ParseChoiceExpression()
    {
        var alternatives = new List<PegExpression>();
        alternatives.Add(ParseSequenceExpression());

        while (CurrentToken.Type == TokenType.Pipe)
        {
            Advance(); // |
            alternatives.Add(ParseSequenceExpression());
        }

        if (alternatives.Count == 1)
        {
            return alternatives[0];
        }

        return new ChoiceExpression
        {
            ChoiceAlternatives = alternatives,
            SourceLine = alternatives[0].SourceLine,
            SourceColumn = alternatives[0].SourceColumn
        };
    }

    /// <summary>
    /// Parse sequence expression: a b c
    /// Middle precedence.
    /// </summary>
    private PegExpression ParseSequenceExpression()
    {
        var sequenceElements = new List<PegExpression>();

        // Keep parsing unary expressions until we hit a terminator
        while (!IsSequenceTerminator())
        {
            sequenceElements.Add(ParseUnaryExpression());
        }

        if (sequenceElements.Count == 0)
        {
            throw new GrammarParserException(
                $"Empty sequence at line {CurrentToken.Line}"
            );
        }

        if (sequenceElements.Count == 1)
        {
            return sequenceElements[0];
        }

        return new SequenceExpression
        {
            SequenceElements = sequenceElements,
            SourceLine = sequenceElements[0].SourceLine,
            SourceColumn = sequenceElements[0].SourceColumn
        };
    }

    private bool IsSequenceTerminator()
    {
        // Stop at: |, ), ->, ::=, EOF
        if (CurrentToken.Type == TokenType.Pipe ||
            CurrentToken.Type == TokenType.RightParen ||
            CurrentToken.Type == TokenType.Arrow ||
            CurrentToken.Type == TokenType.DefinitionAssign ||
            IsAtEnd())
        {
            return true;
        }

        // Stop at directives
        if (CurrentToken.Type == TokenType.DirectiveSkip ||
            CurrentToken.Type == TokenType.DirectiveDefine ||
            CurrentToken.Type == TokenType.DirectiveIf ||
            CurrentToken.Type == TokenType.DirectiveElse ||
            CurrentToken.Type == TokenType.DirectiveEndif)
        {
            return true;
        }

        // Stop at new rule/token definition: name/TOKEN followed by ::=
        if ((CurrentToken.Type == TokenType.RuleName || CurrentToken.Type == TokenType.TokenName) &&
            _currentTokenIndex + 1 < _allTokens.Count &&
            _allTokens[_currentTokenIndex + 1].Type == TokenType.DefinitionAssign)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Parse unary expression: e*, e+, e?
    /// Highest precedence.
    /// </summary>
    private PegExpression ParseUnaryExpression()
    {
        var primaryExpr = ParsePrimaryExpression();

        // Check for postfix operators
        if (CurrentToken.Type == TokenType.Star)
        {
            Advance();
            return new RepeatExpression
            {
                RepeatedExpression = primaryExpr,
                Mode = RepeatMode.ZeroOrMore,
                SourceLine = primaryExpr.SourceLine,
                SourceColumn = primaryExpr.SourceColumn
            };
        }

        if (CurrentToken.Type == TokenType.Plus)
        {
            Advance();
            return new RepeatExpression
            {
                RepeatedExpression = primaryExpr,
                Mode = RepeatMode.OneOrMore,
                SourceLine = primaryExpr.SourceLine,
                SourceColumn = primaryExpr.SourceColumn
            };
        }

        if (CurrentToken.Type == TokenType.Question)
        {
            Advance();
            return new RepeatExpression
            {
                RepeatedExpression = primaryExpr,
                Mode = RepeatMode.Optional,
                SourceLine = primaryExpr.SourceLine,
                SourceColumn = primaryExpr.SourceColumn
            };
        }

        return primaryExpr;
    }

    /// <summary>
    /// Parse primary expression: literal, token, rule, capture, or group
    /// </summary>
    private PegExpression ParsePrimaryExpression()
    {
        int exprLine = CurrentToken.Line;
        int exprColumn = CurrentToken.Column;

        // String literal: "text"
        if (CurrentToken.Type == TokenType.StringLiteral)
        {
            string literalText = CurrentToken.Text;
            Advance();
            return new LiteralExpression
            {
                LiteralText = literalText,
                SourceLine = exprLine,
                SourceColumn = exprColumn
            };
        }

        // Group: (expr)
        if (CurrentToken.Type == TokenType.LeftParen)
        {
            Advance(); // (
            var groupedExpr = ParseChoiceExpression();
            Expect(TokenType.RightParen, "')' to close group");
            return new GroupExpression
            {
                GroupedExpression = groupedExpr,
                SourceLine = exprLine,
                SourceColumn = exprColumn
            };
        }

        // Token reference: TOKEN_NAME
        if (CurrentToken.Type == TokenType.TokenName)
        {
            string tokenName = CurrentToken.Text;
            Advance();
            return new TokenRefExpression
            {
                TokenName = tokenName,
                SourceLine = exprLine,
                SourceColumn = exprColumn
            };
        }

        // Rule reference or capture: rule_name or name:expr
        if (CurrentToken.Type == TokenType.RuleName)
        {
            string identifierName = CurrentToken.Text;
            Advance();

            // Check for capture: name:expr
            if (CurrentToken.Type == TokenType.Colon)
            {
                Advance(); // :
                var capturedExpr = ParseUnaryExpression();
                return new CaptureExpression
                {
                    CaptureName = identifierName,
                    CapturedExpression = capturedExpr,
                    SourceLine = exprLine,
                    SourceColumn = exprColumn
                };
            }

            // Just a rule reference
            return new RuleRefExpression
            {
                RuleName = identifierName,
                SourceLine = exprLine,
                SourceColumn = exprColumn
            };
        }

        throw new GrammarParserException(
            $"Unexpected token {CurrentToken.Type} at line {CurrentToken.Line}, column {CurrentToken.Column}. " +
            "Expected string literal, token name, rule name, or '('"
        );
    }

    // ── Token Stream Helpers ─────────────────────────────

    private GrammarToken CurrentToken => _allTokens[_currentTokenIndex];

    private bool IsAtEnd() => _currentTokenIndex >= _allTokens.Count ||
                              CurrentToken.Type == TokenType.EndOfFile;

    private void Advance()
    {
        if (!IsAtEnd())
        {
            _currentTokenIndex++;
        }
    }

    private GrammarToken Expect(TokenType expectedType, string description)
    {
        if (CurrentToken.Type != expectedType)
        {
            throw new GrammarParserException(
                $"Expected {description} at line {CurrentToken.Line}, column {CurrentToken.Column}, " +
                $"but got {CurrentToken.Type} ('{CurrentToken.Text}')"
            );
        }

        var matchedToken = CurrentToken;
        Advance();
        return matchedToken;
    }
}

/// <summary>
/// Exception thrown when the grammar parser encounters an error.
/// </summary>
public class GrammarParserException : Exception
{
    public GrammarParserException(string message) : base(message) { }
}