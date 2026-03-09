namespace YeetCode.Template;

/// <summary>
/// Parses expression strings from inside template delimiters.
/// Handles: paths, bracket access, function calls, string literals,
/// concatenation (+), comparisons (==, !=), and @TypeRef.
/// </summary>
public static class ExpressionParser
{
    private static readonly HashSet<string> DirectiveKeywords = new(StringComparer.Ordinal)
    {
        "each", "if", "elif", "else", "define", "call", "output",
        "/each", "/if", "/define", "/output"
    };

    private static readonly HashSet<string> BuiltinFunctionNames = new(StringComparer.Ordinal)
    {
        "pascal", "camel", "snake", "upper", "lower", "length", "pascal_dotted"
    };

    /// <summary>
    /// Determine if a delimited block content string is a directive or a value expression.
    /// Returns true if it starts with a directive keyword.
    /// </summary>
    public static bool IsDirective(string blockContent)
    {
        string firstToken = GetFirstToken(blockContent);
        return DirectiveKeywords.Contains(firstToken);
    }

    /// <summary>
    /// Get the directive keyword from a block content string.
    /// </summary>
    public static string GetDirectiveKeyword(string blockContent)
    {
        return GetFirstToken(blockContent);
    }

    /// <summary>
    /// Get the content after the directive keyword.
    /// </summary>
    public static string GetDirectiveBody(string blockContent)
    {
        string firstToken = GetFirstToken(blockContent);
        return blockContent[firstToken.Length..].TrimStart();
    }

    /// <summary>
    /// Parse a full expression from a string.
    /// Handles concatenation (+) and comparisons (==, !=) at the top level.
    /// </summary>
    public static TemplateExpression ParseExpression(string expressionText)
    {
        expressionText = expressionText.Trim();

        // Check for comparison operators (lowest precedence)
        int comparisonOperatorIndex = FindTopLevelOperator(expressionText, "==", "!=");
        if (comparisonOperatorIndex >= 0) {
            string operatorText = expressionText.Substring(comparisonOperatorIndex, 2);
            string leftText = expressionText[..comparisonOperatorIndex].TrimEnd();
            string rightText = expressionText[(comparisonOperatorIndex + 2)..].TrimStart();
            return new ComparisonExpression
            {
                LeftExpression = ParseConcatExpression(leftText),
                Operator = operatorText,
                RightExpression = ParseConcatExpression(rightText)
            };
        }

        return ParseConcatExpression(expressionText);
    }

    /// <summary>
    /// Parse a concatenation expression (expr + expr + ...).
    /// </summary>
    private static TemplateExpression ParseConcatExpression(string expressionText)
    {
        expressionText = expressionText.Trim();

        int concatOperatorIndex = FindTopLevelOperator(expressionText, "+");
        if (concatOperatorIndex >= 0) {
            string leftText = expressionText[..concatOperatorIndex].TrimEnd();
            string rightText = expressionText[(concatOperatorIndex + 1)..].TrimStart();
            return new ConcatExpression
            {
                LeftExpression = ParseConcatExpression(leftText),
                RightExpression = ParseConcatExpression(rightText)
            };
        }

        return ParsePrimaryExpression(expressionText);
    }

