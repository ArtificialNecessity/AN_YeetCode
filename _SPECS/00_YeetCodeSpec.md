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
    label: string = "optional"    # default value
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
    return_type: string = "void"  # required, has default
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

```hjson
{
  @Expression: {
    kind: string
    binary: {
      op: string
      left: @Expression
      right: @Expression
    }?
    literal: {
      value: string
      type: string
    }?
    call: {
      function: string
      args: [@Expression]
    }?
    unary: {
      op: string
      operand: @Expression
    }?
  }

  @Statement: {
    kind: string
    assign: {
      target: string
      value: @Expression
    }?
    if: {
      condition: @Expression
      body: [@Statement]
      else_body: [@Statement]?
    }?
    return: {
      value: @Expression?
    }?
    block: {
      statements: [@Statement]
    }?
  }
}
```

The `kind` field acts as a discriminator tag. Only the matching variant branch is populated — others are absent. This is a discriminated union expressed in plain HJSON, not algebraic types.

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

### Defaults

`= value` after the type provides a default. If the grammar doesn't populate the field, the schema fills it automatically. The template never sees missing required fields.

```hjson
{
  @Field: {
    name: string
    type: string
    label: string = "optional"
    deprecated: bool = false
    wire_type: string = "varint"
  }
}
```

---

## 2. Grammar

Yeetcode grammars are PEG (Parsing Expression Grammars) with named captures and schema-targeted output rules. PEG provides deterministic parsing — ordered choice, no ambiguity.

### Basic Syntax

```
# Rules — lowercase names
rule_name: expression

# Lexer tokens — UPPERCASE names
TOKEN_NAME: /regex/

# Implicit whitespace/comment skipping
%skip: /pattern/
```

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
field: label:LABEL? type:IDENT name:IDENT "=" tag:INT ";"
  -> @MessageField

# With discriminator — sets the kind tag for variant types
binary: left:expression op:OP right:expression
  -> @Expression { kind: "binary" }

call: function:IDENT "(" args:expression_list ")"
  -> @Expression { kind: "call" }

literal: value:LITERAL
  -> @Expression { kind: "literal" }
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
file: package_decl? (message | enum)*

package_decl: "package" package:IDENT ";"

message: "message" name:IDENT "{" fields:field* "}"
  -> messages[]

field: label:LABEL? type:IDENT name:IDENT "=" tag:INT ";"
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

### Alternatives and Discriminators

When a rule has alternatives that produce different types or variants:

- **Keyword alternatives** become string values automatically:
  ```
  primitive: "int32" | "string" | "bool" | "float"
  ```
  Produces just the matched string.

- **Rule alternatives** that target different variants use per-alternative `->`:
  ```
  expression: binary | call | literal

  binary: left:expression op:OP right:expression
    -> @Expression { kind: "binary" }

  call: function:IDENT "(" args:expression_list ")"
    -> @Expression { kind: "call" }

  literal: value:LITERAL
    -> @Expression { kind: "literal" }
  ```

### Full Grammar Example — Protobuf

```
# === Grammar for a protobuf-like language ===

file: syntax_decl? package_decl? (message | enum)*

syntax_decl: "syntax" "=" version:QUOTED_STRING ";"

package_decl: "package" package:IDENT ";"

message: "message" name:IDENT "{" fields:field* "}"
  -> messages[]

field: label:LABEL? type:IDENT name:IDENT "=" tag:INT options:field_options? ";"
  -> messages[].fields[]

field_options: "[" option ("," option)* "]"
option: key:IDENT "=" value:(IDENT | QUOTED_STRING | INT)

enum: "enum" name:IDENT "{" values:enum_value* "}"
  -> enums[]

enum_value: name:IDENT "=" number:INT ";"
  -> enums[].values[]

# Lexer
LABEL: "required" | "optional" | "repeated"
IDENT: /[a-zA-Z_][a-zA-Z0-9_.]+/
INT: /0|[1-9][0-9]*/
QUOTED_STRING: /"(?:[^"\\]|\\.)*"/

%skip: /(?:\s|\/\/[^\n]*|\/\*[\s\S]*?\*\/)*/
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
<%#each messages as msg%>
public class <%msg.name%> {
    <%#each msg.fields as f%>
    public <%csharp_type f.type%> <%f.name%> { get; set; }
    <%/each%>
}
<%/each%>
```

