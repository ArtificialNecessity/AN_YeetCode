namespace YeetCode.Template;

/// <summary>
/// Token types produced by the template lexer.
/// </summary>
public enum TemplateTokenKind
{
    LiteralText,      // Raw text outside delimiters
    DelimitedBlock,   // Content inside delimiters (directive or expression)
}

/// <summary>
/// A token produced by the template lexer.
/// </summary>
public class TemplateToken
{
    public required TemplateTokenKind Kind { get; init; }
    public required string Content { get; init; }
    public required int Line { get; init; }
    public required int Column { get; init; }
}

/// <summary>
/// Lexes a template file into literal text and delimited blocks.
///
/// 1. Parses the header: <?yt delim="OPEN CLOSE" ?>
/// 2. Splits remaining content by the declared delimiters
/// 3. Produces alternating LiteralText and DelimitedBlock tokens
///
/// The delimited block content is trimmed of leading/trailing whitespace.
/// </summary>
public class TemplateLexer
{
    public string OpenDelimiter { get; private set; } = "";
    public string CloseDelimiter { get; private set; } = "";
    /// <summary>
    /// Whether standalone directive lines are trimmed (default: true).
    /// Set to false with: <?yt delim="<% %>" trimlines=false ?>
    /// </summary>
    public bool TrimDirectiveLines { get; private set; } = true;

    /// <summary>
    /// Lex a template string into tokens.
    /// The first line must be the header: <?yt delim="OPEN CLOSE" ?>
    /// </summary>
    public List<TemplateToken> Lex(string templateSource)
    {
        // Parse header
        int headerEndPosition = ParseHeader(templateSource);

        // Lex body
        return LexBody(templateSource, headerEndPosition);
    }

