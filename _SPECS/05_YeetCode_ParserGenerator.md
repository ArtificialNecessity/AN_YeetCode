# Yeetcode Parser Generator

**When your grammar needs to run at runtime, not just build time.**

Companion spec to the main Yeetcode spec. The parser generator takes
the same `.yeet` grammar files used for build-time code generation and
produces standalone, dependency-free parsers in any target language.

---

## The Two Modes

Yeetcode grammars serve double duty:

| | Build-time Mode | Parser Generator Mode |
|---|---|---|
| **When** | Build / CI | Runtime |
| **Who parses** | Yeetcode's built-in PEG interpreter | Generated parser (standalone code) |
| **Output** | HJSON → templates → generated code | A parser source file that produces HJSON |
| **Dependencies** | Yeetcode toolchain | None — parser is self-contained |
| **Use case** | Compile `.crucible` → `Crucible.g.cs` | Load `.crucible` at runtime, execute dynamically |

Same grammar. Same schema. Same HJSON shape. Different execution context.

---

## Pipeline

The parser generator is itself a Yeetcode pipeline:

```
grammar-schema.hjson          ← schema: what a grammar looks like
grammar-grammar.yeet          ← grammar: parses .yeet syntax
                ↓
         grammar.hjson         ← your grammar, as structured data
                ↓
  parser-csharp.yt             ← template: emits a C# parser
  parser-typescript.yt         ← template: emits a TS parser
  parser-rust.yt               ← template: emits a Rust parser
                ↓
  MyLanguageParser.g.cs        ← standalone recursive descent parser
```

The generated parser:
- Is a single source file (or small set of files)
- Has zero external dependencies
- Parses input text into the HJSON shape defined by the schema
- Validates against the schema (fills defaults, rejects missing required fields)
- Returns a structured object tree that matches the schema types

---

## 1. Grammar Schema (`grammar-schema.hjson`)

This schema describes what a `.yeet` grammar file contains — rules, expressions,
captures, type mappings, lexer tokens.

```hjson
{
  # ── Expression types (what appears in rule bodies) ──

  @Literal: {
    value: string             # "message", ";", "{"
  }

  @TokenRef: {
    name: string              # IDENT, INT, QUOTED_STRING
  }

  @RuleRef: {
    name: string              # another rule name
  }

  @Capture: {
    name: string              # capture label
    expr: @Expr               # what to capture
  }

  @Sequence: {
    elements: [@Expr]
  }

  @Choice: {
    alternatives: [@Expr]     # ordered — first match wins (PEG)
  }

  @Repeat: {
    expr: @Expr
    mode: string              # "zero_or_more" | "one_or_more" | "optional"
  }

  @Group: {
    expr: @Expr
  }

  @Expr: {
    kind: string              # @Literal | @TokenRef | @RuleRef | @Capture |
                              # @Sequence | @Choice | @Repeat | @Group
    literal: @Literal?
    token_ref: @TokenRef?
    rule_ref: @RuleRef?
    capture: @Capture?
    sequence: @Sequence?
    choice: @Choice?
    repeat: @Repeat?
    group: @Group?
  }

  # ── Type mapping (the -> arrows) ──

  @TypeMapping: {
    target_type: string?      # @TypeName — what type this rule produces
    kind_value: string?       # @VariantName — discriminator for unions
    path: string?             # schema path like "messages[name]"
  }

  # ── Rules ──

  @Rule: {
    expr: @Expr
    mappings: [@TypeMapping]?
  }

  @Token: {
    pattern: string           # regex
    flags: string?            # "s" for dotall, etc.
  }

  # ── Document root ──

  rules: {@Rule}              # parser rules — lowercase names
  tokens: {@Token}            # lexer tokens — UPPERCASE names
  skip: string?               # whitespace/comment pattern
}
```

---

## 2. Grammar Grammar (`grammar-grammar.yeet`)

This grammar parses `.yeet` files. It's self-referential — Yeetcode uses
its own grammar format to describe its own grammar format.

