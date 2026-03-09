namespace HJSONParserForAI.Core;

/// <summary>
/// Phase 1.5: Identifies healthy vs damaged regions of source code
/// based on structural analysis results
/// </summary>
public static class RegionIsolator
{
    public static List<Region> IdentifyRegions(
        string sourceText,
        List<Delimiter> allDelimiters,
        List<StructuralError> structuralErrors)
    {
        var codeRegions = new List<Region>();
        var sourceLines = sourceText.Split('\n');
        int totalLineCount = sourceLines.Length;

        if (structuralErrors.Count == 0)
        {
            // Entire document is healthy - single healthy region
            return new List<Region>
            {
                new Region(0, sourceText.Length, 1, totalLineCount, RegionHealth.Healthy, null)
            };
        }

        // Sort errors by location for sequential region building
        var sortedErrors = structuralErrors
            .OrderBy(e => e.Line)
            .ThenBy(e => e.Column)
            .ToList();

        // Track position as we build regions
        int currentOffset = 0;
        int currentLine = 1;

        foreach (var structError in sortedErrors)
        {
            // Determine error span based on opener/closer positions
            int errorStartOffset = structError.Opener?.Offset ?? structError.Closer?.Offset ?? 0;
            int errorEndOffset = structError.Closer?.Offset ?? structError.Opener?.Offset ?? 0;

            // Ensure we don't go backwards
            errorStartOffset = Math.Max(currentOffset, errorStartOffset);
            errorEndOffset = Math.Max(errorStartOffset, errorEndOffset);

            // If there's healthy code before this error, mark it as healthy region
            if (errorStartOffset > currentOffset)
            {
                int healthyRegionStartLine = currentLine;
                int healthyRegionEndLine = Math.Max(1, structError.Line - 1);

                codeRegions.Add(new Region(
                    currentOffset, errorStartOffset,
                    healthyRegionStartLine, healthyRegionEndLine,
                    RegionHealth.Healthy,
                    null
                ));
            }

            // Determine the line range affected by this error
            int errorStartLine = structError.Opener?.Line ?? structError.Line;
            int errorEndLine = structError.Closer?.Line ?? structError.Line;

            // Mark the error region as damaged
            codeRegions.Add(new Region(
                errorStartOffset, errorEndOffset + 1,
                errorStartLine, errorEndLine,
                RegionHealth.Damaged,
                structError
            ));

            currentOffset = errorEndOffset + 1;
            currentLine = errorEndLine + 1;
        }

        // Any remaining code after the last error is potentially healthy
        // (though it might be affected by structural damage upstream)
        if (currentOffset < sourceText.Length)
        {
            codeRegions.Add(new Region(
                currentOffset, sourceText.Length,
                currentLine, totalLineCount,
                RegionHealth.Healthy,
                null
            ));
        }

        // Post-process: Mark regions following severe structural damage as Quarantined
        MarkQuarantinedRegions(codeRegions);

        return codeRegions;
    }

    /// <summary>
    /// Regions following unclosed delimiters may be too damaged to parse reliably
    /// Mark them as Quarantined to indicate low confidence in any parsing
    /// </summary>
    private static void MarkQuarantinedRegions(List<Region> codeRegions)
    {
        bool hasUpstreamUnclosedDelimiter = false;

        for (int regionIndex = 0; regionIndex < codeRegions.Count; regionIndex++)
        {
            var currentRegion = codeRegions[regionIndex];

            // Track if we've seen an unclosed delimiter error
            if (currentRegion.RelatedError?.Kind == StructuralErrorKind.UnclosedDelimiter)
            {
                hasUpstreamUnclosedDelimiter = true;
            }

            // If a "healthy" region follows unclosed delimiter damage, quarantine it
            // because the structural context is ambiguous
            if (hasUpstreamUnclosedDelimiter && currentRegion.Health == RegionHealth.Healthy)
            {
                codeRegions[regionIndex] = currentRegion with { Health = RegionHealth.Quarantined };
            }
        }
    }
}