    /// <summary>
    /// Parse a primary expression: path, function call, bracket access, string literal, @TypeRef.
    /// </summary>
    private static TemplateExpression ParsePrimaryExpression(string expressionText)
    {
        expressionText = expressionText.Trim();

        if (expressionText.Length == 0) {
            throw new InvalidOperationException("Empty expression in template");
        }

        // String literal: "..."
        if (expressionText.StartsWith('"') && expressionText.EndsWith('"')) {
            return new StringLiteralExpression
            {
                Value = expressionText[1..^1]
            };
        }

        // @TypeRef
        if (expressionText.StartsWith('@')) {
            return new TypeRefExpression
            {
                TypeName = expressionText[1..]
            };
        }

        // Check for function call: name(args) or name expr (built-in shorthand)
        // But first check for bracket access: path[expr]
        int bracketIndex = FindTopLevelChar(expressionText, '[');
        int parenIndex = FindTopLevelChar(expressionText, '(');

        // Function call with parens: name(args)
        if (parenIndex >= 0 && (bracketIndex < 0 || parenIndex < bracketIndex)) {
            string functionName = expressionText[..parenIndex].Trim();
            int closeParenIndex = FindMatchingClose(expressionText, parenIndex, '(', ')');
            string argsText = expressionText[(parenIndex + 1)..closeParenIndex];

            var argumentExpressions = ParseArgumentList(argsText);

            // Check if there's bracket access after the function call
            if (closeParenIndex + 1 < expressionText.Length && expressionText[closeParenIndex + 1] == '[') {
                var funcExpr = new FunctionCallExpression
                {
                    FunctionName = functionName,
                    ArgumentExpressions = argumentExpressions
                };
                return ParseBracketChain(funcExpr, expressionText, closeParenIndex + 1);
            }

            return new FunctionCallExpression
            {
                FunctionName = functionName,
                ArgumentExpressions = argumentExpressions
            };
        }

        // Bracket access: path[expr]
        if (bracketIndex >= 0) {
            string basePath = expressionText[..bracketIndex].Trim();
            var baseExpression = ParsePathExpression(basePath);
            return ParseBracketChain(baseExpression, expressionText, bracketIndex);
        }

        // Plain path: msg.name, package, msg.fields.length
        return ParsePathExpression(expressionText);
    }

    /// <summary>
    /// Parse a value expression that may be preceded by a built-in function name.
    /// e.g. "pascal msg.name" → FunctionCallExpression(pascal, [PathExpression(msg.name)])
    /// e.g. "msg.name" → PathExpression(msg.name)
    /// </summary>
    public static TemplateExpression ParseValueExpression(string expressionText)
    {
        expressionText = expressionText.Trim();

        // Check if the first token is a built-in function name (space-separated shorthand)
        string firstToken = GetFirstToken(expressionText);
        if (BuiltinFunctionNames.Contains(firstToken) && expressionText.Length > firstToken.Length) {
            string argumentText = expressionText[(firstToken.Length + 1)..].Trim();
            return new FunctionCallExpression
            {
                FunctionName = firstToken,
                ArgumentExpressions = new List<TemplateExpression> { ParseExpression(argumentText) }
            };
        }

        return ParseExpression(expressionText);
    }

    /// <summary>
    /// Parse a dot-separated path: msg.name, msg.fields.length
    /// Handles ?. optional access.
    /// </summary>
    private static TemplateExpression ParsePathExpression(string pathText)
    {
        pathText = pathText.Trim();
        bool hasOptionalAccess = pathText.Contains("?.");

        // Split on . but handle ?. by removing the ?
        string normalizedPath = pathText.Replace("?.", ".");
        string[] segments = normalizedPath.Split('.');

        return new PathExpression
        {
            PathSegments = new List<string>(segments),
            HasOptionalAccess = hasOptionalAccess
        };
    }

    /// <summary>
    /// Parse bracket access chain: base[expr1][expr2]...
    /// </summary>
    private static TemplateExpression ParseBracketChain(
        TemplateExpression baseExpression, string fullText, int bracketStartIndex)
    {
        int currentIndex = bracketStartIndex;
        var currentExpression = baseExpression;

        while (currentIndex < fullText.Length && fullText[currentIndex] == '[') {
            int closeBracketIndex = FindMatchingClose(fullText, currentIndex, '[', ']');
            string indexText = fullText[(currentIndex + 1)..closeBracketIndex].Trim();

            currentExpression = new BracketAccessExpression
            {
                BaseExpression = currentExpression,
                IndexExpression = ParseExpression(indexText)
            };

            currentIndex = closeBracketIndex + 1;
        }

        return currentExpression;
    }

    /// <summary>
    /// Parse a comma-separated argument list.
    /// </summary>
    private static List<TemplateExpression> ParseArgumentList(string argsText)
    {
        var argumentExpressions = new List<TemplateExpression>();
        if (string.IsNullOrWhiteSpace(argsText)) return argumentExpressions;

        // Split by commas at the top level (not inside brackets or parens)
        var argStrings = SplitTopLevel(argsText, ',');
        foreach (string argString in argStrings) {
            argumentExpressions.Add(ParseExpression(argString.Trim()));
        }

        return argumentExpressions;
    }