```
# ═══════════════════════════════════════════════
# Top level
# ═══════════════════════════════════════════════

file ::= (rule_def | token_def | skip_def)*

rule_def ::= name:RULE_NAME "::=" expr:choice_expr mapping*
  -> rules[name]
  -> @Rule

token_def ::= name:TOKEN_NAME "::=" pattern:REGEX flags:REGEX_FLAGS?
  -> tokens[name]
  -> @Token

skip_def ::= "%skip" "::=" pattern:REGEX

# ═══════════════════════════════════════════════
# Expressions (precedence: choice < sequence < unary)
# ═══════════════════════════════════════════════

choice_expr ::= first:sequence_expr ("|" rest:sequence_expr)+
  -> @Expr { kind: @Choice }
           | sequence_expr

sequence_expr ::= elements:unary_expr+
  -> @Expr { kind: @Sequence }

unary_expr ::= primary_expr "*"
  -> @Expr { kind: @Repeat }
  -> @Repeat { mode: "zero_or_more" }
          | primary_expr "+"
  -> @Expr { kind: @Repeat }
  -> @Repeat { mode: "one_or_more" }
          | primary_expr "?"
  -> @Expr { kind: @Repeat }
  -> @Repeat { mode: "optional" }
          | primary_expr

primary_expr ::= capture_expr | literal_expr | token_ref_expr
            | rule_ref_expr | group_expr

capture_expr ::= name:RULE_NAME ":" expr:unary_expr
  -> @Expr { kind: @Capture }

literal_expr ::= value:QUOTED_STRING
  -> @Expr { kind: @Literal }

token_ref_expr ::= name:TOKEN_NAME
  -> @Expr { kind: @TokenRef }

rule_ref_expr ::= name:RULE_NAME
  -> @Expr { kind: @RuleRef }

group_expr ::= "(" expr:choice_expr ")"
  -> @Expr { kind: @Group }

# ═══════════════════════════════════════════════
# Type mappings (-> arrows)
# ═══════════════════════════════════════════════

mapping ::= "->" mapping_body

mapping_body ::= path_mapping | type_mapping

path_mapping ::= path:SCHEMA_PATH
  -> @TypeMapping

type_mapping ::= target_type:TYPE_REF ("{" "kind:" kind_value:TYPE_REF "}")?
  -> @TypeMapping

# ═══════════════════════════════════════════════
# Lexer
# ═══════════════════════════════════════════════

RULE_NAME ::= /[a-z_][a-z0-9_]*/
TOKEN_NAME ::= /[A-Z_][A-Z0-9_]*/
TYPE_REF ::= /@[A-Z][a-zA-Z0-9]*/
SCHEMA_PATH ::= /[a-z_][a-z0-9_]*(\.[a-z_][a-z0-9_]*)*(\[\w+\])*/
QUOTED_STRING ::= /"(?:[^"\\]|\\.)*"/
REGEX ::= /\/(?:[^\/\\]|\\.)*\/[a-z]*/
REGEX_FLAGS ::= /[a-z]+/

%skip ::= /(?:\s|#[^\n]*)*/
```

---

## 3. Parser Templates

The parser generator ships templates for each target language. Each
template reads a parsed grammar (as HJSON) and emits a complete
recursive descent parser.

### What the generated parser contains

For a grammar with N rules and M tokens:

- **Lexer** — regex-based tokenizer with skip pattern
- **N parse methods** — one per grammar rule, recursive descent
- **Token matching** — literal and token consumption with error reporting
- **Capture collection** — named captures assembled into HJSON-compatible objects
- **Schema validation** — default filling, required field checking, type validation
- **HJSON output** — returns structured data matching the schema

### Template: `parser-csharp.yt`

