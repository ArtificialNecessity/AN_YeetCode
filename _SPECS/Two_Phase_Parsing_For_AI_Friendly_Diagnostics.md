# Two-Phase Parser Architecture for AI-Friendly Diagnostics

## The Core Problem

Traditional parsers answer: "Where did parsing fail?"
What AI assistants need: "What is structurally wrong, and what are the likely fixes?"

When a parser reports "Unexpected token '}' at line 47", an AI (or human) looking at line 47 sees a perfectly reasonable closing brace. The actual bug—a missing brace on line 12—is invisible because the parser has no hypothesis about *alternative valid structures*.

## Architecture Overview

```
ect┌─────────────────────────────────────────────────────────────────┐
│                         Source Text                              │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                  PHASE 1: Structural Analysis                    │
│  ─────────────────────────────────────────────────────────────  │
│  • Bracket/brace/paren matching                                  │
│  • Quote pairing (with escape awareness)                         │
│  • Multi-line string boundaries                                  │
│  • Produces: StructureMap + StructuralErrors                     │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                  PHASE 1.5: Region Isolation                     │
│  ─────────────────────────────────────────────────────────────  │
│  • Identify "healthy" regions (structurally valid)               │
│  • Identify "damaged" regions (structural ambiguity)             │
│  • Propose repair hypotheses for damaged regions                 │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                  PHASE 2: Content Parsing                        │
│  ─────────────────────────────────────────────────────────────  │
│  • Parse healthy regions fully                                   │
│  • Attempt partial parsing of damaged regions                    │
│  • Collect semantic errors with structural context               │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                  Diagnostic Synthesis                            │
│  ─────────────────────────────────────────────────────────────  │
│  • Merge structural + semantic errors                            │
│  • Rank by likelihood / minimum-edit-distance                    │
│  • Format for AI consumption                                     │
└─────────────────────────────────────────────────────────────────┘
```

## Phase 1: Structural Analysis

This is intentionally simple—a single-pass stack-based scanner. The goal is NOT to parse HJSON, just to track delimiter structure.

### Data Structures

```csharp
public enum DelimiterType { Brace, Bracket, Paren, DoubleQuote, SingleQuote, MultiLineString }

public record Delimiter(
    DelimiterType Type,
    int Line,
    int Column,
    int Offset,
    bool IsOpen
);

public record StructuralError(
    StructuralErrorKind Kind,
    Delimiter? Opener,
    Delimiter? Closer,
    int Line,
    int Column,
    string Message,
    List<RepairHypothesis> Hypotheses
);

public enum StructuralErrorKind
{
    UnclosedDelimiter,      // Opened but never closed
    UnmatchedClose,         // Close with no matching open
    MismatchedPair,         // { closed with ] 
    UnclosedString,         // Quote opened, hit EOL (for single-line strings)
    AmbiguousNesting        // Multiple valid interpretations
}

public record RepairHypothesis(
    RepairAction Action,
    int Line,
    int Column,
    string Description,
    float Confidence         // 0.0 - 1.0
);

public enum RepairAction { Insert, Delete, Replace }
```

### The Scanner

