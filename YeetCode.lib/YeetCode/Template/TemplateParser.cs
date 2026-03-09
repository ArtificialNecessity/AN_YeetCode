namespace YeetCode.Template;

/// <summary>
/// Parses template lexer tokens into an AST (list of TemplateNode).
/// Handles block nesting for each/if/define/output directives.
/// </summary>
public class TemplateParser
{
    private List<TemplateToken> _tokens = new();
    private int _tokenIndex;

    /// <summary>
    /// Parse a template source string into an AST.
    /// </summary>
    public List<TemplateNode> Parse(string templateSource)
    {
        var lexer = new TemplateLexer();
        _tokens = lexer.Lex(templateSource);
        _tokenIndex = 0;

        return ParseNodeList(null);
    }

    /// <summary>
    /// Parse a list of nodes until we hit a closing directive or end of tokens.
    /// closingKeyword is null for top-level, or "/each", "/if", "/define", "/output" for blocks.
    /// </summary>
    private List<TemplateNode> ParseNodeList(string? closingKeyword)
    {
        var nodes = new List<TemplateNode>();

        while (_tokenIndex < _tokens.Count)
        {
            var currentToken = _tokens[_tokenIndex];

            if (currentToken.Kind == TemplateTokenKind.LiteralText) {
                nodes.Add(new LiteralTextNode
                {
                    Text = currentToken.Content,
                    SourceLine = currentToken.Line,
                    SourceColumn = currentToken.Column
                });
                _tokenIndex++;
                continue;
            }

            // DelimitedBlock — check if it's a directive or value expression
            string blockContent = currentToken.Content;

            if (ExpressionParser.IsDirective(blockContent)) {
                string directiveKeyword = ExpressionParser.GetDirectiveKeyword(blockContent);

                // Check for closing directive
                if (directiveKeyword == closingKeyword) {
                    _tokenIndex++; // consume the closing directive
                    return nodes;
                }

                // Also check for elif/else which close the current if-branch
                if (closingKeyword == "/if" && (directiveKeyword == "elif" || directiveKeyword == "else")) {
                    // Don't consume — the if parser will handle it
                    return nodes;
                }

                // Parse the directive
                switch (directiveKeyword)
                {
                    case "each":
                        nodes.Add(ParseEachDirective(currentToken));
                        break;
                    case "if":
                        nodes.Add(ParseIfDirective(currentToken));
                        break;
                    case "define":
                        nodes.Add(ParseDefineDirective(currentToken));
                        break;
                    case "call":
                        nodes.Add(ParseCallDirective(currentToken));
                        break;
                    case "output":
                        nodes.Add(ParseOutputDirective(currentToken));
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Unexpected directive '{directiveKeyword}' at line {currentToken.Line}. " +
                            $"Expected: each, if, elif, else, define, call, output, or a closing directive"
                        );
                }
            } else {
                // Value expression
                var valueExpression = ExpressionParser.ParseValueExpression(blockContent);
                nodes.Add(new ValueExpressionNode
                {
                    Expression = valueExpression,
                    SourceLine = currentToken.Line,
                    SourceColumn = currentToken.Column
                });
                _tokenIndex++;
            }
        }

        if (closingKeyword != null) {
            throw new InvalidOperationException(
                $"Unexpected end of template — expected '{closingKeyword}' directive"
            );
        }

