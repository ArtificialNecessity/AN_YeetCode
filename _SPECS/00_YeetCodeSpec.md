# Yeetcode

**Yeets one language into another.**

Yeetcode is a schema-driven meta-programming tool for language-to-language transformation. It parses any custom syntax into a validated HJSON intermediate representation, then generates multi-file output through templates with configurable delimiters.

The HJSON schema is the center of the system — the grammar maps *into* it, the template reads *out of* it. The schema is a real, human-readable, human-editable artifact, not an opaque AST.

## Pipeline

```
                    schema.hjson
                   ╱            ╲
                  ╱   validates   ╲
                 ╱                 ╲
grammar.yeet ──→ data.hjson ──→ template.yt ──→ output files
 (parse)         (validated)     (generate)       (N files)
```

1. **Schema** (`schema.hjson`) — defines the shape of the intermediate data, with typed fields, defaults, optional markers, and reusable recursive types
2. **Grammar** (`grammar.yeet`) — PEG grammar with named captures that maps parsed input into the schema
3. **Data** (`data.hjson`) — validated intermediate representation, human-readable and human-editable
4. **Template** (`template.yt`) — Clearsilver-heritage templates with custom delimiters and multi-file output

Each stage is independently inspectable and debuggable. The data.hjson can be hand-written, hand-edited after generation, or produced entirely by the grammar. The template doesn't know or care where the data came from.

---

## 1. Schema

The schema defines the contract between the grammar and the template. It declares field names, types, optionality, defaults, and reusable type definitions.

### Primitive Types

- `string` — text value
- `int` — integer
- `float` — floating point
- `bool` — true/false

### Type References

The `@` prefix defines and references user types. In a key position it declares a type definition. In a value position it references one.

```hjson
{
  # Definition: @MessageField is a reusable type
  @MessageField: {
    name: string
    type: string
    tag: int
    label [default:optional]: string
  }

  # Reference: fields contains an array of @MessageField
  messages: [{
    name: string
    fields: [@MessageField]
  }]
}
```

### Optionality

A trailing `?` marks a field or type as optional. Non-optional fields are guaranteed present — the grammar must populate them, or the schema fills them from defaults.

```hjson
{
  @Function: {
    name: string                # required — always present
    return_type [default:void]: string  # required, has default
    doc_comment: string?        # optional — may be absent
    params: [{
      name: string
      type: string
      default_value: string?    # optional per-param
    }]
    body: [@Statement]?         # optional — declaration vs definition
  }
}
```

### Freeform Objects

`{}` allows arbitrary key-value pairs when the schema can't predict the structure:

```hjson
{
  @Annotation: {
    name: string
    arguments: {}?    # optional freeform key-value pairs
  }
}
```

### Recursive Types

Types can reference themselves. This is how you represent trees, expressions, nested structures — anything with arbitrary depth.

Each variant in a discriminated union is extracted into its own named `@Type`. The `kind` field holds a type reference (`@TypeName`) that identifies which variant is active, and the system validates that the referenced type matches one of the optional branches.

```hjson
{
  # Variant types for @Expression
  @Binary: {
    op: string
    left: @Expression
    right: @Expression
  }

  @Literal: {
    value: string
    type: string
  }

  @Call: {
    function: string
    args: [@Expression]
  }

  @Unary: {
    op: string
    operand: @Expression
  }

  # Discriminated union — kind is always a @Type reference
  @Expression: {
    kind: string          # @Binary | @Literal | @Call | @Unary
    binary: @Binary?
    literal: @Literal?
    call: @Call?
    unary: @Unary?
  }

  # Variant types for @Statement
  @Assign: {
    target: string
    value: @Expression
  }

  @If: {
    condition: @Expression
    body: [@Statement]
    else_body: [@Statement]?
  }

  @Return: {
    value: @Expression?
  }

  @Block: {
    statements: [@Statement]
  }

  @Statement: {
    kind: string          # @Assign | @If | @Return | @Block
    assign: @Assign?
    if: @If?
    return: @Return?
    block: @Block?
  }
}
```

The `kind` field holds a `@Type` reference that acts as a discriminator. Only the matching variant branch is populated — others are absent. Because the discriminator is a real type reference (not an opaque string), the system can validate at parse time that the `kind` value matches one of the optional branches and that the branch's type is correct. This is a discriminated union expressed in plain HJSON, not algebraic types.