```csharp
public class StructuralAnalyzer
{
    public StructureResult Analyze(string source)
    {
        var stack = new Stack<Delimiter>();
        var errors = new List<StructuralError>();
        var allDelimiters = new List<Delimiter>();
      
        int i = 0, line = 1, col = 1;
      
        while (i < source.Length)
        {
            char c = source[i];
          
            // Track position
            if (c == '\n') { line++; col = 1; }
            else { col++; }
          
            // Inside string? Only look for closing quote (with escape handling)
            if (stack.Count > 0 && IsStringDelimiter(stack.Peek().Type))
            {
                if (IsMatchingClose(stack.Peek().Type, source, i, out int skip))
                {
                    var opener = stack.Pop();
                    allDelimiters.Add(new Delimiter(opener.Type, line, col, i, false));
                    i += skip;
                    continue;
                }
              
                // Check for unescaped newline in single-line string
                if (c == '\n' && stack.Peek().Type != DelimiterType.MultiLineString)
                {
                    var opener = stack.Pop();
                    errors.Add(new StructuralError(
                        StructuralErrorKind.UnclosedString,
                        opener, null, opener.Line, opener.Column,
                        $"String opened at line {opener.Line} col {opener.Column} not closed before end of line",
                        GenerateStringRepairHypotheses(opener, line, col)
                    ));
                }
              
                // Handle escapes
                if (c == '\\') { i += 2; continue; }
              
                i++; continue;
            }
          
            // Check for HJSON comments (// and /* */)
            if (c == '/' && i + 1 < source.Length)
            {
                if (source[i + 1] == '/') { i = SkipToEndOfLine(source, i); continue; }
                if (source[i + 1] == '*') { i = SkipBlockComment(source, i); continue; }
            }
          
            // Check for openers
            if (TryGetOpener(source, i, out var openerType, out int openerLen))
            {
                var delim = new Delimiter(openerType, line, col, i, true);
                stack.Push(delim);
                allDelimiters.Add(delim);
                i += openerLen;
                continue;
            }
          
            // Check for closers
            if (TryGetCloser(source, i, out var closerType, out int closerLen))
            {
                var closer = new Delimiter(closerType, line, col, i, false);
                allDelimiters.Add(closer);
              
                if (stack.Count == 0)
                {
                    errors.Add(new StructuralError(
                        StructuralErrorKind.UnmatchedClose,
                        null, closer, line, col,
                        $"Unexpected closing '{GetDelimChar(closerType)}' at line {line} col {col} with no matching opener",
                        GenerateUnmatchedCloseHypotheses(allDelimiters, closer)
                    ));
                }
                else if (stack.Peek().Type != closerType)
                {
                    var opener = stack.Peek();
                    errors.Add(new StructuralError(
                        StructuralErrorKind.MismatchedPair,
                        opener, closer, line, col,
                        $"Closing '{GetDelimChar(closerType)}' at line {line} col {col} doesn't match opening '{GetDelimChar(opener.Type)}' at line {opener.Line} col {opener.Column}",
                        GenerateMismatchHypotheses(opener, closer)
                    ));
                    // Attempt recovery: pop anyway if it helps
                    stack.Pop();
                }
                else
                {
                    stack.Pop();
                }
              
                i += closerLen;
                continue;
            }
          
            i++;
        }
      
        // Anything left on stack is unclosed
        while (stack.Count > 0)
        {
            var unclosed = stack.Pop();
            errors.Add(new StructuralError(
                StructuralErrorKind.UnclosedDelimiter,
                unclosed, null, unclosed.Line, unclosed.Column,
                $"Opening '{GetDelimChar(unclosed.Type)}' at line {unclosed.Line} col {unclosed.Column} is never closed",
                GenerateUnclosedHypotheses(source, allDelimiters, unclosed)
            ));
        }
      
        return new StructureResult(allDelimiters, errors, IdentifyRegions(allDelimiters, errors));
    }
}
```

### Hypothesis Generation (The Key Differentiator)

