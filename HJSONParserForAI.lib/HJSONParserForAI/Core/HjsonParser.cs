namespace HJSONParserForAI.Core;

/// <summary>
/// Phase 2: Parse HJSON content with structural awareness
/// This is a stub version that focuses on proving the two-phase architecture.
/// Full HJSON parsing would be implemented here in future iterations.
/// </summary>
public class HjsonParser
{
    public ParseResult Parse(string sourceText, StructureResult structureAnalysisResult)
    {
        var semanticErrors = new List<SemanticError>();
        object? parsedValue = null;

        // For now, just verify structure is sound before attempting content parsing
        if (structureAnalysisResult.StructuralErrors.Count == 0)
        {
            // Structure is valid - in full implementation, we would parse HJSON here
            // For proof of concept, return success indicator
            parsedValue = new Dictionary<string, object>
            {
                ["_parserStatus"] = "StructurallyValid",
                ["_regionCount"] = structureAnalysisResult.Regions.Count,
                ["_delimiterCount"] = structureAnalysisResult.AllDelimiters.Count
            };
        }
        else
        {
            // Structure has errors - report that structure must be fixed first
            var primaryStructuralError = structureAnalysisResult.StructuralErrors.First();

            semanticErrors.Add(new SemanticError(
                "StructuralDependency",
                1, 1,
                "Cannot parse HJSON content until structural errors are resolved",
                primaryStructuralError,
                "Fix structural issues first, then re-parse to check for semantic errors"
            ));

            // Try to extract partial information from healthy regions
            var healthyRegions = structureAnalysisResult.Regions
                .Where(r => r.Health == RegionHealth.Healthy)
                .ToList();

            if (healthyRegions.Count > 0)
            {
                parsedValue = new Dictionary<string, object>
                {
                    ["_parserStatus"] = "PartialParse",
                    ["_healthyRegionCount"] = healthyRegions.Count,
                    ["_errorCount"] = structureAnalysisResult.StructuralErrors.Count
                };
            }
        }

        return new ParseResult(
            parsedValue,
            semanticErrors,
            structureAnalysisResult.StructuralErrors
        );
    }
}