### Arrays

Square brackets denote arrays. An array of objects uses `[{...}]`. An array of type references uses `[@TypeName]`. An array of primitives uses `[string]`, `[int]`, etc.

```hjson
{
  tags: [string]             # array of strings
  scores: [int]              # array of ints
  items: [{ name: string }]  # array of inline objects
  fields: [@MessageField]    # array of typed objects
}
```

### Maps

Curly braces with a type denote maps — HJSON objects whose keys are strings and values conform to a type. Maps enforce key uniqueness and enable O(1) lookup by key.

```hjson
{
  enums: {@Enum}?            # map of enum name → @Enum
  messages: {@Message}?      # map of message name → @Message
  options: {string}?         # map of option name → string value
}
```

Maps are the natural choice for named, lookup-heavy collections. Use arrays when ordering matters or elements are anonymous. Use maps when elements have unique names and you need to look them up by name.

In templates, maps support both dot access for literal keys and bracket access for dynamic keys:

```
<%enums.WidgetType%>         # literal key — known at template-write time
<%enums[f.type]%>            # dynamic key — resolved at template-eval time
```

Maps are iterated with `#each` using key-value destructuring:

```
<%#each enums as name, values%>
public enum <%pascal name%> { ... }
<%/each%>
```

In grammars, the `[name]` path notation maps parsed data into map keys:

```
enum_decl ::= "enum" name:IDENT "{" enum_body* "}"
  -> enums[name]
```

### Defaults

The `[default:value]` key attribute provides a default value. If the grammar doesn't populate the field, the schema fills it automatically. The template never sees missing required fields.

```hjson
{
  @Field: {
    name: string
    type: string
    label [default:optional]: string
    deprecated [default:false]: bool
    wire_type [default:varint]: string
  }
}
```

---

## 2. Grammar

Yeetcode grammars are PEG (Parsing Expression Grammars) with named captures and schema-targeted output rules. PEG provides deterministic parsing — ordered choice, no ambiguity.

### Basic Syntax

```
# Rules — lowercase names
rule_name ::= expression

# Lexer tokens — UPPERCASE names
TOKEN_NAME ::= /regex/

# Implicit whitespace/comment skipping
%skip ::= /pattern/
```

### Grammar Directives

| Directive | Purpose |
|---|---|
| `%skip: /pattern/` | Whitespace/comment pattern, consumed implicitly between tokens |
| `%define name` | Declare a grammar parameter, supplied via `--define name=value` |
| `%if expr` / `%else` / `%endif` | Preprocessor conditional — resolves before parsing, dead branches removed |
| `%parse_file(capture, order, dups, target)` | Parse another file during the parse (see Multi-File Input) |
| `-> @Type` | Map rule output to a schema type |
| `-> @Type { kind: @Variant }` | Map rule output with discriminator |
| `-> path[]` | Append to array at schema path |
| `-> path[capture]` | Insert into map at schema path, keyed by capture |

### Expressions

| Syntax | Meaning |
|--------|---------|
| `"text"` | Literal string match |
| `IDENT` | Token reference |
| `rule` | Rule reference (recursive descent) |
| `a b c` | Sequence — match all in order |
| `a \| b \| c` | Ordered choice — try left to right, take first match |
| `e*` | Zero or more |
| `e+` | One or more |
| `e?` | Optional |
| `(a b)` | Grouping |
| `name:e` | Named capture — `name` becomes a key in the output |

### Schema Mapping

The `->` arrow maps a rule's output to a schema type. Named captures auto-map to fields by name matching.

```
# Simple — captures map to @MessageField.name, @MessageField.type, etc.
field ::= label:LABEL? type:IDENT name:IDENT "=" tag:INT ";"
  -> @MessageField

# With discriminator — kind references the variant @Type
binary ::= left:expression op:OP right:expression
  -> @Expression { kind: @Binary }

call ::= function:IDENT "(" args:expression_list ")"
  -> @Expression { kind: @Call }

literal ::= value:LITERAL
  -> @Expression { kind: @Literal }
```

When a rule specifies `-> @Type`, the following happens:

