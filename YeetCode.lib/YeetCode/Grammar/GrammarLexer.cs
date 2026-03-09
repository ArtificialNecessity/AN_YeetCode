using System.Text;
using System.Text.RegularExpressions;

namespace YeetCode.Grammar;

/// <summary>
/// Tokenizes .yeet grammar files into a stream of tokens.
/// Uses two-phase parsing: structural analysis first, then content tokenization.
/// </summary>
public class GrammarLexer
{
    private readonly string _grammarSourceText;
    private int _currentPosition;
    private int _currentLine;
    private int _currentColumn;

    public GrammarLexer(string grammarSourceText)
    {
        _grammarSourceText = grammarSourceText;
        _currentPosition = 0;
        _currentLine = 1;
        _currentColumn = 1;
    }

    /// <summary>
    /// Tokenize the entire grammar file into a list of tokens.
    /// </summary>
    public List<GrammarToken> Tokenize()
    {
        var allTokens = new List<GrammarToken>();

        while (_currentPosition < _grammarSourceText.Length)
        {
            SkipWhitespaceAndComments();

            if (_currentPosition >= _grammarSourceText.Length)
            {
                break;
            }

            var nextToken = ReadNextToken();
            if (nextToken != null)
            {
                allTokens.Add(nextToken);
            }
        }

        // Add EOF token
        allTokens.Add(new GrammarToken
        {
            Type = TokenType.EndOfFile,
            Text = "",
            Line = _currentLine,
            Column = _currentColumn
        });

        return allTokens;
    }

    private void SkipWhitespaceAndComments()
    {
        while (_currentPosition < _grammarSourceText.Length)
        {
            char currentChar = _grammarSourceText[_currentPosition];

            // Skip whitespace
            if (char.IsWhiteSpace(currentChar))
            {
                AdvancePosition(1);
                continue;
            }

            // Skip line comments: # or //
            if (currentChar == '#' ||
                (_currentPosition + 1 < _grammarSourceText.Length &&
                 _grammarSourceText.Substring(_currentPosition, 2) == "//"))
            {
                // Skip to end of line
                while (_currentPosition < _grammarSourceText.Length &&
                       _grammarSourceText[_currentPosition] != '\n')
                {
                    AdvancePosition(1);
                }
                continue;
            }

            // Skip block comments: /* ... */
            if (_currentPosition + 1 < _grammarSourceText.Length &&
                _grammarSourceText.Substring(_currentPosition, 2) == "/*")
            {
                AdvancePosition(2);
                while (_currentPosition + 1 < _grammarSourceText.Length)
                {
                    if (_grammarSourceText.Substring(_currentPosition, 2) == "*/")
                    {
                        AdvancePosition(2);
                        break;
                    }
                    AdvancePosition(1);
                }
                continue;
            }

            break;
        }
    }