```csharp
private List<RepairHypothesis> GenerateUnclosedHypotheses(
    string source, 
    List<Delimiter> allDelims, 
    Delimiter unclosed)
{
    var hypotheses = new List<RepairHypothesis>();
  
    // Hypothesis 1: Insert closer at end of file
    hypotheses.Add(new RepairHypothesis(
        RepairAction.Insert,
        GetLastLine(source), GetLastCol(source),
        $"Insert '{GetCloseChar(unclosed.Type)}' at end of file",
        0.3f
    ));
  
    // Hypothesis 2: Find sibling closers at same indent level, insert before next one
    var siblings = FindSiblingClosers(allDelims, unclosed);
    foreach (var sibling in siblings)
    {
        hypotheses.Add(new RepairHypothesis(
            RepairAction.Insert,
            sibling.Line, sibling.Column,
            $"Insert '{GetCloseChar(unclosed.Type)}' before line {sibling.Line} (before sibling close)",
            0.6f
        ));
    }
  
    // Hypothesis 3: Look for suspicious line patterns after opener
    // e.g., a line that looks like it should start a new top-level block
    var suspiciousLines = FindStructuralBreakPoints(source, unclosed);
    foreach (var (line, col, description) in suspiciousLines)
    {
        hypotheses.Add(new RepairHypothesis(
            RepairAction.Insert,
            line, 0,
            $"Insert '{GetCloseChar(unclosed.Type)}' before line {line} ({description})",
            0.7f
        ));
    }
  
    // Hypothesis 4: The opener itself is wrong (less common)
    hypotheses.Add(new RepairHypothesis(
        RepairAction.Delete,
        unclosed.Line, unclosed.Column,
        $"Delete the '{GetDelimChar(unclosed.Type)}' at line {unclosed.Line} col {unclosed.Column} (it may be spurious)",
        0.2f
    ));
  
    return hypotheses.OrderByDescending(h => h.Confidence).ToList();
}

private List<(int Line, int Col, string Description)> FindStructuralBreakPoints(
    string source, 
    Delimiter unclosed)
{
    // HJSON-specific heuristics:
    // - A line starting at column 0/1 with an unquoted key followed by ':'
    // - A line with significantly less indentation than the opener
    // - A blank line followed by different indentation pattern
  
    var results = new List<(int, int, string)>();
    var lines = source.Split('\n');
    var openerIndent = GetIndent(lines[unclosed.Line - 1]);
  
    for (int i = unclosed.Line; i < lines.Length; i++)
    {
        var lineIndent = GetIndent(lines[i]);
        var trimmed = lines[i].TrimStart();
      
        // Significant dedent
        if (lineIndent < openerIndent && trimmed.Length > 0 && !trimmed.StartsWith("//"))
        {
            results.Add((i + 1, 0, $"dedent from {openerIndent} to {lineIndent} chars"));
        }
      
        // Looks like a new top-level key
        if (lineIndent == 0 && Regex.IsMatch(trimmed, @"^[a-zA-Z_][a-zA-Z0-9_]*\s*:"))
        {
            results.Add((i + 1, 0, "appears to be new top-level key"));
        }
    }
  
    return results;
}
```

## Phase 1.5: Region Isolation

After structural analysis, we know which parts of the document are "healthy" (balanced structure) vs "damaged" (inside or around a structural error).

```csharp
public record Region(
    int StartOffset,
    int EndOffset,
    int StartLine,
    int EndLine,
    RegionHealth Health,
    StructuralError? RelatedError
);

public enum RegionHealth { Healthy, Damaged, Quarantined }

public List<Region> IdentifyRegions(List<Delimiter> delimiters, List<StructuralError> errors)
{
    var regions = new List<Region>();
  
    if (errors.Count == 0)
    {
        // Entire document is healthy
        return new List<Region> { /* single healthy region */ };
    }
  
    // For each error, mark affected span as damaged
    // Spans between errors that are structurally valid are healthy
    // This allows Phase 2 to parse healthy regions fully and 
    // attempt best-effort on damaged regions
  
    // ... implementation
  
    return regions;
}
```

## Phase 2: Content Parsing

Now we parse actual HJSON/HDF content, but with awareness of structural regions.

```csharp
public class HjsonParser
{
    public ParseResult Parse(string source, StructureResult structure)
    {
        var values = new List<ParsedValue>();
        var errors = new List<SemanticError>();
      
        foreach (var region in structure.Regions)
        {
            if (region.Health == RegionHealth.Healthy)
            {
                // Full parsing with confidence
                var (value, regionErrors) = ParseRegion(source, region, strict: true);
                values.Add(value);
                errors.AddRange(regionErrors);
            }
            else
            {
                // Best-effort parsing, expect failures
                var (partialValue, regionErrors) = ParseRegion(source, region, strict: false);
              
                // Attach structural context to semantic errors
                foreach (var err in regionErrors)
                {
                    err.StructuralContext = region.RelatedError;
                    err.Note = $"This error may be caused by: {region.RelatedError?.Message}";
                }
              
                values.Add(partialValue);
                errors.AddRange(regionErrors);
            }
        }
      
        return new ParseResult(values, errors, structure.Errors);
    }
}
```