1. A new instance of `@Type` is created
2. Named captures are matched to fields by name
3. If a capture name matches a field in the active variant, it's placed there
4. Type validation occurs — a capture from a rule that produces `@Expression` must map to a field declared as `@Expression`
5. Defaults from the schema fill any unpopulated required fields
6. Missing required fields without defaults cause a parse error

### Schema Path Targeting

For rules that populate nested positions in the document, use path notation:

```
file ::= package_decl? (message | enum)*

package_decl ::= "package" package:IDENT ";"

message ::= "message" name:IDENT "{" fields:field* "}"
  -> messages[]

field ::= label:LABEL? type:IDENT name:IDENT "=" tag:INT ";"
  -> messages[].fields[]
```

`messages[]` means "append a new object to the `messages` array." The parser knows context from lexical nesting — `field` appears inside `message`, so `messages[].fields[]` refers to the current message's fields array.

### Lexer Primitives

Tokens use regex patterns and are matched before parser rules:

```
IDENT: /[a-zA-Z_][a-zA-Z0-9_]*/
INT: /0|[1-9][0-9]*/
FLOAT: /-?(?:0|[1-9]\d*)(?:\.\d+)(?:[eE][+-]?\d+)?/
QUOTED_STRING: /"(?:[^"\\]|\\.)*"/
MULTILINE_STRING: /'''(?:(?!''').)*'''/s
```

### Whitespace and Comments

The `%skip` directive defines patterns consumed implicitly between tokens:

```
%skip: /(?:\s|#[^\n]*|\/\/[^\n]*)*/
```

### Grammar Parameters

`%define` declares parameters that are supplied at invocation time via `--define`. Combined with `%if` / `%else` / `%endif`, they enable grammar variants without separate grammar files.

```
%define syntax

field_decl:
  %if syntax == "proto2"
    label:LABEL type:IDENT fname:IDENT "=" tag:INT field_options? ";"
  %else
    label:LABEL? type:IDENT fname:IDENT "=" tag:INT field_options? ";"
  %endif
  -> messages[name].fields[fname]
  -> @Field
```

```bash
yeetcode generate --grammar proto.grammar.yeet --define syntax=proto3 ...
```

`%if` / `%else` / `%endif` are **preprocessor directives**, not runtime conditionals. They resolve before parsing begins — the grammar that actually executes has one code path, no ambiguity. The parser never sees the dead branch.

### Multi-File Input

`%parse_file` triggers parsing of additional input files during the parse. It references a capture for the filename and specifies merge behavior.

```
%parse_file(capture, order, duplicates, target_path)
```

| Argument | Values | Meaning |
|---|---|---|
| `capture` | any capture name | Which capture holds the file path |
| `order` | `now` / `fifo` / `lifo` | When to parse: immediately (depth-first), queued after current file, or deferred stack |
| `duplicates` | `skip` / `error` | If file was already parsed: silently skip, or fail |
| `target_path` | `_` / any path | Where to merge the imported file's HJSON tree |

**Target path `_`** merges into the document root. Imported file's top-level map keys union with the current tree. Key collisions are validation errors (maps enforce uniqueness):

```
# Protobuf-style: imported enums/messages merge into the global namespace
import_decl ::= "import" path:QUOTED_STRING ";"
  %parse_file(path, now, skip, _)
  -> imports[]
```

Importing `google/protobuf/timestamp.proto` parses that file, and its `enums`, `messages`, `services` maps merge directly into the current file's maps.

**Named target path** isolates the import under a specific key:

```
# Namespace-style: each imported file lands under its own key
import_decl ::= "import" path:QUOTED_STRING ";"
  %parse_file(path, now, skip, dependencies[path])
  -> imports[]
```

The imported file's entire tree lands at `dependencies["google/protobuf/timestamp.proto"]`. No collision possible — each file gets its own namespace. Templates access imported types with `dependencies[import_path].messages[type_name]`.

**Any arbitrary path** works:

```
# Merge into a shared section
include_decl ::= "include" path:QUOTED_STRING ";"
  %parse_file(path, now, skip, includes.shared_types)
```

File search paths are supplied at invocation:

```bash
yeetcode generate \
  --grammar proto.grammar.yeet \
  --input widgets.proto \
  --import-path ./proto/ \
  --import-path /usr/share/proto/ \
  ...
```

The engine resolves the captured path against `--import-path` directories in order. The imported file is parsed using the **same grammar** — recursive imports work naturally (depth-first with `order: now`, breadth-first with `order: fifo`).

### Alternatives and Discriminators

When a rule has alternatives that produce different types or variants:

- **Keyword alternatives** become string values automatically:
  ```
  primitive: "int32" | "string" | "bool" | "float"
  ```
  Produces just the matched string.

- **Rule alternatives** that target different variants use per-alternative `->`:
  ```
  expression ::= binary | call | literal

  binary ::= left:expression op:OP right:expression
    -> @Expression { kind: @Binary }

  call ::= function:IDENT "(" args:expression_list ")"
    -> @Expression { kind: @Call }

  literal ::= value:LITERAL
    -> @Expression { kind: @Literal }
  ```

### Full Grammar Example — Protobuf

```
# === Grammar for a protobuf-like language ===

file ::= syntax_decl? package_decl? (message | enum)*

syntax_decl ::= "syntax" "=" version:QUOTED_STRING ";"

package_decl ::= "package" package:IDENT ";"

message ::= "message" name:IDENT "{" fields:field* "}"
  -> messages[]

field ::= label:LABEL? type:IDENT name:IDENT "=" tag:INT options:field_options? ";"
  -> messages[].fields[]

field_options ::= "[" option ("," option)* "]"
option ::= key:IDENT "=" value:(IDENT | QUOTED_STRING | INT)

enum ::= "enum" name:IDENT "{" values:enum_value* "}"
  -> enums[]

enum_value ::= name:IDENT "=" number:INT ";"
  -> enums[].values[]

# Lexer
LABEL ::= "required" | "optional" | "repeated"
IDENT ::= /[a-zA-Z_][a-zA-Z0-9_.]+/
INT ::= /0|[1-9][0-9]*/
QUOTED_STRING ::= /"(?:[^"\\]|\\.)*"/

%skip ::= /(?:\s|\/\/[^\n]*|\/\*[\s\S]*?\*\/)*/
```

---

## 3. Template

Yeetcode templates generate output files from validated HJSON data. They use Clearsilver-heritage syntax with two critical features: **configurable delimiters** and **multi-file output**.

### Custom Delimiters

The first line of a template declares its delimiters. This eliminates escaping — pick delimiters that don't appear in your output language.

```
<?yt delim="<% %>" ?>
```

Generating C# or JavaScript? Use `<% %>`. Generating XML? Use `{% %}`. Generating ERB templates? Use `[[ ]]`. The template language has zero reserved syntax until you declare it.

```
<?yt delim="<% %>" ?>
<% each messages as msg_name, msg %>
public class <% pascal msg_name %> {
    <% each msg.fields as fname, f %>
    public <% csharp_type[f.type] %> <% pascal fname %> { get; set; }
    <% /each %>
}
<% /each %>
```

### Single-File vs Multi-File Output

Output mode is implicit — the presence of `#output` directives determines the mode. No mode declaration needed.

**Single-file** — template has no `output` directives. All output goes to the file named by `--output` on the CLI, or to stdout:

```
<?yt delim="<% %>" ?>
public class <% pascal name %>
{
    <% each fields as fname, f %>
    public <% csharp_type[f.type] %> <% pascal fname %> { get; set; }
    <% /each %>
}
```

```bash
yeetcode generate --template simple.yt --output Widget.cs
```

**Multi-file** — template uses `output` directives. Each `output` block routes content to a named file under `--outdir`:

```
<?yt delim="<% %>" ?>
<% each messages as msg_name, msg %>

<% output pascal(msg_name) + ".cs" %>
using System;

public class <% pascal msg_name %> {
    <% each msg.fields as fname, f %>
    public <% csharp_type[f.type] %> <% pascal fname %> { get; set; }
    <% /each %>
}
<% /output %>

<% output pascal(msg_name) + ".java" %>
package <% package %>;

public class <% pascal msg_name %> {
    <% each msg.fields as fname, f %>
    private <% java_type[f.type] %> <% camel fname %>;
    <% /each %>
}
<% /output %>

<% /each %>
```