```
<?yt delim="<% %>" ?>
<%#output parser_class_name + ".g.cs"%>
// <auto-generated>
//   Parser generated by Yeetcode Parser Generator — do not edit.
// </auto-generated>
#nullable enable

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace <%namespace%>;

public sealed class <%parser_class_name%>
{
    private string _input = "";
    private int _pos;
    private int _line;
    private int _col;

    // ── Token patterns ──────────────────────────
<%#each tokens as name, tok%>
    private static readonly Regex _<%name%> = new(@"<%tok.pattern%>"<%#if tok.flags%>, RegexOptions.<%regex_options[tok.flags]%><%/if%>);
<%/each%>
<%#if skip%>
    private static readonly Regex _Skip = new(@"<%skip%>");
<%/if%>

    // ── Public API ──────────────────────────────

    public Dictionary<string, object?> Parse(string input)
    {
        _input = input;
        _pos = 0;
        _line = 1;
        _col = 1;

        var result = Parse_<%first_rule%>();
        SkipWhitespace();
        if (_pos < _input.Length)
            throw new ParseException($"Unexpected input at {Location()}: '{Peek(20)}'");
        return result;
    }

    // ── Skip ────────────────────────────────────

    private void SkipWhitespace()
    {
<%#if skip%>
        var match = _Skip.Match(_input, _pos);
        if (match.Success && match.Index == _pos)
        {
            Advance(match.Length);
        }
<%/if%>
    }

    // ── Token consumption ───────────────────────

    private string? TryConsume(string literal)
    {
        SkipWhitespace();
        if (_pos + literal.Length <= _input.Length &&
            _input.AsSpan(_pos, literal.Length).SequenceEqual(literal))
        {
            Advance(literal.Length);
            return literal;
        }
        return null;
    }

    private string ConsumeLiteral(string literal)
    {
        return TryConsume(literal)
            ?? throw new ParseException($"Expected '{literal}' at {Location()}, got '{Peek(20)}'");
    }

    private string? TryConsumeToken(Regex pattern)
    {
        SkipWhitespace();
        var match = pattern.Match(_input, _pos);
        if (match.Success && match.Index == _pos)
        {
            Advance(match.Length);
            return match.Value;
        }
        return null;
    }

    private string ConsumeToken(Regex pattern, string name)
    {
        return TryConsumeToken(pattern)
            ?? throw new ParseException($"Expected {name} at {Location()}, got '{Peek(20)}'");
    }

    // ── Position tracking ───────────────────────

    private void Advance(int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (_input[_pos + i] == '\n') { _line++; _col = 1; }
            else { _col++; }
        }
        _pos += count;
    }

    private string Location() => $"line {_line}, col {_col}";
    private string Peek(int n) => _input[_pos..Math.Min(_pos + n, _input.Length)];

    // ── Save/restore for backtracking ───────────

    private (int pos, int line, int col) Save() => (_pos, _line, _col);

    private void Restore((int pos, int line, int col) state)
    {
        _pos = state.pos;
        _line = state.line;
        _col = state.col;
    }

    // ── Parse methods (one per rule) ────────────

<%#each rules as rule_name, rule%>
<%#call emit_parse_method(rule_name, rule)%>

<%/each%>
}

public class ParseException : Exception
{
    public ParseException(string message) : base(message) { }
}
<%/output%>

<%# ═══════════════════════════════════════════════ %>
<%# Method emission — one per grammar rule          %>
<%# ═══════════════════════════════════════════════ %>

<%#define emit_parse_method(rule_name, rule)%>
    private Dictionary<string, object?>? Parse_<%rule_name%>()
    {
        var _captures = new Dictionary<string, object?>();
        var _saved = Save();

<%#call emit_expr(rule.expr, "        ")%>

<%#if rule.mappings%>
        // Apply type mappings
<%#each rule.mappings as m%>
<%#if m.kind_value%>
        _captures["kind"] = "<%m.kind_value%>";
<%/if%>
<%/each%>
<%/if%>

        return _captures;

    _fail:
        Restore(_saved);
        return null;
    }
<%/define%>

<%# ═══════════════════════════════════════════════ %>
<%# Expression emission — recursive                 %>
<%# ═══════════════════════════════════════════════ %>

<%#define emit_expr(expr, indent)%>
<%#if expr.kind == @Sequence%>
<%#call emit_sequence(expr.sequence, indent)%>
<%#elif expr.kind == @Choice%>
<%#call emit_choice(expr.choice, indent)%>
<%#elif expr.kind == @Literal%>
<%indent%>if (TryConsume("<%expr.literal.value%>") == null) goto _fail;
<%#elif expr.kind == @TokenRef%>
<%indent%>{
<%indent%>    var _tok = TryConsumeToken(_<%expr.token_ref.name%>);
<%indent%>    if (_tok == null) goto _fail;
<%indent%>}
<%#elif expr.kind == @RuleRef%>
<%indent%>{
<%indent%>    var _sub = Parse_<%expr.rule_ref.name%>();
<%indent%>    if (_sub == null) goto _fail;
<%indent%>}
<%#elif expr.kind == @Capture%>
<%#call emit_capture(expr.capture, indent)%>
<%#elif expr.kind == @Repeat%>
<%#call emit_repeat(expr.repeat, indent)%>
<%#elif expr.kind == @Group%>
<%#call emit_expr(expr.group.expr, indent)%>
<%/if%>
<%/define%>

<%#define emit_sequence(seq, indent)%>
<%#each seq.elements as elem%>
<%#call emit_expr(elem, indent)%>
<%/each%>
<%/define%>

<%#define emit_choice(choice, indent)%>
<%indent%>{
<%indent%>    var _choiceSaved = Save();
<%#each choice.alternatives as alt%>
<%#if !first%>
<%indent%>    Restore(_choiceSaved);
<%/if%>
<%indent%>    // Alternative <%index%>
<%indent%>    do {
<%#call emit_expr(alt, indent + "        ")%>
<%indent%>        goto _choiceDone;
<%indent%>    } while (false);
<%/each%>
<%indent%>    goto _fail; // no alternative matched
<%indent%>    _choiceDone:;
<%indent%>}
<%/define%>

<%#define emit_capture(cap, indent)%>
<%#if cap.expr.kind == @TokenRef%>
<%indent%>{
<%indent%>    var _tok = TryConsumeToken(_<%cap.expr.token_ref.name%>);
<%indent%>    if (_tok == null) goto _fail;
<%indent%>    _captures["<%cap.name%>"] = _tok;
<%indent%>}
<%#elif cap.expr.kind == @RuleRef%>
<%indent%>{
<%indent%>    var _sub = Parse_<%cap.expr.rule_ref.name%>();
<%indent%>    if (_sub == null) goto _fail;
<%indent%>    _captures["<%cap.name%>"] = _sub;
<%indent%>}
<%#elif cap.expr.kind == @Literal%>
<%indent%>{
<%indent%>    var _lit = TryConsume("<%cap.expr.literal.value%>");
<%indent%>    if (_lit == null) goto _fail;
<%indent%>    _captures["<%cap.name%>"] = _lit;
<%indent%>}
<%#else%>
<%indent%>// complex capture — evaluate expr, store result
<%indent%>{
<%indent%>    var _inner = new Dictionary<string, object?>();
<%indent%>    // TODO: nested capture expression
<%indent%>    _captures["<%cap.name%>"] = _inner;
<%indent%>}
<%/if%>
<%/define%>

<%#define emit_repeat(rep, indent)%>
<%#if rep.mode == "zero_or_more"%>
<%indent%>while (true)
<%indent%>{
<%indent%>    var _repSaved = Save();
<%#call emit_expr(rep.expr, indent + "    ")%>
<%indent%>    continue;
<%indent%>    _fail: Restore(_repSaved); break;
<%indent%>}
<%#elif rep.mode == "one_or_more"%>
<%indent%>// must match at least once
<%#call emit_expr(rep.expr, indent)%>
<%indent%>while (true)
<%indent%>{
<%indent%>    var _repSaved = Save();
<%#call emit_expr(rep.expr, indent + "    ")%>
<%indent%>    continue;
<%indent%>    _fail: Restore(_repSaved); break;
<%indent%>}
<%#elif rep.mode == "optional"%>
<%indent%>{
<%indent%>    var _optSaved = Save();
<%#call emit_expr(rep.expr, indent + "    ")%>
<%indent%>    goto _optDone;
<%indent%>    _fail: Restore(_optSaved);
<%indent%>    _optDone:;
<%indent%>}
<%/if%>
<%/define%>
```