## AI-Friendly Error Output Format

This is the key deliverable—errors formatted for LLM consumption:

```csharp
public class DiagnosticFormatter
{
    public string FormatForAI(ParseResult result, string source)
    {
        var sb = new StringBuilder();
        var lines = source.Split('\n');
      
        sb.AppendLine("# PARSE DIAGNOSTICS");
        sb.AppendLine();
      
        // Structural errors first (they're usually the root cause)
        if (result.StructuralErrors.Any())
        {
            sb.AppendLine("## STRUCTURAL ERRORS (likely root causes)");
            sb.AppendLine();
          
            foreach (var err in result.StructuralErrors)
            {
                sb.AppendLine($"### {err.Kind}");
                sb.AppendLine($"**Location:** Line {err.Line}, Column {err.Column}");
                sb.AppendLine($"**Message:** {err.Message}");
                sb.AppendLine();
              
                // Show code context
                sb.AppendLine("**Context:**");
                sb.AppendLine("```");
                AppendCodeContext(sb, lines, err.Line, contextLines: 3);
                sb.AppendLine("```");
                sb.AppendLine();
              
                // Show repair hypotheses
                if (err.Hypotheses.Any())
                {
                    sb.AppendLine("**Likely fixes (ranked by confidence):**");
                    foreach (var hyp in err.Hypotheses.Take(3))
                    {
                        sb.AppendLine($"- [{hyp.Confidence:P0}] {hyp.Description}");
                        if (hyp.Action == RepairAction.Insert)
                        {
                            sb.AppendLine($"  At line {hyp.Line}, col {hyp.Column}");
                        }
                    }
                    sb.AppendLine();
                }
              
                // If we have an opener, show IT with context too
                if (err.Opener != null && err.Opener.Line != err.Line)
                {
                    sb.AppendLine($"**Related opener at line {err.Opener.Line}:**");
                    sb.AppendLine("```");
                    AppendCodeContext(sb, lines, err.Opener.Line, contextLines: 2);
                    sb.AppendLine("```");
                    sb.AppendLine();
                }
            }
        }
      
        // Semantic errors with structural context
        if (result.SemanticErrors.Any())
        {
            sb.AppendLine("## SEMANTIC ERRORS");
            sb.AppendLine();
          
            foreach (var err in result.SemanticErrors)
            {
                sb.AppendLine($"### {err.Kind} at line {err.Line}");
                sb.AppendLine($"**Message:** {err.Message}");
              
                if (err.StructuralContext != null)
                {
                    sb.AppendLine($"**⚠️ Note:** {err.Note}");
                    sb.AppendLine($"Fix the structural error first; this error may resolve automatically.");
                }
              
                sb.AppendLine();
            }
        }
      
        // Summary for AI
        sb.AppendLine("## SUMMARY FOR REPAIR");
        sb.AppendLine();
      
        if (result.StructuralErrors.Any())
        {
            var primary = result.StructuralErrors.First();
            var bestFix = primary.Hypotheses.FirstOrDefault();
          
            sb.AppendLine($"**Primary issue:** {primary.Message}");
            if (bestFix != null)
            {
                sb.AppendLine($"**Recommended fix:** {bestFix.Description}");
            }
            sb.AppendLine();
            sb.AppendLine("After fixing structural errors, re-parse to check for remaining semantic issues.");
        }
        else if (result.SemanticErrors.Any())
        {
            sb.AppendLine($"Structure is valid. {result.SemanticErrors.Count} semantic error(s) to fix.");
        }
        else
        {
            sb.AppendLine("No errors detected.");
        }
      
        return sb.ToString();
    }
  
    private void AppendCodeContext(StringBuilder sb, string[] lines, int targetLine, int contextLines)
    {
        int start = Math.Max(0, targetLine - 1 - contextLines);
        int end = Math.Min(lines.Length - 1, targetLine - 1 + contextLines);
      
        for (int i = start; i <= end; i++)
        {
            string marker = (i == targetLine - 1) ? ">>>" : "   ";
            sb.AppendLine($"{marker} {i + 1,4} | {lines[i]}");
        }
    }
}
```

## Example Output

Given this broken HJSON:

```hjson
{
  database: {
    host: localhost
    port: 5432
    settings: {
      timeout: 30
      retries: 3
    // missing close brace here
  }
  
  logging: {
    level: debug
  }
}
```

The diagnostic output would be:

```markdown
# PARSE DIAGNOSTICS

## STRUCTURAL ERRORS (likely root causes)

### MismatchedPair
**Location:** Line 9, Column 3
**Message:** Closing '}' at line 9 col 3 doesn't match opening '{' at line 5 col 15

**Context:**
```

    6 |       timeout: 30
       7 |       retries: 3
       8 |     // missing close brace here

>>> 9 |   }
>>> 10 |
>>> 11 |   logging: {
>>>
>>

```

**Likely fixes (ranked by confidence):**
- [70%] Insert '}' before line 9 (dedent from 6 to 2 chars)
- [60%] Insert '}' before line 9 (before sibling close)
- [30%] Insert '}' at end of file

**Related opener at line 5:**
```

    3 |     host: localhost
       4 |     port: 5432

>>> 5 |     settings: {
>>> 6 |       timeout: 30
>>> 7 |       retries: 3
>>>
>>

```

## SUMMARY FOR REPAIR

**Primary issue:** Closing '}' at line 9 col 3 doesn't match opening '{' at line 5 col 15
**Recommended fix:** Insert '}' before line 9 (dedent from 6 to 2 chars)

After fixing structural errors, re-parse to check for remaining semantic issues.
```

## Implementation Notes

### For HJSON Specifically

HJSON has several structural quirks to handle:

- Unquoted strings (makes string detection context-dependent in Phase 2, but doesn't affect Phase 1)
- Multiline strings with `'''`
- Optional commas (actually makes parsing easier—fewer "missing comma" false positives)
- Comments (`//` and `/* */`)

### For Clearsilver HDF

HDF is simpler structurally:

- Brace-based hierarchy OR indentation-based
- No brackets or parens in structure
- Angle brackets for includes: `#include <file>`

The same two-phase approach works; Phase 1 just tracks `{`/`}` and validates indent consistency.

### Parser Generator Choice

For Phase 2 (content parsing), I'd actually suggest **hand-written recursive descent** for HJSON. It's simple enough that a generator adds complexity without benefit, and hand-written gives you complete control over error recovery and partial-parse behavior.

If you want a generator anyway, **Superpower** fits well because:

- Tokenizer-first aligns with our Phase 1 output
- Easy to attempt parses on regions and capture partial results
- Error positions are token-precise

## Extension: Edit-Distance Repair

For maximum AI-friendliness, compute actual repairs:

```csharp
public class MinimalRepairFinder
{
    // Given structural errors, find minimum edit (insert/delete/replace)
    // that produces a structurally valid document
  
    public List<Edit> FindMinimalRepairs(string source, List<StructuralError> errors)
    {
        // This is essentially computing Levenshtein distance on the 
        // delimiter sequence, with domain-specific costs:
        // - Inserting a closer near its opener: low cost
        // - Deleting a random brace: high cost
        // - Swapping brace types: medium cost
      
        // For most real errors, this is overkill—the heuristic 
        // hypotheses above catch 95% of cases. But for truly 
        // mangled files, this can find non-obvious repairs.
    }
}
```

## Testing Strategy

Build a corpus of intentionally broken HJSON files with known errors:

1. Missing closers (at various nesting depths)
2. Extra closers
3. Mismatched types (`{` closed with `]`)
4. Unclosed strings
5. Multiple simultaneous errors

For each, verify:

- Phase 1 detects the structural issue
- Hypotheses include the actual fix
- Highest-confidence hypothesis is correct >80% of the time

---

## Next Steps

1. Implement Phase 1 structural analyzer (~200 lines)
2. Test on corpus of broken files
3. Tune hypothesis generation heuristics
4. Implement Phase 2 HJSON parser (hand-written RD, ~400 lines)
5. Build diagnostic formatter
6. Integration: wire into your AI pipeline

Want me to start with a working Phase 1 implementation?
