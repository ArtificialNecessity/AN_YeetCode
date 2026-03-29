namespace YeetJson;

using System.Text;

/// <summary>
/// Formats parse results as clean, readable diagnostic output.
/// Designed for both AI and human consumption — no markdown, no emoji, just clear structure.
/// </summary>
public class DiagnosticFormatter
{
    private const string ERROR_BANNER_START = "===[ YeetJson: Error Start ]=============================================";
    private const string ERROR_BANNER_END   = "===[ YeetJson: Error End ]===============================================";
    private const string SEPARATOR_LINE     = "-------------------------------------------------------------------------";

    private const string TEST_MODE_BANNER =
        "******************************************************************************\n" +
        "*  TEST MODE -- All errors below are INTENTIONAL test fixtures.              *\n" +
        "*  DO NOT attempt to fix them. The parser is working correctly.              *\n" +
        "******************************************************************************";

    public string FormatForAI(ParseResult parseResult, string sourceText, bool isTestMode = false, string? sourceFilePath = null)
    {
        var outputBuilder = new StringBuilder();
        var sourceLines = sourceText.Split('\n');
        string fileLabel = sourceFilePath ?? "(unknown)";

        int totalErrorCount = parseResult.StructuralErrors.Count + parseResult.SemanticErrors.Count;

        if (totalErrorCount == 0 && !isTestMode)
        {
            outputBuilder.AppendLine($"YeetJson: No errors detected in {fileLabel}");
            return outputBuilder.ToString();
        }

        if (isTestMode)
        {
            outputBuilder.AppendLine(TEST_MODE_BANNER);
            outputBuilder.AppendLine();
        }

        if (totalErrorCount == 0)
        {
            outputBuilder.AppendLine($"YeetJson: No errors detected in {fileLabel}");
            return outputBuilder.ToString();
        }

        // Structural errors (root causes)
        foreach (var structuralError in parseResult.StructuralErrors)
        {
            outputBuilder.AppendLine(ERROR_BANNER_START);
            outputBuilder.AppendLine($"  File:      {fileLabel}");
            outputBuilder.AppendLine($"  Location:  Line {structuralError.Line}, Column {structuralError.Column}");
            outputBuilder.AppendLine($"  Type:      {structuralError.Kind} (structural)");
            outputBuilder.AppendLine($"  Message:   {structuralError.Message}");
            outputBuilder.AppendLine();

            // Code context at error location
            AppendCodeContext(outputBuilder, sourceLines, structuralError.Line, 3);
            outputBuilder.AppendLine();

            // Repair hypotheses
            if (structuralError.Hypotheses.Count > 0)
            {
                outputBuilder.AppendLine("  Likely Fixes (ranked by confidence):");
                foreach (var hypothesis in structuralError.Hypotheses.Take(3))
                {
                    outputBuilder.AppendLine($"    - [{hypothesis.Confidence:P0}] {hypothesis.Description}");
                }
                outputBuilder.AppendLine();
            }

            // Show opener context if different from error line
            if (structuralError.Opener != null && structuralError.Opener.Line != structuralError.Line)
            {
                outputBuilder.AppendLine($"  Related opener at line {structuralError.Opener.Line}:");
                AppendCodeContext(outputBuilder, sourceLines, structuralError.Opener.Line, 2);
                outputBuilder.AppendLine();
            }

            outputBuilder.AppendLine(ERROR_BANNER_END);
            outputBuilder.AppendLine();
        }

        // Semantic errors
        foreach (var semanticError in parseResult.SemanticErrors)
        {
            outputBuilder.AppendLine(ERROR_BANNER_START);
            outputBuilder.AppendLine($"  File:      {fileLabel}");
            outputBuilder.AppendLine($"  Location:  Line {semanticError.Line}, Column {semanticError.Column}");
            outputBuilder.AppendLine($"  Type:      {semanticError.Kind} (semantic)");
            outputBuilder.AppendLine($"  Message:   {semanticError.Message}");

            if (semanticError.StructuralContext != null)
            {
                outputBuilder.AppendLine($"  Note:      {semanticError.Note}");
                outputBuilder.AppendLine("             Fix structural errors first; this may resolve automatically.");
            }

            outputBuilder.AppendLine(ERROR_BANNER_END);
            outputBuilder.AppendLine();
        }

        // Repair summary
        if (parseResult.StructuralErrors.Count > 0)
        {
            outputBuilder.AppendLine(SEPARATOR_LINE);
            outputBuilder.AppendLine("  REPAIR SUMMARY");
            outputBuilder.AppendLine(SEPARATOR_LINE);

            var primaryError = parseResult.StructuralErrors.First();
            var bestFix = primaryError.Hypotheses.FirstOrDefault();

            outputBuilder.AppendLine($"  File:             {fileLabel}");
            outputBuilder.AppendLine($"  Primary issue:    {primaryError.Message}");
            if (bestFix != null)
            {
                outputBuilder.AppendLine($"  Recommended fix:  {bestFix.Description}");
            }
            outputBuilder.AppendLine();
            outputBuilder.AppendLine("  After fixing structural errors, re-parse to check for remaining issues.");
            outputBuilder.AppendLine(SEPARATOR_LINE);
        }
        else if (parseResult.SemanticErrors.Count > 0)
        {
            outputBuilder.AppendLine(SEPARATOR_LINE);
            outputBuilder.AppendLine($"  Structure is valid. {parseResult.SemanticErrors.Count} semantic error(s) to fix in {fileLabel}.");
            outputBuilder.AppendLine(SEPARATOR_LINE);
        }

        return outputBuilder.ToString();
    }

    private static void AppendCodeContext(StringBuilder outputBuilder, string[] sourceLines, int targetLineNumber, int contextLineCount)
    {
        int startLineIndex = Math.Max(0, targetLineNumber - 1 - contextLineCount);
        int endLineIndex = Math.Min(sourceLines.Length - 1, targetLineNumber - 1 + contextLineCount);

        for (int lineIndex = startLineIndex; lineIndex <= endLineIndex; lineIndex++)
        {
            string lineMarker = (lineIndex == targetLineNumber - 1) ? ">>>" : "   ";
            int displayLineNumber = lineIndex + 1;
            outputBuilder.AppendLine($"  {lineMarker} {displayLineNumber,4} | {sourceLines[lineIndex]}");
        }
    }
}