---

## 4. Generated Parser Example

Given this grammar:

```
# simple_calc.grammar.yeet

expr ::= term (("+" | "-") term)*

term ::= factor (("*" | "/") factor)*

factor ::= number | "(" expr ")"

number ::= value:NUMBER

NUMBER ::= /[0-9]+(\.[0-9]+)?/

%skip ::= /\s*/
```

The parser generator produces:

### `SimpleCalcParser.g.cs` (abbreviated)

```csharp
// <auto-generated>
//   Parser generated by Yeetcode Parser Generator — do not edit.
// </auto-generated>
#nullable enable

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MyApp.Parsers;

public sealed class SimpleCalcParser
{
    private string _input = "";
    private int _pos;
    private int _line;
    private int _col;

    private static readonly Regex _NUMBER = new(@"[0-9]+(\.[0-9]+)?");
    private static readonly Regex _Skip = new(@"\s*");

    public Dictionary<string, object?> Parse(string input)
    {
        _input = input;
        _pos = 0;
        _line = 1;
        _col = 1;

        var result = Parse_expr();
        SkipWhitespace();
        if (_pos < _input.Length)
            throw new ParseException($"Unexpected input at {Location()}: '{Peek(20)}'");
        return result;
    }

    // ... (SkipWhitespace, TryConsume, etc. — same boilerplate) ...

    private Dictionary<string, object?>? Parse_expr()
    {
        var _captures = new Dictionary<string, object?>();
        var _saved = Save();

        // term
        {
            var _sub = Parse_term();
            if (_sub == null) goto _fail;
        }

        // (("+" | "-") term)*
        while (true)
        {
            var _repSaved = Save();

            // ("+" | "-")
            {
                var _choiceSaved = Save();
                do {
                    if (TryConsume("+") == null) goto _fail;
                    goto _choiceDone;
                } while (false);
                Restore(_choiceSaved);
                do {
                    if (TryConsume("-") == null) goto _fail;
                    goto _choiceDone;
                } while (false);
                goto _fail;
                _choiceDone:;
            }

            // term
            {
                var _sub = Parse_term();
                if (_sub == null) goto _fail;
            }

            continue;
            _fail: Restore(_repSaved); break;
        }

        return _captures;

    _fail:
        Restore(_saved);
        return null;
    }

    private Dictionary<string, object?>? Parse_term()
    {
        // ... same pattern: factor (("*" | "/") factor)* ...
    }

    private Dictionary<string, object?>? Parse_factor()
    {
        var _captures = new Dictionary<string, object?>();
        var _saved = Save();

        // number | "(" expr ")"
        {
            var _choiceSaved = Save();

            // Alternative 0: number
            do {
                var _sub = Parse_number();
                if (_sub == null) goto _fail;
                goto _choiceDone;
            } while (false);

            Restore(_choiceSaved);

            // Alternative 1: "(" expr ")"
            do {
                if (TryConsume("(") == null) goto _fail;
                var _sub = Parse_expr();
                if (_sub == null) goto _fail;
                if (TryConsume(")") == null) goto _fail;
                goto _choiceDone;
            } while (false);

            goto _fail;
            _choiceDone:;
        }

        return _captures;

    _fail:
        Restore(_saved);
        return null;
    }

    private Dictionary<string, object?>? Parse_number()
    {
        var _captures = new Dictionary<string, object?>();
        var _saved = Save();

        {
            var _tok = TryConsumeToken(_NUMBER);
            if (_tok == null) goto _fail;
            _captures["value"] = _tok;
        }

        return _captures;

    _fail:
        Restore(_saved);
        return null;
    }
}
```

