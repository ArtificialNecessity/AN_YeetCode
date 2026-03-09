namespace HJSONParserForAI.Core;

using System.Text;

/// <summary>
/// Formats parse results as AI-friendly diagnostic output.
/// Supports --testMode which adds clear banners warning that errors are intentional test fixtures.
/// </summary>
public class DiagnosticFormatter
{
    private const string TEST_MODE_BANNER = @"
╔══════════════════════════════════════════════════════════════════════════════╗
║  ⚠️  THIS IS A TEST MODE ERROR - DO NOT FIX THIS ERROR!                      ║
║      This error is INTENTIONALLY present in the test file for validation.   ║
║      The parser is working correctly by detecting this expected error.       ║
╚══════════════════════════════════════════════════════════════════════════════╝";

    private const string TEST_MODE_HEADER = @"
╔══════════════════════════════════════════════════════════════════════════════╗
║                         🧪 TEST MODE ACTIVE 🧪                               ║
║  All errors below are INTENTIONAL test fixtures. DO NOT attempt to fix them.║
║  These errors validate that the parser correctly detects structural issues.  ║
╚══════════════════════════════════════════════════════════════════════════════╝
";

    public string FormatForAI(ParseResult parseResult, string sourceText, bool isTestMode = false)
    {
        var outputBuilder = new StringBuilder();
        var sourceLines = sourceText.Split('\n');

        outputBuilder.AppendLine("# HJSON PARSE DIAGNOSTICS");
        outputBuilder.AppendLine();

        // Add test mode header if active
        if (isTestMode)
        {
            outputBuilder.AppendLine(TEST_MODE_HEADER);
        }

        // Structural errors first (these are usually root causes)
        if (parseResult.StructuralErrors.Any())
        {
            outputBuilder.AppendLine("## STRUCTURAL ERRORS (likely root causes)");
            outputBuilder.AppendLine();

            foreach (var structuralError in parseResult.StructuralErrors)
            {
                // Add test mode banner before each error if in test mode
                if (isTestMode)
                {
                    outputBuilder.AppendLine(TEST_MODE_BANNER);
                    outputBuilder.AppendLine();
                }

                outputBuilder.AppendLine($"### {structuralError.Kind}");
                outputBuilder.AppendLine($"**Location:** Line {structuralError.Line}, Column {structuralError.Column}");
                outputBuilder.AppendLine($"**Message:** {structuralError.Message}");
                outputBuilder.AppendLine();

                // Code context at error location
                outputBuilder.AppendLine("**Context:**");
                outputBuilder.AppendLine("```hjson");
                AppendCodeContext(outputBuilder, sourceLines, structuralError.Line, 3);
                outputBuilder.AppendLine("```");
                outputBuilder.AppendLine();

                // Repair hypotheses
                if (structuralError.Hypotheses.Any())
                {
                    outputBuilder.AppendLine("**Likely fixes (ranked by confidence):**");
                    foreach (var hypothesis in structuralError.Hypotheses.Take(3))
                    {
                        outputBuilder.AppendLine($"- [{hypothesis.Confidence:P0}] {hypothesis.Description}");
                    }
                    outputBuilder.AppendLine();
                }

                // Show opener context if different from error line
                if (structuralError.Opener != null && structuralError.Opener.Line != structuralError.Line)
                {
                    outputBuilder.AppendLine($"**Related opener at line {structuralError.Opener.Line}:**");
                    outputBuilder.AppendLine("```hjson");
                    AppendCodeContext(outputBuilder, sourceLines, structuralError.Opener.Line, 2);
                    outputBuilder.AppendLine("```");
                    outputBuilder.AppendLine();
                }
            }
        }

        // Semantic errors
        if (parseResult.SemanticErrors.Any())
        {
            outputBuilder.AppendLine("## SEMANTIC ERRORS");
            outputBuilder.AppendLine();

            foreach (var semanticError in parseResult.SemanticErrors)
            {
                // Add test mode banner before each error if in test mode
                if (isTestMode)
                {
                    outputBuilder.AppendLine(TEST_MODE_BANNER);
                    outputBuilder.AppendLine();
                }

                outputBuilder.AppendLine($"### {semanticError.Kind} at line {semanticError.Line}");
                outputBuilder.AppendLine($"**Message:** {semanticError.Message}");

                if (semanticError.StructuralContext != null)
                {
                    outputBuilder.AppendLine($"**⚠️ Note:** {semanticError.Note}");
                    outputBuilder.AppendLine("Fix structural errors first; this may resolve automatically.");
                }

                outputBuilder.AppendLine();
            }
        }

        // Summary section
        outputBuilder.AppendLine("## SUMMARY FOR REPAIR");
        outputBuilder.AppendLine();

        if (isTestMode)
        {
            outputBuilder.AppendLine("**🧪 TEST MODE:** Errors above are intentional test fixtures.");
            outputBuilder.AppendLine();
        }

        if (parseResult.StructuralErrors.Any())
        {
            var primaryError = parseResult.StructuralErrors.First();
            var bestFix = primaryError.Hypotheses.FirstOrDefault();

            outputBuilder.AppendLine($"**Primary issue:** {primaryError.Message}");
            if (bestFix != null)
            {
                outputBuilder.AppendLine($"**Recommended fix:** {bestFix.Description}");
            }
            outputBuilder.AppendLine();
            outputBuilder.AppendLine("After fixing structural errors, re-parse to check for remaining issues.");
        }
        else if (parseResult.SemanticErrors.Any())
        {
            outputBuilder.AppendLine($"Structure is valid. {parseResult.SemanticErrors.Count} semantic error(s) to fix.");
        }
        else
        {
            outputBuilder.AppendLine("✓ No errors detected. Document is valid HJSON.");
        }

        return outputBuilder.ToString();
    }

    private void AppendCodeContext(StringBuilder outputBuilder, string[] sourceLines, int targetLineNumber, int contextLineCount)
    {
        int startLineIndex = Math.Max(0, targetLineNumber - 1 - contextLineCount);
        int endLineIndex = Math.Min(sourceLines.Length - 1, targetLineNumber - 1 + contextLineCount);

        for (int lineIndex = startLineIndex; lineIndex <= endLineIndex; lineIndex++)
        {
            string lineMarker = (lineIndex == targetLineNumber - 1) ? ">>>" : "   ";
            int displayLineNumber = lineIndex + 1;
            outputBuilder.AppendLine($"{lineMarker} {displayLineNumber,4} | {sourceLines[lineIndex]}");
        }
    }
}