### Multi-File Output

The `#output` directive routes output to named files. A single template can produce any number of output files.

```
<?yt delim="<% %>" ?>
<%#each messages as msg%>

<%#output "<%msg.name%>.cs" %>
using System;

public class <%msg.name%> {
    <%#each msg.fields as f%>
    public <%csharp_type f.type%> <%f.name%> { get; set; }
    <%/each%>
}
<%/output%>

<%#output "<%msg.name%>.java" %>
package <%package%>;

public class <%msg.name%> {
    <%#each msg.fields as f%>
    private <%java_type f.type%> <%f.name%>;
    <%/each%>
}
<%/output%>

<%/each%>
```

### Template Directives

| Directive | Purpose |
|-----------|---------|
| `#each collection as item` | Iterate over an array |
| `#if condition` | Conditional block |
| `#elif condition` | Else-if branch |
| `#else` | Else branch |
| `#define name(args)` | Define a reusable template macro |
| `#call name(args)` | Invoke a macro (supports recursion) |
| `#output "path"` | Direct output to a named file |
| `/each`, `/if`, `/define`, `/output` | Close blocks |

### Value Expressions

Values are emitted by placing a dotted path inside delimiters:

```
<%msg.name%>                  # field access
<%msg.fields.length%>         # array length
<%csharp_type msg.type%>      # function call
```

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
<%#if e.kind == "binary"%>
(<%#call render_expr(e.binary.left)%> <%e.binary.op%> <%#call render_expr(e.binary.right)%>)
<%#elif e.kind == "literal"%>
<%e.literal.value%>
<%#elif e.kind == "call"%>
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

### User-Defined Functions

Type mapping and other custom transformations are defined in a functions file:

```hjson
# functions.hjson — type mapping tables and transforms
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

Used in templates as simple lookups:

```
<%csharp_type f.type%>     # looks up f.type in the csharp_type table
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
# Full pipeline — parse input, validate against schema, generate output
yeetcode generate \
  --schema schema.hjson \
  --grammar grammar.yeet \
  --input source.proto \
  --template template.yt \
  --functions functions.hjson \
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
    name: string
    type: string
    tag: int
    label: string = "optional"
    deprecated: bool = false
  }

  @EnumValue: {
    name: string
    number: int
  }

  package: string?
  syntax: string = "proto3"
  messages: [{
    name: string
    fields: [@Field]
  }]
  enums: [{
    name: string
    values: [@EnumValue]
  }]?
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
  messages: [
    {
      name: Widget
      fields: [
        { name: name,     type: string, tag: 1, label: required, deprecated: false }
        { name: quantity,  type: int32,  tag: 2, label: optional, deprecated: false }
        { name: tags,      type: string, tag: 3, label: repeated, deprecated: false }
      ]
    }
  ]
  enums: [
    {
      name: WidgetType
      values: [
        { name: UNKNOWN,  number: 0 }
        { name: SPROCKET, number: 1 }
        { name: GEAR,     number: 2 }
      ]
    }
  ]
}
```

This file is human-readable, human-editable, and self-describing. Defaults have been filled (`deprecated: false`). Any hand edits here will be validated against the schema before templates run.

### Template (`csharp.yt`)

```
<?yt delim="<% %>" ?>
<%#each messages as msg%>
<%#output "<%pascal msg.name%>.cs"%>
// Auto-generated by Yeetcode — do not edit
using System;
using System.Collections.Generic;

namespace <%package?.replace(".", ".")%>
{
    public class <%pascal msg.name%>
    {
        <%#each msg.fields as f%>
        <%#if f.label == "repeated"%>
        public List<<%csharp_type f.type%>> <%pascal f.name%> { get; set; } = new();
        <%#else%>
        public <%csharp_type f.type%> <%pascal f.name%> { get; set; }
        <%/if%>
        <%/each%>
    }
}
<%/output%>
<%/each%>

<%#each enums as e%>
<%#output "<%pascal e.name%>.cs"%>
// Auto-generated by Yeetcode — do not edit
namespace <%package?.replace(".", ".")%>
{
    public enum <%pascal e.name%>
    {
        <%#each e.values as v%>
        <%v.name%> = <%v.number%>,
        <%/each%>
    }
}
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

namespace acme.widgets
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
namespace acme.widgets
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