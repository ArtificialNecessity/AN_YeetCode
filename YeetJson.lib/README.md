# YeetJson

**AI-Friendly JSON Parser with Two-Phase Error Diagnostics**

YeetJson is a robust JSON/HJSON parser designed to provide exceptionally clear, actionable error messages for AI assistants and developers. It supports multiple JSON variants with configurable strictness levels.

## Supported Formats

- **JSON** — Standard JSON (RFC 8259)
- **JSONC** — JSON with Comments (`//` and `/* */`)
- **HJSON** — Human JSON with unquoted keys/values, trailing commas, multiline strings
- **YTSON** — HJSON + key attributes (`key [attr, attr:value]: value`)

## Key Features

### Two-Phase Parsing Architecture

1. **Phase 1: Structural Analysis** — Validates bracket/quote matching independently of content
2. **Phase 1.5: Region Isolation** — Identifies healthy vs damaged code regions
3. **Phase 2: Content Parsing** — Parses HJSON semantics with structural awareness

### AI-Friendly Error Diagnostics

When parsing fails, YeetJson doesn't just tell you *where* it failed — it tells you:

- **What's structurally wrong** (unclosed brace, mismatched delimiter, etc.)
- **Why it's wrong** (context showing both error location and related opener)
- **How to fix it** (ranked repair hypotheses with confidence scores)

Example error output:
```
## STRUCTURAL ERRORS (likely root causes)

### UnclosedDelimiter
**Location:** Line 5, Column 3
**Message:** Opening '{' at line 5 col 3 is never closed

**Context:**
```hjson
    3 | database: {
    4 |   host: localhost
>>> 5 |   settings: {
    6 |     timeout: 30
    7 |   }
```

**Likely fixes (ranked by confidence):**
- [70%] Insert '}' before line 7 (dedent from 4 to 2 spaces)
- [60%] Insert '}' before sibling close at line 7
- [30%] Insert '}' at end of file
```

## Usage

```csharp
using YeetJson;

// Parse HJSON with error diagnostics
var structuralAnalyzer = new StructuralAnalyzer();
var structureResult = structuralAnalyzer.Analyze(hjsonText);

var contentParser = new HjsonContentParser();
var parseResult = contentParser.Parse(hjsonText, structureResult);

if (parseResult.ParsedDocument != null) {
    // Success - use the JsonDocument
    var data = parseResult.ParsedDocument.Deserialize<MyType>();
} else {
    // Format errors for AI/human consumption
    var formatter = new DiagnosticFormatter();
    string diagnostics = formatter.FormatForAI(parseResult, hjsonText);
    Console.WriteLine(diagnostics);
}
```

## Key Attributes (YTSON Extension)

YeetJson supports key attributes for schema metadata:

```hjson
{
  name [optional, default:"unnamed"]: string
  count [min:0, max:100]: int
  tags [lang:en]: string
}
```

Enable attribute emission:
```csharp
var options = new HjsonParserOptions { EmitKeyAttributes = true };
var parser = new HjsonContentParser(options);
```

Attributes are emitted as a `__keyAttributes` sibling node in the parsed JSON.

## Output Format

YeetJson parses to `System.Text.Json.JsonDocument`, enabling seamless integration with .NET's JSON ecosystem:

```csharp
var parseResult = parser.Parse(hjsonText, structureResult);
var myObject = parseResult.ParsedDocument.Deserialize<MyType>();
```

## Design Philosophy

- **Errors are data** — Parse errors are structured, queryable, and actionable
- **AI-first diagnostics** — Error messages designed for LLM comprehension
- **Fail gracefully** — Partial parsing of healthy regions even when structure is damaged
- **Zero ambiguity** — Clear distinction between structural and semantic errors

## Documentation

- [`HJP_IMPL.md`](YeetJson/HJP_IMPL.md) — Implementation guide and architecture
- [`TESTING.md`](YeetJson/TESTING.md) — Testing strategy and gold file system
- [`TODO_TO_USE.md`](YeetJson/TODO_TO_USE.md) — Usage examples and integration guide

## Future Enhancements

- **Strictness levels** — Configurable parsing modes (strict JSON, JSONC, HJSON, YTSON)
- **Comment preservation** — Parse comments into the tree for round-trip editing
- **Format-preserving edits** — Programmatic HJSON editing without reformatting
- **HJSON serialization** — Convert JsonDocument back to human-friendly HJSON

---

**Part of the YeetCode project** — Schema-driven meta-programming for language transformation