Key properties of the generated parser:
- **One method per rule** — trivial to debug, stack trace shows exact parse position
- **PEG semantics** — ordered choice, committed once past first token, no ambiguity
- **Backtracking** via save/restore — only within choice alternatives
- **Named captures** go into `Dictionary<string, object?>` — the HJSON-compatible shape
- **Zero dependencies** — just `System`, `System.Collections.Generic`, `System.Text.RegularExpressions`

---

## 5. Schema Validation Layer

The generated parser produces raw dictionaries. A separate generated
validator applies the schema — filling defaults, checking required fields,
type-checking values.

The validator is also template-generated from the schema:

```
schema.hjson ──→ validator-csharp.yt ──→ MyLanguageValidator.g.cs
```

### What the validator does

1. **Walks the parsed dictionary** against the schema
2. **Fills defaults** for missing fields that have `= value` in the schema
3. **Rejects** missing required fields without defaults
4. **Type-checks** primitives — `int` fields contain ints, `bool` fields contain bools
5. **Validates `kind` discriminators** — `kind: @Thinker` matches an existing `@Type`
6. **Validates maps** — keys are strings, values conform to declared type
7. **Returns** a validated, fully-populated object tree

### Generated validator shape

```csharp
public sealed class MyLanguageValidator
{
    public ValidationResult Validate(Dictionary<string, object?> raw)
    {
        var errors = new List<string>();
        var result = new Dictionary<string, object?>();

        // Required field: crucible (string)
        if (raw.TryGetValue("crucible", out var _crucible) && _crucible is string)
            result["crucible"] = _crucible;
        else
            errors.Add("Missing required field 'crucible' (string)");

        // Default field: version (string = "3.0")
        if (raw.TryGetValue("version", out var _version) && _version is string)
            result["version"] = _version;
        else
            result["version"] = "3.0";

        // Optional field: description (string?)
        if (raw.TryGetValue("description", out var _description))
            result["description"] = _description;

        // Map field: enums ({})
        if (raw.TryGetValue("enums", out var _enums) && _enums is Dictionary<string, object?> enumMap)
        {
            var validatedEnums = new Dictionary<string, object?>();
            foreach (var (key, value) in enumMap)
            {
                // validate each enum value...
                validatedEnums[key] = value;
            }
            result["enums"] = validatedEnums;
        }

        // ... more fields ...

        return new ValidationResult(result, errors);
    }
}
```