    private GrammarToken? ReadNextToken()
    {
        int tokenStartLine = _currentLine;
        int tokenStartColumn = _currentColumn;
        char firstChar = _grammarSourceText[_currentPosition];

        // Three-character operator: ::=
        if (firstChar == ':' && _currentPosition + 2 < _grammarSourceText.Length &&
            _grammarSourceText.Substring(_currentPosition, 3) == "::=")
        {
            AdvancePosition(3);
            return new GrammarToken { Type = TokenType.DefinitionAssign, Text = "::=", Line = tokenStartLine, Column = tokenStartColumn };
        }

        // Single-character operators
        switch (firstChar)
        {
            case ':':
                AdvancePosition(1);
                return new GrammarToken { Type = TokenType.Colon, Text = ":", Line = tokenStartLine, Column = tokenStartColumn };
            case '|':
                AdvancePosition(1);
                return new GrammarToken { Type = TokenType.Pipe, Text = "|", Line = tokenStartLine, Column = tokenStartColumn };
            case '*':
                AdvancePosition(1);
                return new GrammarToken { Type = TokenType.Star, Text = "*", Line = tokenStartLine, Column = tokenStartColumn };
            case '+':
                AdvancePosition(1);
                return new GrammarToken { Type = TokenType.Plus, Text = "+", Line = tokenStartLine, Column = tokenStartColumn };
            case '?':
                AdvancePosition(1);
                return new GrammarToken { Type = TokenType.Question, Text = "?", Line = tokenStartLine, Column = tokenStartColumn };
            case '(':
                AdvancePosition(1);
                return new GrammarToken { Type = TokenType.LeftParen, Text = "(", Line = tokenStartLine, Column = tokenStartColumn };
            case ')':
                AdvancePosition(1);
                return new GrammarToken { Type = TokenType.RightParen, Text = ")", Line = tokenStartLine, Column = tokenStartColumn };
            case '{':
                AdvancePosition(1);
                return new GrammarToken { Type = TokenType.LeftBrace, Text = "{", Line = tokenStartLine, Column = tokenStartColumn };
            case '}':
                AdvancePosition(1);
                return new GrammarToken { Type = TokenType.RightBrace, Text = "}", Line = tokenStartLine, Column = tokenStartColumn };
            case '[':
                AdvancePosition(1);
                return new GrammarToken { Type = TokenType.LeftBracket, Text = "[", Line = tokenStartLine, Column = tokenStartColumn };
            case ']':
                AdvancePosition(1);
                return new GrammarToken { Type = TokenType.RightBracket, Text = "]", Line = tokenStartLine, Column = tokenStartColumn };
            case ',':
                AdvancePosition(1);
                return new GrammarToken { Type = TokenType.Comma, Text = ",", Line = tokenStartLine, Column = tokenStartColumn };
            case '.':
                AdvancePosition(1);
                return new GrammarToken { Type = TokenType.Dot, Text = ".", Line = tokenStartLine, Column = tokenStartColumn };
        }

        // Two-character operators: ->, ==, !=
        if (_currentPosition + 1 < _grammarSourceText.Length)
        {
            string twoCharSequence = _grammarSourceText.Substring(_currentPosition, 2);
            switch (twoCharSequence)
            {
                case "->":
                    AdvancePosition(2);
                    return new GrammarToken { Type = TokenType.Arrow, Text = "->", Line = tokenStartLine, Column = tokenStartColumn };
                case "==":
                    AdvancePosition(2);
                    return new GrammarToken { Type = TokenType.EqualsEquals, Text = "==", Line = tokenStartLine, Column = tokenStartColumn };
                case "!=":
                    AdvancePosition(2);
                    return new GrammarToken { Type = TokenType.NotEquals, Text = "!=", Line = tokenStartLine, Column = tokenStartColumn };
            }
        }

        // String literals: "text"
        if (firstChar == '"')
        {
            return ReadStringLiteral(tokenStartLine, tokenStartColumn);
        }

        // Regex patterns: /pattern/flags?
        if (firstChar == '/')
        {
            return ReadRegexPattern(tokenStartLine, tokenStartColumn);
        }

        // Identifiers, keywords, and directives
        if (char.IsLetter(firstChar) || firstChar == '_' || firstChar == '%' || firstChar == '@')
        {
            return ReadIdentifierOrKeyword(tokenStartLine, tokenStartColumn);
        }

        throw new GrammarLexerException(
            $"Unexpected character '{firstChar}' at line {_currentLine}, column {_currentColumn}");
    }

    private GrammarToken ReadStringLiteral(int startLine, int startColumn)
    {
        var literalBuilder = new StringBuilder();
        AdvancePosition(1); // Skip opening quote

        while (_currentPosition < _grammarSourceText.Length)
        {
            char currentChar = _grammarSourceText[_currentPosition];

            if (currentChar == '"')
            {
                AdvancePosition(1); // Skip closing quote
                return new GrammarToken
                {
                    Type = TokenType.StringLiteral,
                    Text = literalBuilder.ToString(),
                    Line = startLine,
                    Column = startColumn
                };
            }

            if (currentChar == '\\' && _currentPosition + 1 < _grammarSourceText.Length)
            {
                // Handle escape sequences
                AdvancePosition(1);
                char escapedChar = _grammarSourceText[_currentPosition];
                literalBuilder.Append(escapedChar switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    '\\' => '\\',
                    '"' => '"',
                    _ => escapedChar
                });
                AdvancePosition(1);
            }
            else
            {
                literalBuilder.Append(currentChar);
                AdvancePosition(1);
            }
        }