```bash
yeetcode generate --template multi.yt --outdir ./generated/
```

**Error rules** — no footguns:

| Situation | Result |
|---|---|
| Template has `output` but CLI passes `--output` | Error: "multi-file template requires --outdir" |
| Template has `output` and content outside any `output` block | Error: "content outside output in multi-file template" |
| Template has no `output` but CLI passes `--outdir` | OK — writes single output to `--outdir/` with default name |
| Template has `output` and CLI passes `--outdir` | OK — each `output` creates a file under `--outdir` |

### Template Directives

Directives are keyword-first inside delimiters. No `#` prefix needed — the parser recognizes directives by the leading keyword.

| Directive | Purpose |
|-----------|---------|
| `each array as item` | Iterate over an array |
| `each map as key, value` | Iterate over a map's key-value pairs |
| `if condition` | Conditional block |
| `elif condition` | Else-if branch |
| `else` | Else branch |
| `define name(args)` | Define a reusable template macro |
| `call name(args)` | Invoke a macro (supports recursion) |
| `output expr` | Direct output to a named file (expr is evaluated) |
| `/each`, `/if`, `/define`, `/output` | Close blocks |

### Value Expressions

Values are emitted by placing a path inside delimiters. Two access modes:

- **Dot** for literal keys known at template-write time: `msg.name`, `enums.WidgetType`
- **Brackets** for dynamic keys resolved at template-eval time: `enums[f.type]`, `csharp_type[f.type]`

```
<%msg.name%>                  # literal field access
<%msg.fields.length%>         # array/map length
<%csharp_type[f.type]%>       # dynamic map lookup
<%enums[f.type]%>             # dynamic existence + value
<%pascal msg.name%>           # built-in function call
```

Bracket lookups also serve as existence checks in `#if`:

```
<%#if enums[f.type]%>
  ...type is an enum...
<%#elif csharp_type[f.type]%>
  ...type is a scalar...
<%#else%>
  ...type is a message ref...
<%/if%>
```

If the key doesn't exist in the map, the bracket lookup evaluates to absent (falsy in `#if`, empty string in value position). This is the primary mechanism for cross-referencing data — no special query functions needed.

### Optionality in Templates

Accessing optional fields follows the `?.` convention. The template engine validates paths against the schema at **template compile time**, not runtime.

```
# COMPILE ERROR — doc_comment is optional in schema, bare access forbidden
<%func.doc_comment%>

# OK — ?. acknowledges optionality, emits nothing if absent
<%func.doc_comment?.value%>

# OK — branch first, then access is safe inside the block
<%#if func.doc_comment%>
/// <%func.doc_comment%>
<%/if%>
```

The rule: if the schema marks a field with `?`, the template must use `?.` for inline access or `#if` to branch. Bare access to an optional field is a compile error. This guarantees the template never hits a null at runtime.

### Recursive Templates

The `#define` / `#call` mechanism supports recursion, which is required for recursive `@Types`:

```
<?yt delim="<% %>" ?>
<%#define render_expr(e)%>
<%#if e.kind == @Binary%>
(<%#call render_expr(e.binary.left)%> <%e.binary.op%> <%#call render_expr(e.binary.right)%>)
<%#elif e.kind == @Literal%>
<%e.literal.value%>
<%#elif e.kind == @Call%>
<%e.call.function%>(<%#each e.call.args as arg separator=", "%><%#call render_expr(arg)%><%/each%>)
<%/if%>
<%/define%>
```

### Built-in Functions

Templates can call built-in transformation functions:

| Function | Purpose |
|----------|---------|
| `upper(s)` | UPPER_CASE |
| `lower(s)` | lower_case |
| `pascal(s)` | PascalCase |
| `camel(s)` | camelCase |
| `snake(s)` | snake_case |
| `length(arr)` | Array length |
| `index` | Current iteration index (0-based) inside `#each` |
| `first` | Boolean, true on first iteration |
| `last` | Boolean, true on last iteration |

### User-Defined Lookup Tables

Type mapping and other custom transformations are defined in a functions file as maps. They're accessed using bracket notation in templates:

