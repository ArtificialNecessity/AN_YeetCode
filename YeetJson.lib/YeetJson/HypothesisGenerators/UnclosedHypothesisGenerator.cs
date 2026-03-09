namespace YeetJson.HypothesisGenerators;

using System.Text.RegularExpressions;

/// <summary>
/// Generates repair hypotheses for unclosed delimiters
/// </summary>
public static class UnclosedHypothesisGenerator
{
    public static List<RepairHypothesis> Generate(
        string sourceText,
        List<Delimiter> allDelimiters,
        Delimiter unclosedOpener)
    {
        var repairHypotheses = new List<RepairHypothesis>();
        var sourceLines = sourceText.Split('\n');

        // Hypothesis 1: Insert at end of file
        repairHypotheses.Add(new RepairHypothesis(
            RepairAction.Insert,
            sourceLines.Length, 0,
            $"Insert '{GetCloserChar(unclosedOpener.Type)}' at end of file",
            0.3f,
            GetCloserChar(unclosedOpener.Type)
        ));

        // Hypothesis 2: Find sibling closers at similar indent
        int openerLineIndent = GetIndentSpaces(sourceLines[unclosedOpener.Line - 1]);
        var siblingClosers = FindSiblingClosers(allDelimiters, unclosedOpener, openerLineIndent);

        foreach (var siblingCloser in siblingClosers)
        {
            repairHypotheses.Add(new RepairHypothesis(
                RepairAction.Insert,
                siblingCloser.Line, siblingCloser.Column,
                $"Insert '{GetCloserChar(unclosedOpener.Type)}' before sibling close at line {siblingCloser.Line}",
                0.6f,
                GetCloserChar(unclosedOpener.Type)
            ));
        }

        // Hypothesis 3: Find structural break points (dedents, new keys)
        var structuralBreakPoints = FindStructuralBreakPoints(sourceLines, unclosedOpener, openerLineIndent);

        foreach (var (breakLine, breakColumn, breakDescription) in structuralBreakPoints)
        {
            repairHypotheses.Add(new RepairHypothesis(
                RepairAction.Insert,
                breakLine, breakColumn,
                $"Insert '{GetCloserChar(unclosedOpener.Type)}' before line {breakLine} ({breakDescription})",
                0.7f,
                GetCloserChar(unclosedOpener.Type)
            ));
        }

        // Hypothesis 4: Opening delimiter is spurious (delete it)
        repairHypotheses.Add(new RepairHypothesis(
            RepairAction.Delete,
            unclosedOpener.Line, unclosedOpener.Column,
            $"Delete spurious '{GetOpenerChar(unclosedOpener.Type)}' at line {unclosedOpener.Line}",
            0.2f
        ));

        return repairHypotheses.OrderByDescending(h => h.Confidence).ToList();
    }

    private static List<Delimiter> FindSiblingClosers(
        List<Delimiter> allDelimiters,
        Delimiter unclosedOpener,
        int openerIndentSpaces)
    {
        // Find closing delimiters after the opener that might be siblings
        // (at same or less indentation)
        return allDelimiters
            .Where(d => !d.IsOpen &&
                       d.Line > unclosedOpener.Line &&
                       d.Type == unclosedOpener.Type)
            .Take(3)
            .ToList();
    }

    private static List<(int Line, int Column, string Description)> FindStructuralBreakPoints(
        string[] sourceLines,
        Delimiter unclosedOpener,
        int openerIndentSpaces)
    {
        var breakPoints = new List<(int, int, string)>();

        for (int lineIndex = unclosedOpener.Line; lineIndex < sourceLines.Length; lineIndex++)
        {
            var lineText = sourceLines[lineIndex];
            var lineIndentSpaces = GetIndentSpaces(lineText);
            var trimmedLine = lineText.TrimStart();

            // Skip empty lines and comments
            if (string.IsNullOrWhiteSpace(trimmedLine) ||
                trimmedLine.StartsWith("//") ||
                trimmedLine.StartsWith("#"))
            {
                continue;
            }

            // Significant dedent indicates potential missing closer
            if (lineIndentSpaces < openerIndentSpaces)
            {
                breakPoints.Add((lineIndex + 1, 0, $"dedent from {openerIndentSpaces} to {lineIndentSpaces} spaces"));
            }

            // Looks like new top-level key (unindented key: value)
            if (lineIndentSpaces == 0 && Regex.IsMatch(trimmedLine, @"^[a-zA-Z_][a-zA-Z0-9_]*\s*[:{]"))
            {
                breakPoints.Add((lineIndex + 1, 0, "new top-level key"));
            }
        }

        return breakPoints;
    }

    private static int GetIndentSpaces(string lineText)
    {
        int spaceCount = 0;
        foreach (char c in lineText)
        {
            if (c == ' ') spaceCount++;
            else if (c == '\t') spaceCount += 2; // Treat tab as 2 spaces
            else break;
        }
        return spaceCount;
    }

    private static string GetOpenerChar(DelimiterType delimiterType)
    {
        return delimiterType switch
        {
            DelimiterType.Brace => "{",
            DelimiterType.Bracket => "[",
            DelimiterType.Paren => "(",
            DelimiterType.DoubleQuote => "\"",
            DelimiterType.SingleQuote => "'",
            DelimiterType.TripleDoubleQuote => "\"\"\"",
            _ => "?"
        };
    }

    private static string GetCloserChar(DelimiterType delimiterType)
    {
        return delimiterType switch
        {
            DelimiterType.Brace => "}",
            DelimiterType.Bracket => "]",
            DelimiterType.Paren => ")",
            DelimiterType.DoubleQuote => "\"",
            DelimiterType.SingleQuote => "'",
            DelimiterType.TripleDoubleQuote => "\"\"\"",
            _ => "?"
        };
    }
}