        throw new GrammarLexerException(
            $"Unterminated string literal starting at line {startLine}, column {startColumn}");
    }

    private GrammarToken ReadRegexPattern(int startLine, int startColumn)
    {
        var patternBuilder = new StringBuilder();
        AdvancePosition(1); // Skip opening /

        while (_currentPosition < _grammarSourceText.Length)
        {
            char currentChar = _grammarSourceText[_currentPosition];

            if (currentChar == '/')
            {
                AdvancePosition(1); // Skip closing /

                // Read optional flags (e.g., 's' for dotall)
                var flagsBuilder = new StringBuilder();
                while (_currentPosition < _grammarSourceText.Length &&
                       char.IsLetter(_grammarSourceText[_currentPosition]))
                {
                    flagsBuilder.Append(_grammarSourceText[_currentPosition]);
                    AdvancePosition(1);
                }

                return new GrammarToken
                {
                    Type = TokenType.RegexPattern,
                    Text = patternBuilder.ToString(),
                    RegexFlags = flagsBuilder.Length > 0 ? flagsBuilder.ToString() : null,
                    Line = startLine,
                    Column = startColumn
                };
            }

            if (currentChar == '\\' && _currentPosition + 1 < _grammarSourceText.Length)
            {
                // Preserve escape sequences in regex
                patternBuilder.Append(currentChar);
                AdvancePosition(1);
                patternBuilder.Append(_grammarSourceText[_currentPosition]);
                AdvancePosition(1);
            }
            else
            {
                patternBuilder.Append(currentChar);
                AdvancePosition(1);
            }
        }

        throw new GrammarLexerException(
            $"Unterminated regex pattern starting at line {startLine}, column {startColumn}");
    }

    private GrammarToken ReadIdentifierOrKeyword(int startLine, int startColumn)
    {
        var identifierBuilder = new StringBuilder();

        while (_currentPosition < _grammarSourceText.Length)
        {
            char currentChar = _grammarSourceText[_currentPosition];

            if (char.IsLetterOrDigit(currentChar) || currentChar == '_' || currentChar == '%' || currentChar == '@')
            {
                identifierBuilder.Append(currentChar);
                AdvancePosition(1);
            }
            else
            {
                break;
            }
        }

        string identifierText = identifierBuilder.ToString();

        // Check for directives (%skip, %define, %if, %else, %endif, %parse_file)
        if (identifierText.StartsWith('%'))
        {
            return new GrammarToken
            {
                Type = identifierText switch
                {
                    "%skip" => TokenType.DirectiveSkip,
                    "%define" => TokenType.DirectiveDefine,
                    "%if" => TokenType.DirectiveIf,
                    "%else" => TokenType.DirectiveElse,
                    "%endif" => TokenType.DirectiveEndif,
                    "%parse_file" => TokenType.DirectiveParseFile,
                    _ => TokenType.Identifier
                },
                Text = identifierText,
                Line = startLine,
                Column = startColumn
            };
        }

        // Check for type references (@TypeName)
        if (identifierText.StartsWith('@'))
        {
            return new GrammarToken
            {
                Type = TokenType.TypeReference,
                Text = identifierText,
                Line = startLine,
                Column = startColumn
            };
        }

        // Distinguish between UPPERCASE tokens and lowercase rules
        bool isUppercase = identifierText.Length > 0 && char.IsUpper(identifierText[0]);

        return new GrammarToken
        {
            Type = isUppercase ? TokenType.TokenName : TokenType.RuleName,
            Text = identifierText,
            Line = startLine,
            Column = startColumn
        };
    }

    private void AdvancePosition(int characterCount)
    {
        for (int i = 0; i < characterCount && _currentPosition < _grammarSourceText.Length; i++)
        {
            if (_grammarSourceText[_currentPosition] == '\n')
            {
                _currentLine++;
                _currentColumn = 1;
            }
            else
            {
                _currentColumn++;
            }
            _currentPosition++;
        }
    }
}

/// <summary>
/// A single token from the grammar lexer.
/// </summary>
public class GrammarToken
{
    public required TokenType Type { get; init; }
    public required string Text { get; init; }
    public string? RegexFlags { get; init; }  // For regex patterns only
    public required int Line { get; init; }
    public required int Column { get; init; }

    public override string ToString() => $"{Type}({Text}) at {Line}:{Column}";
}

/// <summary>
/// Token types for grammar lexer.
/// </summary>
public enum TokenType
{
    // Identifiers
    RuleName,           // lowercase_name
    TokenName,          // UPPERCASE_NAME
    TypeReference,      // @TypeName

    // Literals
    StringLiteral,      // "text"
    RegexPattern,       // /pattern/flags?

    // Operators
    Colon,              // :
    DefinitionAssign,   // ::=
    Arrow,              // ->
    Pipe,               // |
    Star,               // *
    Plus,               // +
    Question,           // ?
    Dot,                // .
    EqualsEquals,       // ==
    NotEquals,          // !=

    // Delimiters
    LeftParen,          // (
    RightParen,         // )
    LeftBrace,          // {
    RightBrace,         // }
    LeftBracket,        // [
    RightBracket,       // ]
    Comma,              // ,

    // Directives
    DirectiveSkip,      // %skip
    DirectiveDefine,    // %define
    DirectiveIf,        // %if
    DirectiveElse,      // %else
    DirectiveEndif,     // %endif
    DirectiveParseFile, // %parse_file

    // Special
    Identifier,         // Generic identifier (fallback)
    EndOfFile
}

/// <summary>
/// Exception thrown when the grammar lexer encounters an error.
/// </summary>
public class GrammarLexerException : Exception
{
    public GrammarLexerException(string message) : base(message) { }
}