```hjson
# functions.hjson — lookup tables
{
  csharp_type: {
    int32: "int"
    int64: "long"
    string: "string"
    bool: "bool"
    float: "float"
    double: "double"
    bytes: "byte[]"
  }

  java_type: {
    int32: "int"
    int64: "long"
    string: "String"
    bool: "boolean"
    float: "float"
    double: "double"
    bytes: "byte[]"
  }
}
```

Used in templates with bracket notation:

```
<%csharp_type[f.type]%>     # looks up f.type in the csharp_type map
<%java_type[f.type]%>       # looks up f.type in the java_type map
```

Lookup tables are also usable in `#if` — a missing key evaluates to absent:

```
<%#if csharp_type[f.type]%>
  ...it's a known scalar type...
<%/if%>
```

### Separator Support

`#each` supports a `separator` attribute for comma-separated lists and similar patterns:

```
<%#each params as p separator=", "%><%p.type%> <%p.name%><%/each%>
```

Produces: `int x, string y, float z` — separator emitted between items, not after the last one.

---

## 4. Command Line

```bash
# Full pipeline — single-file output
yeetcode generate \
  --schema schema.hjson \
  --grammar grammar.yeet \
  --input source.txt \
  --template template.yt \
  --functions functions.hjson \
  --output result.cs

# Full pipeline — multi-file output (template uses #output directives)
yeetcode generate \
  --schema schema.hjson \
  --grammar grammar.yeet \
  --input source.proto \
  --template template.yt \
  --functions functions.hjson \
  --outdir ./generated/

# With grammar parameters
yeetcode generate \
  --schema schema.hjson \
  --grammar proto.grammar.yeet \
  --define syntax=proto3 \
  --input widgets.proto \
  --template proto-csharp.yt \
  --outdir ./generated/

# With import search paths (for grammars using %parse_file)
yeetcode generate \
  --schema schema.hjson \
  --grammar proto.grammar.yeet \
  --input widgets.proto \
  --import-path ./proto/ \
  --import-path /usr/share/proto/ \
  --template proto-csharp.yt \
  --outdir ./generated/

# Parse only — produce validated HJSON from input
yeetcode parse \
  --schema schema.hjson \
  --grammar grammar.yeet \
  --input source.proto \
  --output parsed.hjson

# Generate from existing HJSON — skip parsing, just run templates
yeetcode template \
  --schema schema.hjson \
  --data parsed.hjson \
  --template template.yt \
  --functions functions.hjson \
  --outdir ./generated/

# Validate — check data against schema without generating
yeetcode validate \
  --schema schema.hjson \
  --data parsed.hjson

# Check template — compile-time validation against schema
yeetcode check \
  --schema schema.hjson \
  --template template.yt
```

---

## 5. Complete Example — Protobuf to C# and Java

### Schema (`proto.schema.hjson`)

```hjson
{
  @Field: {
    type: string
    tag: int
    label [default:optional]: string
    deprecated [default:false]: bool
  }

  package: string?
  syntax [default:proto3]: string
  messages: {
    @: {
      fields: {@Field}
    }
  }?
  enums: {}?
}
```

### Input (`example.proto`)

```proto
syntax = "proto3";
package acme.widgets;

message Widget {
  required string name = 1;
  optional int32 quantity = 2;
  repeated string tags = 3;
}

enum WidgetType {
  UNKNOWN = 0;
  SPROCKET = 1;
  GEAR = 2;
}
```

### Parsed Data (`example.hjson`)

After `yeetcode parse`, the validated HJSON looks like:

```hjson
{
  syntax: proto3
  package: acme.widgets
  messages: {
    Widget: {
      fields: {
        name:     { type: string, tag: 1, label: required, deprecated: false }
        quantity: { type: int32,  tag: 2, label: optional, deprecated: false }
        tags:     { type: string, tag: 3, label: repeated, deprecated: false }
      }
    }
  }
  enums: {
    WidgetType: {
      UNKNOWN: 0
      SPROCKET: 1
      GEAR: 2
    }
  }
}
```

This file is human-readable, human-editable, and self-describing. Defaults have been filled (`deprecated: false`). Every name is a map key — no `name` fields inside objects. Any hand edits here will be validated against the schema before templates run.

### Template (`csharp.yt`)

