# YeetCode & YeetJson

(C)opyright 2026 by David Jeske <davidj@gmail.com>
Licensed under Apache 2.0

**Yeet one language into another.   -   HJSon and Json Parsing with errors your AI will love as much as you will.**

YeetCode is a schema-driven meta-programming tool for language-to-language transformation. It parses custom syntax into validated HJSON, then generates multi-file output through templates.

## What It Does

Transform (most) any custom language (DSL) into any target language through three artifacts:

1. **Schema** (`.ytson`) — defines the data shape with types, defaults, and validation rules
2. **Grammar** (`.yeet`) — PEG parser that maps input syntax to the schema
3. **Template** (`.yt`) — generates output files from validated data

```
grammar.yeet ──→ data.hjson ──→ template.yt ──→ output files
  (parse)       (validated)     (generate)       (N files)
                     ↑
               schema.ytson
                (validates)
```

## Quick Example

**Input:** Protocol Buffer definition
**Output:** C# classes with serialization

```proto
message Widget {
  required string name = 1;
  int32 quantity = 2;
  repeated string tags = 3;
}
```

↓ *YeetCode pipeline* ↓ - [**20_ProtoBuff_YeetCode.md**](_SPECS/20_ProtoBuff_YeetCode.md)

```csharp
public class Widget
{
    public string Name { get; set; } = "";
    public int Quantity { get; set; }
    public List<string> Tags { get; set; } = new();
  
    public void Encode(IWriter writer) { /* ... */ }
    public static Widget Decode(IReader reader) { /* ... */ }
}
```

## Key Features

- **Schema-driven validation** — data shape is enforced before templates run
- **Human-readable intermediate** — HJSON output is inspectable, editable, diffable
- **Multi-file output** — one template generates N files with custom delimiters
- **Zero escaping** — pick delimiters that don't collide with your output language
- **Template comments** — `<% # comment %>` for annotations that don't appear in output
- **Smart line trimming** — standalone directive lines don't leave blank lines in output (configurable via `trimlines=false`)
- **Recursive types** — discriminated unions and tree structures are first-class
- **PEG grammars** — deterministic parsing with ordered choice, no ambiguity

## Getting Started

See [**Intro to YeetCode**](_SPECS/00_IntroToYeetCode.md) for a complete walkthrough with examples.

## Documentation

- [**IntroToYeetCode.md**](_SPECS/00_IntroToYeetCode.md) — Quick start guide with complete example
- [**YeetCodeSpec.md**](_SPECS/01_YeetCodeSpec.md) — Full specification
- [**20_ProtoBuff_YeetCode.md**](_SPECS/20_ProtoBuff_YeetCode.md) — Protobuf → C# example
- [**05_YeetCode_ParserGenerator.md**](_SPECS/05_YeetCode_ParserGenerator.md) — Runtime parser generation

## Project Structure

- **[YeetJson.lib/](YeetJson.lib/)** — Two-phase JSON/HJSON/YTSON parser with AI-friendly error diagnostics. Supports JSON, JSONC, HJSON, and YTSON (HJSON + key attributes). Provides exceptionally clear, actionable error messages with ranked repair suggestions. See [YeetJson README](YeetJson.lib/README.md) for details.
- **[YeetCode.VSCode/](YeetCode.VSCode/)** — VSCode extension providing syntax highlighting for all YeetCode file formats (HJSON, YTSON, YTGMR, YTMPL). See [VSCode Extension README](YeetCode.VSCode/README.md) for details and screenshots.
- **YeetCode.lib/** — Core YeetCode implementation
  - `Schema/` — Schema loading and validation
  - `Template/` — Template engine with custom delimiters
  - `Grammar/` — PEG grammar parser and interpreter
  - `Core/` — Utilities (string case conversion, JSON source generators)
- **YeetCode_Tests/** — Comprehensive test suite (37 tests, all passing)

## Current Status

**Phase 1-3 Complete:**

- ✅ HJSON Parser with key attributes extension
- ✅ Schema system with type validation and defaults
- ✅ Template engine with multi-file output
- ✅ Grammar lexer, parser, and PEG interpreter
- ✅ All tests passing (37/37)

**Remaining Work:**

- Grammar preprocessor (`%define`, `%if`/`%else`/`%endif`)
- CLI implementation
- End-to-end integration tests

## File Extensions

| Extension  | Purpose                               |
| ---------- | ------------------------------------- |
| `.hjson` | Standard HJSON data files             |
| `.ytson` | HJSON + key attributes (schema files) |
| `.yt`    | YeetCode templates                    |
| `.yeet`  | PEG grammar definitions               |

## License

(not sure yet)