---

## 6. Full Runtime Pipeline

Putting parser + validator together for Crucible interpreter mode:

```csharp
// Load and parse a .crucible file at runtime
var parser = new CrucibleParser();
var raw = parser.Parse(File.ReadAllText("devnull.crucible"));

// Validate against schema, fill defaults
var validator = new CrucibleValidator();
var result = validator.Validate(raw);

if (result.Errors.Count > 0)
{
    foreach (var err in result.Errors)
        Console.Error.WriteLine(err);
    return;
}

// Execute the validated crucible data
var engine = new CrucibleEngine();
var output = await engine.Execute(result.Data);
```

Both `CrucibleParser.g.cs` and `CrucibleValidator.g.cs` are generated
by Yeetcode at build time. At runtime they're just normal C# classes
with no dependencies on the Yeetcode toolchain.

---

## 7. Template Inventory

The parser generator ships these templates:

| Template | Output | Purpose |
|---|---|---|
| `parser-csharp.yt` | `{Name}Parser.g.cs` | C# recursive descent parser |
| `parser-typescript.yt` | `{name}-parser.g.ts` | TypeScript parser |
| `parser-rust.yt` | `{name}_parser.g.rs` | Rust parser |
| `validator-csharp.yt` | `{Name}Validator.g.cs` | C# schema validator |
| `validator-typescript.yt` | `{name}-validator.g.ts` | TypeScript schema validator |
| `validator-rust.yt` | `{name}_validator.g.rs` | Rust schema validator |

Adding a new target language: write one parser template and one validator
template. The grammar and schema don't change.

---

## 8. Command Line

