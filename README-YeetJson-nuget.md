# ArtificialNecessity.YeetJson

(C)opyright 2026 by David Jeske <davidj@gmail.com>
Licensed under Apache 2.0

**JSON parsing with errors your AI will love as much as you will.**

YeetJson is a two-phase JSON/HJSON parser that produces `System.Text.Json.JsonDocument` output with exceptionally clear, actionable error diagnostics. When parsing fails, it doesn't just tell you *where* — it tells you *what's wrong*, *why*, and *how to (maybe) )fix it*, with ranked repair suggestions.

> 💡 *If you think this is cool, check out [YeetCode](https://www.nuget.org/packages/ArtificialNecessity.YeetCode/) — schema-driven meta-programming to yeet one language into another!*

## Installation

```xml
<PackageReference Include="ArtificialNecessity.YeetJson" Version="0.1.*" />
```

---

## Supported Formats

| Format          | Description                                                                   |
| --------------- | ----------------------------------------------------------------------------- |
| **JSON**  | Standard JSON (RFC 8259)                                                      |
| **JSONC** | JSON with `//` and `/* */` comments                                       |
| **HJSON** | Human JSON — unquoted keys/values, trailing commas,`'''` multiline strings |
| **YTSON** | HJSON + key attributes:`key [optional, default:"value"]: type`              |

All formats parse to `System.Text.Json.JsonDocument` for seamless .NET integration.

---

## Why Two-Phase Parsing?

Traditional parsers answer: *"Where did parsing fail?"*
YeetJson answers: *"What is structurally wrong, and what are the likely fixes?"*

When a parser reports `Unexpected token '}' at line 47`, you look at line 47 and see a perfectly reasonable closing brace. The actual bug — a missing brace on line 12 — is invisible because the parser has no hypothesis about *alternative valid structures*.

YeetJson solves this with a two-phase architecture:

```
Source Text
    │
    ▼
Phase 1: Structural Analysis
    • Bracket/brace/paren matching
    • Quote pairing with escape awareness
    • Multiline string boundaries
    • Produces: StructureMap + StructuralErrors + RepairHypotheses
    │
    ▼
Phase 1.5: Region Isolation
    • Identifies "healthy" regions (structurally valid)
    • Identifies "damaged" regions (structural ambiguity)
    │
    ▼
Phase 2: Content Parsing
    • Parses healthy regions fully → JsonDocument
    • Attempts partial parsing of damaged regions
    • Attaches structural context to semantic errors
    │
    ▼
Diagnostic Output
    • Structural + semantic errors merged
    • Ranked repair hypotheses with confidence scores
    • Formatted for AI or human consumption
```

---

## Quick Start

```csharp
using YeetJson;

string hjsonText = File.ReadAllText("config.hjson");

// Phase 1: Structural analysis
var structuralAnalyzer = new StructuralAnalyzer();
var structureResult = structuralAnalyzer.Analyze(hjsonText);

// Phase 2: Content parsing
var contentParser = new HjsonContentParser();
var parseResult = contentParser.Parse(hjsonText, structureResult);

if (parseResult.ParsedDocument != null)
{
    // Success — use the JsonDocument
    var root = parseResult.ParsedDocument.RootElement;
    string host = root.GetProperty("database").GetProperty("host").GetString()!;
}
else
{
    // Format errors for AI/human consumption
    var formatter = new DiagnosticFormatter();
    string diagnostics = formatter.FormatForAI(parseResult, hjsonText);
    Console.WriteLine(diagnostics);
}
```

---

## Error Diagnostics Example

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

YeetJson produces:

```
## STRUCTURAL ERRORS (likely root causes)

### MismatchedPair
**Location:** Line 9, Column 3
**Message:** Closing '}' at line 9 col 3 doesn't match opening '{' at line 5 col 15

**Context:**
      6 |       timeout: 30
      7 |       retries: 3
      8 |     // missing close brace here
>>>   9 |   }
     10 |
     11 |   logging: {

**Likely fixes (ranked by confidence):**
- [70%] Insert '}' before line 9 (dedent from 6 to 2 chars)
- [60%] Insert '}' before line 9 (before sibling close)
- [30%] Insert '}' at end of file

**Related opener at line 5:**
      3 |     host: localhost
      4 |     port: 5432
>>>   5 |     settings: {
      6 |       timeout: 30
      7 |       retries: 3

## SUMMARY FOR REPAIR
**Primary issue:** Closing '}' at line 9 col 3 doesn't match opening '{' at line 5 col 15
**Recommended fix:** Insert '}' before line 9 (dedent from 6 to 2 chars)
```

The error points to the **actual problem** (missing brace after `settings`), not the misleading symptom (the `}` on line 9 that looks fine in isolation).

---

## Key Attributes (YTSON Extension)

YeetJson extends HJSON with key attributes for schema metadata:

```hjson
{
  name [optional, default:"unnamed"]: string
  count [min:0, max:100]: int
  tags [lang:en]: string
}
```

Enable attribute emission:

```csharp
var contentParser = new HjsonContentParser(new HjsonParserOptions
{
    EmitKeyAttributes = true
});
```

Attributes are emitted as a `__keyAttributes` sibling node in the parsed JSON, preserving the metadata alongside the data.

---

## API Reference

### Core Types

| Type                    | Purpose                                                       |
| ----------------------- | ------------------------------------------------------------- |
| `StructuralAnalyzer`  | Phase 1 — bracket/quote matching, structural error detection |
| `HjsonContentParser`  | Phase 2 — HJSON content parsing to `JsonDocument`          |
| `DiagnosticFormatter` | Formats errors for AI/human consumption                       |
| `RegionIsolator`      | Phase 1.5 — identifies healthy vs damaged regions            |

### Error Types

| Type                 | Purpose                                                |
| -------------------- | ------------------------------------------------------ |
| `StructuralError`  | Bracket/quote structural issues with repair hypotheses |
| `SemanticError`    | Content-level errors with structural context           |
| `RepairHypothesis` | Suggested fix with confidence score (0.0–1.0)         |

### Result Types

| Type                | Purpose                                       |
| ------------------- | --------------------------------------------- |
| `StructureResult` | Phase 1 output — delimiters, errors, regions |
| `ParseResult`     | Final output —`JsonDocument?` + all errors |

---

## Design Philosophy

- **Errors are data** — Parse errors are structured records with repair suggestions, not just strings
- **AI-first diagnostics** — Error messages designed for LLM comprehension and automated repair
- **Fail gracefully** — Healthy regions parse fully even when other regions are damaged
- **Zero ambiguity** — Clear distinction between structural errors (root causes) and semantic errors (symptoms)
- **Standard output** — Parses to `System.Text.Json.JsonDocument` for seamless .NET integration

---

## License

Apache 2.0
