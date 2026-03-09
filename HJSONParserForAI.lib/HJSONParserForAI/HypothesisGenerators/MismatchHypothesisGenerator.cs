namespace HJSONParserForAI.HypothesisGenerators;

using HJSONParserForAI.Core;

/// <summary>
/// Generates repair hypotheses for mismatched delimiter pairs
/// (e.g., { opened but ] found for close)
/// </summary>
public static class MismatchHypothesisGenerator
{
    public static List<RepairHypothesis> Generate(Delimiter opener, Delimiter mismatchedCloser)
    {
        var repairHypotheses = new List<RepairHypothesis>();

        // Hypothesis 1: The closer is wrong type - replace it with correct closer
        repairHypotheses.Add(new RepairHypothesis(
            RepairAction.Replace,
            mismatchedCloser.Line, mismatchedCloser.Column,
            $"Replace '{GetCloserChar(mismatchedCloser.Type)}' with '{GetCloserChar(opener.Type)}' at line {mismatchedCloser.Line}",
            0.7f,
            GetCloserChar(opener.Type),
            opener.Type
        ));

        // Hypothesis 2: Missing closer before the mismatched one - insert correct closer
        repairHypotheses.Add(new RepairHypothesis(
            RepairAction.Insert,
            mismatchedCloser.Line, mismatchedCloser.Column,
            $"Insert '{GetCloserChar(opener.Type)}' before line {mismatchedCloser.Line} col {mismatchedCloser.Column}",
            0.6f,
            GetCloserChar(opener.Type)
        ));

        // Hypothesis 3: The opener is wrong type - it should have been different
        repairHypotheses.Add(new RepairHypothesis(
            RepairAction.Replace,
            opener.Line, opener.Column,
            $"Replace '{GetOpenerChar(opener.Type)}' with '{GetOpenerChar(mismatchedCloser.Type)}' at line {opener.Line}",
            0.4f,
            GetOpenerChar(mismatchedCloser.Type),
            mismatchedCloser.Type
        ));

        // Hypothesis 4: Delete the mismatched closer (it's spurious)
        repairHypotheses.Add(new RepairHypothesis(
            RepairAction.Delete,
            mismatchedCloser.Line, mismatchedCloser.Column,
            $"Delete spurious '{GetCloserChar(mismatchedCloser.Type)}' at line {mismatchedCloser.Line}",
            0.3f
        ));

        // Hypothesis 5: Delete the opener (it's spurious)
        repairHypotheses.Add(new RepairHypothesis(
            RepairAction.Delete,
            opener.Line, opener.Column,
            $"Delete spurious '{GetOpenerChar(opener.Type)}' at line {opener.Line}",
            0.2f
        ));

        return repairHypotheses.OrderByDescending(h => h.Confidence).ToList();
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
            DelimiterType.TripleQuote => "'''",
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
            DelimiterType.TripleQuote => "'''",
            _ => "?"
        };
    }
}