```bash
# Generate a parser + validator from a grammar and schema
yeetcode generate-parser \
  --grammar my_language.grammar.yeet \
  --schema my_language.schema.hjson \
  --lang csharp \
  --namespace MyApp.Parsers \
  --outdir ./generated/

# Generate parser only (no validator — raw dictionary output)
yeetcode generate-parser \
  --grammar my_language.grammar.yeet \
  --lang typescript \
  --outdir ./generated/

# Test a grammar against input (uses built-in interpreter, not generated parser)
yeetcode test-parse \
  --grammar my_language.grammar.yeet \
  --schema my_language.schema.hjson \
  --input test_file.mylang \
  --output parsed.hjson
```

---

## 9. Bootstrap

Yeetcode's own grammar parser can be generated by this system:

```
grammar-grammar.yeet          ← grammar that parses .yeet files
grammar-schema.hjson           ← schema for grammar structure
parser-csharp.yt               ← parser template
         ↓
YeetGrammarParser.g.cs         ← generated parser for .yeet files
```

This generated parser produces the same output as Yeetcode's built-in
PEG interpreter — but as standalone C# code. The bootstrap sequence:

1. **Hand-write** a minimal PEG interpreter that can parse `.yeet` files
2. **Use it** to parse `grammar-grammar.yeet` into HJSON
3. **Run** the parser generator template on that HJSON
4. **Output**: `YeetGrammarParser.g.cs` — a generated parser for `.yeet` files
5. **Verify**: the generated parser produces identical output to the hand-written one
6. **Replace**: the hand-written interpreter with the generated one

From this point, Yeetcode's grammar parser is self-hosting. Changes to
the `.yeet` grammar syntax are made by editing `grammar-grammar.yeet` and
regenerating.

---

## 10. Design Principles

1. **Same grammar, two modes.** Build-time code generation and runtime parsing
   use identical `.yeet` files. No separate "parser spec" vs "codegen spec."

2. **Generated parsers are dumb code.** Recursive descent, one method per rule,
   dictionaries for captures. No parser framework, no runtime library, no
   dependencies. A junior developer can read the generated parser and understand it.

3. **Schema validation is separate from parsing.** The parser produces raw
   dictionaries. The validator applies the schema. This separation means you
   can use the parser without a schema (raw mode) or swap validators
   independently.

4. **PEG semantics everywhere.** Both the built-in interpreter and the generated
   parsers use ordered choice, committed match, and explicit backtracking. No
   ambiguity, no LR/LALR table generation, no shift-reduce conflicts. What you
   write is what executes.

5. **The generator eats itself.** The grammar-grammar is a `.yeet` file. The parser
   generator can produce the parser for `.yeet` files. This is the strongest
   possible test of the system — if it can parse its own grammar description
   format, it can parse anything.

6. **One template per target language.** Adding Rust support means writing
   `parser-rust.yt` and `validator-rust.yt`. The grammar doesn't change.
   The schema doesn't change. The HJSON intermediate doesn't change.

---

## 11. Relationship to Main Yeetcode Spec

| Concern | Main Spec | Parser Generator Spec |
|---|---|---|
| **Input** | Grammar + source file | Grammar + schema |
| **Pipeline** | parse → validate → template → code | parse grammar → template → parser source |
| **Output** | Application code (C# classes, etc.) | Parser + validator source code |
| **Runtime dependency** | None (generated code) | None (generated code) |
| **Template subject** | Domain data (messages, fields) | Grammar structure (rules, expressions) |
| **Recursion** | `@Types` in schema, `#define/#call` in templates | `@Expr` is recursive, `emit_expr` is recursive |
| **Schema role** | Validates domain data | Validates parsed output |
| **When to use** | "I want to generate code from this DSL" | "I want to parse this DSL at runtime" |

Both specs use the same grammar format, same schema format, same template
engine. The parser generator is just a specific application of the main
Yeetcode pipeline — where the "domain" happens to be grammar rules, and the
"generated code" happens to be a parser.

---

## License

MIT