    // ── Helper methods ───────────────────────────────────────

    private static string GetFirstToken(string text)
    {
        text = text.TrimStart();
        int tokenEndIndex = 0;
        while (tokenEndIndex < text.Length && !char.IsWhiteSpace(text[tokenEndIndex]) &&
               text[tokenEndIndex] != '(' && text[tokenEndIndex] != '[') {
            // Also stop at / for close directives like /each
            if (tokenEndIndex > 0 && text[tokenEndIndex] == '/') break;
            tokenEndIndex++;
        }
        // Special case: /keyword starts with /
        if (text.Length > 0 && text[0] == '/') {
            tokenEndIndex = 1;
            while (tokenEndIndex < text.Length && char.IsLetterOrDigit(text[tokenEndIndex])) {
                tokenEndIndex++;
            }
        }
        return text[..tokenEndIndex];
    }

    private static int FindTopLevelOperator(string text, params string[] operators)
    {
        int nestingDepth = 0;
        bool insideString = false;

        for (int charIndex = 0; charIndex < text.Length; charIndex++) {
            char currentChar = text[charIndex];

            if (currentChar == '"') {
                insideString = !insideString;
                continue;
            }
            if (insideString) continue;

            if (currentChar == '(' || currentChar == '[') { nestingDepth++; continue; }
            if (currentChar == ')' || currentChar == ']') { nestingDepth--; continue; }

            if (nestingDepth > 0) continue;

            foreach (string operatorText in operators) {
                if (charIndex + operatorText.Length <= text.Length &&
                    text.Substring(charIndex, operatorText.Length) == operatorText) {
                    // For single-char operators like +, make sure it's not inside a longer token
                    if (operatorText.Length == 1) {
                        // Don't match + inside ++ or similar
                        return charIndex;
                    }
                    return charIndex;
                }
            }
        }

        return -1;
    }

    private static int FindTopLevelChar(string text, char targetChar)
    {
        int nestingDepth = 0;
        bool insideString = false;

        for (int charIndex = 0; charIndex < text.Length; charIndex++) {
            char currentChar = text[charIndex];

            if (currentChar == '"') {
                insideString = !insideString;
                continue;
            }
            if (insideString) continue;

            if (currentChar == '(' || currentChar == '[') {
                if (currentChar == targetChar && nestingDepth == 0) return charIndex;
                nestingDepth++;
                continue;
            }
            if (currentChar == ')' || currentChar == ']') { nestingDepth--; continue; }

            if (nestingDepth == 0 && currentChar == targetChar) return charIndex;
        }

        return -1;
    }

    private static int FindMatchingClose(string text, int openIndex, char openChar, char closeChar)
    {
        int nestingDepth = 1;
        for (int charIndex = openIndex + 1; charIndex < text.Length; charIndex++) {
            if (text[charIndex] == openChar) nestingDepth++;
            else if (text[charIndex] == closeChar) {
                nestingDepth--;
                if (nestingDepth == 0) return charIndex;
            }
        }
        throw new InvalidOperationException(
            $"Unmatched '{openChar}' at position {openIndex} in expression: {text}"
        );
    }

    private static List<string> SplitTopLevel(string text, char separator)
    {
        var parts = new List<string>();
        int nestingDepth = 0;
        bool insideString = false;
        int segmentStart = 0;

        for (int charIndex = 0; charIndex < text.Length; charIndex++) {
            char currentChar = text[charIndex];

            if (currentChar == '"') { insideString = !insideString; continue; }
            if (insideString) continue;

            if (currentChar == '(' || currentChar == '[') { nestingDepth++; continue; }
            if (currentChar == ')' || currentChar == ']') { nestingDepth--; continue; }

            if (nestingDepth == 0 && currentChar == separator) {
                parts.Add(text[segmentStart..charIndex]);
                segmentStart = charIndex + 1;
            }
        }

        parts.Add(text[segmentStart..]);
        return parts;
    }
}