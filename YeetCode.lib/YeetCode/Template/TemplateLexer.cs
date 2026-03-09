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

        return tokens;
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