    /// <summary>
    /// Parse the <?yt delim="OPEN CLOSE" ?> header.
    /// Returns the position after the header (start of template body).
    /// </summary>
    private int ParseHeader(string templateSource)
    {
        // Find <?yt ... ?>
        int headerOpenIndex = templateSource.IndexOf("<?yt", StringComparison.Ordinal);
        if (headerOpenIndex < 0) {
            throw new InvalidOperationException(
                "Template must start with a <?yt delim=\"OPEN CLOSE\" ?> header. " +
                "Example: <?yt delim=\"<% %>\" ?>"
            );
        }

        int headerCloseIndex = templateSource.IndexOf("?>", headerOpenIndex + 4, StringComparison.Ordinal);
        if (headerCloseIndex < 0) {
            throw new InvalidOperationException(
                "Template header <?yt ... is missing closing ?>. " +
                "Example: <?yt delim=\"<% %>\" ?>"
            );
        }

        string headerContent = templateSource[(headerOpenIndex + 4)..headerCloseIndex].Trim();

        // Parse delim="OPEN CLOSE"
        const string delimPrefix = "delim=\"";
        int delimStartIndex = headerContent.IndexOf(delimPrefix, StringComparison.Ordinal);
        if (delimStartIndex < 0) {
            throw new InvalidOperationException(
                "Template header must contain delim=\"OPEN CLOSE\". " +
                "Example: <?yt delim=\"<% %>\" ?>"
            );
        }

        int delimValueStartIndex = delimStartIndex + delimPrefix.Length;
        int delimValueEndIndex = headerContent.IndexOf('"', delimValueStartIndex);
        if (delimValueEndIndex < 0) {
            throw new InvalidOperationException(
                "Template header delim value is missing closing quote. " +
                "Example: <?yt delim=\"<% %>\" ?>"
            );
        }

        string delimPairText = headerContent[delimValueStartIndex..delimValueEndIndex];
        string[] delimParts = delimPairText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (delimParts.Length != 2) {
            throw new InvalidOperationException(
                $"Template header delim must have exactly two parts (open and close), " +
                $"got {delimParts.Length}: \"{delimPairText}\". " +
                "Example: <?yt delim=\"<% %>\" ?>"
            );
        }

        OpenDelimiter = delimParts[0];
        CloseDelimiter = delimParts[1];

        // Parse optional trimlines=true|false (default: true)
        const string trimlinesPrefix = "trimlines=";
        int trimlinesIndex = headerContent.IndexOf(trimlinesPrefix, StringComparison.Ordinal);
        if (trimlinesIndex >= 0)
        {
            string trimlinesValue = headerContent[(trimlinesIndex + trimlinesPrefix.Length)..].Trim();
            // Take just the first word (in case there are more attributes after)
            int trimlinesEndIndex = trimlinesValue.IndexOfAny([' ', '\t', '\r', '\n']);
            if (trimlinesEndIndex >= 0)
                trimlinesValue = trimlinesValue[..trimlinesEndIndex];

            TrimDirectiveLines = trimlinesValue.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        // Skip past ?> and any trailing newline
        int bodyStartPosition = headerCloseIndex + 2;
        if (bodyStartPosition < templateSource.Length && templateSource[bodyStartPosition] == '\n') {
            bodyStartPosition++;
        } else if (bodyStartPosition + 1 < templateSource.Length &&
                   templateSource[bodyStartPosition] == '\r' && templateSource[bodyStartPosition + 1] == '\n') {
            bodyStartPosition += 2;
        }

        return bodyStartPosition;
    }

    /// <summary>
    /// Lex the template body (after header) into tokens.
    /// </summary>
    private List<TemplateToken> LexBody(string templateSource, int startPosition)
    {
        var tokens = new List<TemplateToken>();
        int currentPosition = startPosition;
        int currentLine = CountNewlines(templateSource, 0, startPosition) + 1;
        int lastNewlineOffset = templateSource.LastIndexOf('\n', Math.Max(0, startPosition - 1));
        int currentColumn = startPosition - (lastNewlineOffset < 0 ? 0 : lastNewlineOffset + 1) + 1;

        while (currentPosition < templateSource.Length)
        {
            // Find next open delimiter
            int openDelimPosition = templateSource.IndexOf(OpenDelimiter, currentPosition, StringComparison.Ordinal);

            if (openDelimPosition < 0) {
                // No more delimiters — rest is literal text
                string remainingText = templateSource[currentPosition..];
                if (remainingText.Length > 0) {
                    tokens.Add(new TemplateToken
                    {
                        Kind = TemplateTokenKind.LiteralText,
                        Content = remainingText,
                        Line = currentLine,
                        Column = currentColumn
                    });
                }
                break;
            }

            // Emit literal text before the delimiter
            if (openDelimPosition > currentPosition) {
                string literalText = templateSource[currentPosition..openDelimPosition];
                tokens.Add(new TemplateToken
                {
                    Kind = TemplateTokenKind.LiteralText,
                    Content = literalText,
                    Line = currentLine,
                    Column = currentColumn
                });
                UpdateLineColumn(literalText, ref currentLine, ref currentColumn);
            }

            // Find matching close delimiter
            int blockContentStart = openDelimPosition + OpenDelimiter.Length;
            int closeDelimPosition = templateSource.IndexOf(CloseDelimiter, blockContentStart, StringComparison.Ordinal);

            if (closeDelimPosition < 0) {
                throw new InvalidOperationException(
                    $"Unclosed template delimiter '{OpenDelimiter}' at line {currentLine}. " +
                    $"Expected matching '{CloseDelimiter}'"
                );
            }

            // Extract and trim the block content
            string blockContent = templateSource[blockContentStart..closeDelimPosition].Trim();
            int blockLine = currentLine;
            int blockColumn = currentColumn;

            // Update line/column tracking past the open delimiter
            UpdateLineColumn(templateSource[currentPosition..blockContentStart], ref currentLine, ref currentColumn);

            tokens.Add(new TemplateToken
            {
                Kind = TemplateTokenKind.DelimitedBlock,
                Content = blockContent,
                Line = blockLine,
                Column = blockColumn
            });

            // Move past close delimiter
            currentPosition = closeDelimPosition + CloseDelimiter.Length;
            UpdateLineColumn(templateSource[blockContentStart..currentPosition], ref currentLine, ref currentColumn);
        }

        return TrimDirectiveLines ? TrimStandaloneDirectiveLines(tokens) : tokens;
    }

    /// <summary>
    /// Post-process tokens to trim whitespace-only lines that contain only control directives.
    /// When a directive like <% each ... %> or <% # comment %> is the only thing on a line
    /// (besides whitespace), the entire line including its newline is removed from output.
    /// This prevents blank lines from control-flow directives.
    /// </summary>
    private static List<TemplateToken> TrimStandaloneDirectiveLines(List<TemplateToken> tokens)
    {
        for (int tokenIndex = 0; tokenIndex < tokens.Count; tokenIndex++)
        {
            var currentToken = tokens[tokenIndex];
            if (currentToken.Kind != TemplateTokenKind.DelimitedBlock) continue;
            if (!IsControlDirective(currentToken.Content)) continue;

            // Check if this directive is standalone on its line
            bool hasOnlyWhitespaceBefore = CheckOnlyWhitespaceBefore(tokens, tokenIndex);
            bool hasNewlineOrEndAfter = CheckNewlineOrEndAfter(tokens, tokenIndex);

            if (!hasOnlyWhitespaceBefore || !hasNewlineOrEndAfter) continue;

            // Trim the preceding literal's trailing whitespace (after last \n)
            if (tokenIndex > 0 && tokens[tokenIndex - 1].Kind == TemplateTokenKind.LiteralText)
            {
                string precedingText = tokens[tokenIndex - 1].Content;
                int lastNewlineIndex = precedingText.LastIndexOf('\n');
                string trimmedContent = lastNewlineIndex >= 0
                    ? precedingText[..(lastNewlineIndex + 1)]  // keep up to and including the \n
                    : "";                                       // all whitespace at start of body
                tokens[tokenIndex - 1] = new TemplateToken
                {
                    Kind = TemplateTokenKind.LiteralText,
                    Content = trimmedContent,
                    Line = tokens[tokenIndex - 1].Line,
                    Column = tokens[tokenIndex - 1].Column
                };
            }

            // Trim the following literal's leading newline
            if (tokenIndex + 1 < tokens.Count && tokens[tokenIndex + 1].Kind == TemplateTokenKind.LiteralText)
            {
                string followingText = tokens[tokenIndex + 1].Content;
                if (followingText.StartsWith("\r\n"))
                    followingText = followingText[2..];
                else if (followingText.StartsWith('\n'))
                    followingText = followingText[1..];

                tokens[tokenIndex + 1] = new TemplateToken
                {
                    Kind = TemplateTokenKind.LiteralText,
                    Content = followingText,
                    Line = tokens[tokenIndex + 1].Line,
                    Column = tokens[tokenIndex + 1].Column
                };
            }
        }

        // Remove empty literal tokens created by trimming
        tokens.RemoveAll(emptyToken =>
            emptyToken.Kind == TemplateTokenKind.LiteralText && emptyToken.Content.Length == 0);

        return tokens;
    }

    /// <summary>
    /// Check if everything before this directive on its line is whitespace only.
    /// </summary>
    private static bool CheckOnlyWhitespaceBefore(List<TemplateToken> tokens, int directiveIndex)
    {
        if (directiveIndex == 0) return true; // start of body

        var precedingToken = tokens[directiveIndex - 1];
        if (precedingToken.Kind != TemplateTokenKind.LiteralText) return false;

        string precedingText = precedingToken.Content;
        int lastNewlineIndex = precedingText.LastIndexOf('\n');
        if (lastNewlineIndex >= 0)
        {
            string textAfterLastNewline = precedingText[(lastNewlineIndex + 1)..];
            return textAfterLastNewline.All(c => c == ' ' || c == '\t');
        }
        // No newline — entire text must be whitespace (start of body scenario)
        return precedingText.All(c => c == ' ' || c == '\t');
    }

    /// <summary>
    /// Check if the text after this directive starts with a newline (or is end of body).
    /// </summary>
    private static bool CheckNewlineOrEndAfter(List<TemplateToken> tokens, int directiveIndex)
    {
        if (directiveIndex >= tokens.Count - 1) return true; // end of body

        var followingToken = tokens[directiveIndex + 1];
        if (followingToken.Kind != TemplateTokenKind.LiteralText) return false;

        return followingToken.Content.StartsWith('\n') || followingToken.Content.StartsWith("\r\n");
    }

    /// <summary>
    /// Determine if a delimited block is a control directive (not a value expression).
    /// Control directives: each, if, elif, else, /each, /if, define, /define, call, output, /output, # comments.
    /// </summary>
    private static bool IsControlDirective(string blockContent)
    {
        if (blockContent.StartsWith('#')) return true;
        return ExpressionParser.IsDirective(blockContent);
    }

    private static int CountNewlines(string text, int startIndex, int endIndex)
    {
        int newlineCount = 0;
        for (int charIndex = startIndex; charIndex < endIndex && charIndex < text.Length; charIndex++) {
            if (text[charIndex] == '\n') newlineCount++;
        }
        return newlineCount;
    }

    private static void UpdateLineColumn(string text, ref int lineNumber, ref int columnNumber)
    {
        foreach (char textChar in text) {
            if (textChar == '\n') {
                lineNumber++;
                columnNumber = 1;
            } else {
                columnNumber++;
            }
        }
    }
}