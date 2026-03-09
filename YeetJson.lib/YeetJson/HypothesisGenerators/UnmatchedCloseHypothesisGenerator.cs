namespace YeetJson.HypothesisGenerators;

/// <summary>
/// Generates repair hypotheses for unmatched closing delimiters
/// (e.g., } found with no { on the stack)
/// </summary>
public static class UnmatchedCloseHypothesisGenerator
{
    public static List<RepairHypothesis> Generate(List<Delimiter> allDelimiters, Delimiter unmatchedCloser)
    {
        var repairHypotheses = new List<RepairHypothesis>();

        // Hypothesis 1: Delete the unmatched closer (most common fix)
        repairHypotheses.Add(new RepairHypothesis(
            RepairAction.Delete,
            unmatchedCloser.Line, unmatchedCloser.Column,
            $"Delete spurious '{GetCloserChar(unmatchedCloser.Type)}' at line {unmatchedCloser.Line} col {unmatchedCloser.Column}",
            0.7f
        ));

        // Hypothesis 2: Find potential missing opener locations
        var potentialOpenerLocations = FindPotentialOpenerLocations(allDelimiters, unmatchedCloser);

        foreach (var (insertLine, insertColumn, description) in potentialOpenerLocations)
        {
            repairHypotheses.Add(new RepairHypothesis(
                RepairAction.Insert,
                insertLine, insertColumn,
                $"Insert missing '{GetOpenerChar(unmatchedCloser.Type)}' {description}",
                0.5f,
                GetOpenerChar(unmatchedCloser.Type)
            ));
        }

        // Hypothesis 3: Wrong closer type - maybe should be different delimiter
        foreach (var alternativeType in GetAlternativeDelimiterTypes(unmatchedCloser.Type))
        {
            // Check if there's an unmatched opener of that type before this closer
            var potentialMatchingOpener = allDelimiters
                .Where(d => d.IsOpen &&
                           d.Type == alternativeType &&
                           d.Offset < unmatchedCloser.Offset)
                .LastOrDefault();

            if (potentialMatchingOpener != null)
            {
                repairHypotheses.Add(new RepairHypothesis(
                    RepairAction.Replace,
                    unmatchedCloser.Line, unmatchedCloser.Column,
                    $"Replace '{GetCloserChar(unmatchedCloser.Type)}' with '{GetCloserChar(alternativeType)}' to match opener at line {potentialMatchingOpener.Line}",
                    0.4f,
                    GetCloserChar(alternativeType),
                    alternativeType
                ));
            }
        }

        return repairHypotheses.OrderByDescending(h => h.Confidence).ToList();
    }

    private static List<(int Line, int Column, string Description)> FindPotentialOpenerLocations(
        List<Delimiter> allDelimiters,
        Delimiter unmatchedCloser)
    {
        var locations = new List<(int, int, string)>();

        // Start of file
        locations.Add((1, 1, "at start of file"));

        // After the last matched opening delimiter of the same type before this closer
        var lastMatchedOpener = allDelimiters
            .Where(d => d.IsOpen &&
                       d.Type == unmatchedCloser.Type &&
                       d.Offset < unmatchedCloser.Offset)
            .LastOrDefault();

        if (lastMatchedOpener != null)
        {
            locations.Add((lastMatchedOpener.Line, lastMatchedOpener.Column + 1,
                $"after opener at line {lastMatchedOpener.Line}"));
        }

        // Before any sibling content at same nesting level
        var previousCloser = allDelimiters
            .Where(d => !d.IsOpen &&
                       d.Type == unmatchedCloser.Type &&
                       d.Offset < unmatchedCloser.Offset)
            .LastOrDefault();

        if (previousCloser != null)
        {
            locations.Add((previousCloser.Line, previousCloser.Column + 1,
                $"after previous close at line {previousCloser.Line}"));
        }

        return locations;
    }

    private static IEnumerable<DelimiterType> GetAlternativeDelimiterTypes(DelimiterType currentType)
    {
        // Return other bracket-like types that might be confused
        return currentType switch
        {
            DelimiterType.Brace => new[] { DelimiterType.Bracket, DelimiterType.Paren },
            DelimiterType.Bracket => new[] { DelimiterType.Brace, DelimiterType.Paren },
            DelimiterType.Paren => new[] { DelimiterType.Brace, DelimiterType.Bracket },
            _ => Enumerable.Empty<DelimiterType>()
        };
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