```
<?yt delim="<% %>" ?>
<%#each messages as msg_name, msg%>
<%#output pascal(msg_name) + ".cs"%>
// Auto-generated by Yeetcode — do not edit
using System;
using System.Collections.Generic;

<%#if package%>
namespace <%pascal_dotted package%>
{
<%/if%>
    public class <%pascal msg_name%>
    {
        <%#each msg.fields as fname, f%>
        <%#if f.label == "repeated"%>
        public List<<%csharp_type[f.type]%>> <%pascal fname%> { get; set; } = new();
        <%#else%>
        public <%csharp_type[f.type]%> <%pascal fname%> { get; set; }
        <%/if%>
        <%/each%>
    }
<%#if package%>
}
<%/if%>
<%/output%>
<%/each%>

<%#each enums as enum_name, values%>
<%#output pascal(enum_name) + ".cs"%>
// Auto-generated by Yeetcode — do not edit
<%#if package%>
namespace <%pascal_dotted package%>
{
<%/if%>
    public enum <%pascal enum_name%>
    {
        <%#each values as name, number%>
        <%name%> = <%number%>,
        <%/each%>
    }
<%#if package%>
}
<%/if%>
<%/output%>
<%/each%>
```

### Output

Running `yeetcode generate` with the above produces:

**`Widget.cs`**
```csharp
// Auto-generated by Yeetcode — do not edit
using System;
using System.Collections.Generic;

namespace Acme.Widgets
{
    public class Widget
    {
        public string Name { get; set; }
        public int Quantity { get; set; }
        public List<string> Tags { get; set; } = new();
    }
}
```

**`WidgetType.cs`**
```csharp
// Auto-generated by Yeetcode — do not edit
namespace Acme.Widgets
{
    public enum WidgetType
    {
        UNKNOWN = 0,
        SPROCKET = 1,
        GEAR = 2,
    }
}
```

---

## 6. Design Principles

1. **Schema is the contract.** The grammar maps into it, the template reads out of it. Both are validated against it. The schema is the single source of truth for data shape.

2. **Fail loudly by default.** Accessing an optional field without `?.` or `#if` is a compile error. Missing required fields without defaults are parse errors. Type mismatches between grammar captures and schema fields are caught at parse time.

3. **Everything is inspectable.** The HJSON intermediate is a real file you can read, edit, diff, and version control. It's not an opaque binary AST.

4. **No collisions, ever.** Custom template delimiters eliminate escaping. Pick delimiters that don't appear in your output language. The template language has zero reserved syntax until you declare it.

5. **Multi-file output is first-class.** A single template produces N output files. This is the missing primitive that every other template engine lacks or bolts on as an afterthought.

6. **Recursive types are natural.** `@Type` references in the schema support arbitrary recursion. `#define` / `#call` in templates support recursive rendering. Trees, expressions, and nested structures are not special cases.

7. **Defaults eliminate null checks.** The schema declares defaults for required fields. The template never handles missing values for non-optional fields. This is a static guarantee, not a runtime convention.

8. **The HJSON is editable.** Someone can skip the grammar entirely, hand-write HJSON conforming to the schema, and run templates. Or parse with the grammar and hand-edit the result. The pipeline stages are independent.

9. **Maps over arrays for named things.** Named, unique, lookup-heavy collections use maps — HJSON objects keyed by name. This eliminates `name` fields inside objects, enforces uniqueness, enables O(1) lookup, and lets templates resolve cross-references by path existence.

10. **Dot for literal, brackets for dynamic.** `enums.WidgetType` accesses a known key. `enums[f.type]` accesses a dynamic key. Bracket lookups double as existence checks in `#if` — no special query functions needed.

---

## 7. Bootstrapping

Yeetcode's own schema and grammar files are HJSON. This creates a bootstrapping requirement — the first HJSON parser must be hand-written. Once that exists, the system can parse its own schema definitions and grammar definitions, because they're both HJSON.

The bootstrap sequence:

1. Hand-write a minimal HJSON parser
2. Use it to parse `hjson.schema.hjson` (the HJSON schema schema)
3. Use it to parse `hjson.grammar.yeet` (the HJSON grammar)
4. The system can now parse any HJSON, including its own configuration files
5. From here, everything is self-hosting

---

## License

MIT