        return nodes;
    }

    /// <summary>
    /// Parse: each collection as item [separator="..."]
    /// or:   each map as key, value [separator="..."]
    /// </summary>
    private EachBlockNode ParseEachDirective(TemplateToken directiveToken)
    {
        _tokenIndex++; // consume the 'each' token
        string directiveBody = ExpressionParser.GetDirectiveBody(directiveToken.Content);

        // Parse: collection as item [separator="..."]
        // or:   collection as key, value [separator="..."]
        int asIndex = directiveBody.IndexOf(" as ", StringComparison.Ordinal);
        if (asIndex < 0) {
            throw new InvalidOperationException(
                $"'each' directive at line {directiveToken.Line} missing 'as' keyword. " +
                "Expected: each collection as item"
            );
        }

        string collectionText = directiveBody[..asIndex].Trim();
        string afterAsText = directiveBody[(asIndex + 4)..].Trim();

        // Check for separator="..."
        string? separatorText = null;
        int separatorIndex = afterAsText.IndexOf(" separator=", StringComparison.Ordinal);
        if (separatorIndex >= 0) {
            string separatorSpec = afterAsText[(separatorIndex + 11)..].Trim();
            if (separatorSpec.StartsWith('"') && separatorSpec.EndsWith('"')) {
                separatorText = separatorSpec[1..^1];
            }
            afterAsText = afterAsText[..separatorIndex].Trim();
        }

        // Parse variable names: item or key, value
        string itemVariableName;
        string? valueVariableName = null;

        if (afterAsText.Contains(',')) {
            string[] variableParts = afterAsText.Split(',', 2);
            itemVariableName = variableParts[0].Trim();
            valueVariableName = variableParts[1].Trim();
        } else {
            itemVariableName = afterAsText.Trim();
        }

        var collectionExpression = ExpressionParser.ParseExpression(collectionText);
        var bodyNodes = ParseNodeList("/each");

        return new EachBlockNode
        {
            CollectionExpression = collectionExpression,
            ItemVariableName = itemVariableName,
            ValueVariableName = valueVariableName,
            SeparatorText = separatorText,
            BodyNodes = bodyNodes,
            SourceLine = directiveToken.Line,
            SourceColumn = directiveToken.Column
        };
    }

    /// <summary>
    /// Parse: if condition ... [elif condition ...] [else ...] /if
    /// </summary>
    private IfBlockNode ParseIfDirective(TemplateToken directiveToken)
    {
        _tokenIndex++; // consume the 'if' token
        string conditionText = ExpressionParser.GetDirectiveBody(directiveToken.Content);
        var condition = ExpressionParser.ParseExpression(conditionText);

        var branches = new List<ConditionalBranch>();
        List<TemplateNode>? elseBodyNodes = null;

        // Parse the if-branch body (stops at elif, else, or /if)
        var ifBodyNodes = ParseNodeList("/if");
        branches.Add(new ConditionalBranch
        {
            Condition = condition,
            BodyNodes = ifBodyNodes
        });

        // Check for elif/else chains
        while (_tokenIndex < _tokens.Count)
        {
            var nextToken = _tokens[_tokenIndex];
            if (nextToken.Kind != TemplateTokenKind.DelimitedBlock) break;

            string nextKeyword = ExpressionParser.GetDirectiveKeyword(nextToken.Content);

            if (nextKeyword == "elif") {
                _tokenIndex++; // consume elif
                string elifConditionText = ExpressionParser.GetDirectiveBody(nextToken.Content);
                var elifCondition = ExpressionParser.ParseExpression(elifConditionText);
                var elifBodyNodes = ParseNodeList("/if");
                branches.Add(new ConditionalBranch
                {
                    Condition = elifCondition,
                    BodyNodes = elifBodyNodes
                });
            } else if (nextKeyword == "else") {
                _tokenIndex++; // consume else
                elseBodyNodes = ParseNodeList("/if");
                break; // else is always last
            } else {
                break;
            }
        }

        return new IfBlockNode
        {
            Branches = branches,
            ElseBodyNodes = elseBodyNodes,
            SourceLine = directiveToken.Line,
            SourceColumn = directiveToken.Column
        };
    }

    /// <summary>
    /// Parse: define name(args) ... /define
    /// </summary>
    private DefineBlockNode ParseDefineDirective(TemplateToken directiveToken)
    {
        _tokenIndex++; // consume the 'define' token
        string directiveBody = ExpressionParser.GetDirectiveBody(directiveToken.Content);

        // Parse: name(arg1, arg2, ...)
        int parenOpenIndex = directiveBody.IndexOf('(');
        if (parenOpenIndex < 0) {
            throw new InvalidOperationException(
                $"'define' directive at line {directiveToken.Line} missing parameter list. " +
                "Expected: define name(args)"
            );
        }

        string macroName = directiveBody[..parenOpenIndex].Trim();
        int parenCloseIndex = directiveBody.IndexOf(')', parenOpenIndex);
        if (parenCloseIndex < 0) {
            throw new InvalidOperationException(
                $"'define' directive at line {directiveToken.Line} missing closing ')'"
            );
        }

        string paramsText = directiveBody[(parenOpenIndex + 1)..parenCloseIndex];
        var parameterNames = paramsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var bodyNodes = ParseNodeList("/define");

        return new DefineBlockNode
        {
            MacroName = macroName,
            ParameterNames = parameterNames,
            BodyNodes = bodyNodes,
            SourceLine = directiveToken.Line,
            SourceColumn = directiveToken.Column
        };
    }

    /// <summary>
    /// Parse: call name(args)
    /// </summary>
    private CallNode ParseCallDirective(TemplateToken directiveToken)
    {
        _tokenIndex++; // consume the 'call' token
        string directiveBody = ExpressionParser.GetDirectiveBody(directiveToken.Content);

        // Parse: name(arg1, arg2, ...)
        int parenOpenIndex = directiveBody.IndexOf('(');
        if (parenOpenIndex < 0) {
            throw new InvalidOperationException(
                $"'call' directive at line {directiveToken.Line} missing argument list. " +
                "Expected: call name(args)"
            );
        }

        string macroName = directiveBody[..parenOpenIndex].Trim();
        int parenCloseIndex = directiveBody.LastIndexOf(')');
        if (parenCloseIndex < 0) {
            throw new InvalidOperationException(
                $"'call' directive at line {directiveToken.Line} missing closing ')'"
            );
        }

        string argsText = directiveBody[(parenOpenIndex + 1)..parenCloseIndex];
        var argumentExpressions = new List<TemplateExpression>();
        if (!string.IsNullOrWhiteSpace(argsText)) {
            foreach (string argText in argsText.Split(',')) {
                argumentExpressions.Add(ExpressionParser.ParseExpression(argText.Trim()));
            }
        }

        return new CallNode
        {
            MacroName = macroName,
            ArgumentExpressions = argumentExpressions,
            SourceLine = directiveToken.Line,
            SourceColumn = directiveToken.Column
        };
    }

    /// <summary>
    /// Parse: output expr ... /output
    /// </summary>
    private OutputBlockNode ParseOutputDirective(TemplateToken directiveToken)
    {
        _tokenIndex++; // consume the 'output' token
        string directiveBody = ExpressionParser.GetDirectiveBody(directiveToken.Content);
        var fileNameExpression = ExpressionParser.ParseExpression(directiveBody);

        var bodyNodes = ParseNodeList("/output");

        return new OutputBlockNode
        {
            FileNameExpression = fileNameExpression,
            BodyNodes = bodyNodes,
            SourceLine = directiveToken.Line,
            SourceColumn = directiveToken.Column
        };
    }
}