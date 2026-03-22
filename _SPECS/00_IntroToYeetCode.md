# Intro to YeetCode

**Yeets one language into another.**

YeetCode is a schema-driven code generator. You define a data shape (schema), write a parser (grammar) or hand-craft data (HJSON), and run it through templates to produce output files. Every stage is human-readable and independently debuggable.

## The Pipeline

```
grammar.yeet ──→ data.hjson ──→ template.yt ──→ output files
  (parse)          (validated)     (generate)       (N files)
                       ↑
                 schema.ytson
                  (validates)
```

**Schema** defines the data shape. **Grammar** parses input into that shape. **Template** reads the data and generates output. The data in the middle is plain HJSON — you can read it, edit it, diff it, version it.

## Quick Example

### INPUT (`widgets.proto`)

```proto
syntax = "proto3";
package acme.widgets;

message Widget {
  required string name = 1;
  int32 quantity = 2;
  repeated string tags = 3;
}
```

### OUTPUT (`Widget.cs`)

```csharp
using System.Collections.Generic;

namespace Acme.Widgets;

public class Widget
{
    public string Name { get; set; } = "";
    public int Quantity { get; set; }
    public List<string> Tags { get; set; } = new();

    public void Encode(IWriter writer)
    {
        writer.WriteString(1, Name);
        writer.WriteInt32(2, Quantity);
        foreach (var tag in Tags) {
            writer.WriteString(3, tag);
        }
    }

    public static Widget Decode(IReader reader)
    {
        var widget = new Widget();
        while (reader.ReadTag(out int tag)) {
            switch (tag) {
                case 1: widget.Name = reader.ReadString(); break;
                case 2: widget.Quantity = reader.ReadInt32(); break;
                case 3: widget.Tags.Add(reader.ReadString()); break;
                default: reader.SkipField(); break;
            }
        }
        return widget;
    }
}
```

---

## How YeetCode Gets There

YeetCode transforms the input through three artifacts: a **grammar** (parses input), a **schema** (validates data shape), and a **template** (generates output).

### Grammar (`proto.grammar.yeet`)

Parses the input syntax into structured data:

```
# Simplified protobuf grammar
file ::= syntax_decl? package_decl? message*

syntax_decl ::= "syntax" "=" version:QUOTED_STRING ";"

package_decl ::= "package" package:IDENT ";"

message ::= "message" name:IDENT "{" fields:field* "}"
  -> messages[name]

field ::= label:LABEL? type:IDENT name:IDENT "=" tag:INT ";"
  -> messages[].fields[name]
  -> @Field

LABEL ::= "required" | "optional" | "repeated"
IDENT ::= /[a-zA-Z_][a-zA-Z0-9_.]*/
INT ::= /[0-9]+/
QUOTED_STRING ::= /"[^"]*"/

%skip ::= /\s+/
```

The `->` arrows map parsed data into the schema structure. `messages[name]` inserts into a map keyed by the captured `name`. `@Field` creates an instance of the `@Field` type.

### 1. Schema (`proto.schema.ytson`)

Defines the shape of the intermediate data:

```hjson
{
  @Field: {
    type: string
    tag: int
    label [optional, default:optional]: string
  }

  package [optional]: string
  syntax [default:proto3]: string
  messages [optional]: {
    @: { fields: { @: @Field } }
  }
}
```

`@Field` defines a reusable type. `[optional]` marks fields that may be absent. `[default:value]` provides a default. `{ @: ... }` defines a map (named collection).

### 2. Data (`widgets.hjson`)

The input is parsed by a grammar (not shown) into this validated HJSON:

```hjson
{
  package: acme.widgets
  messages: {
    Widget: {
      fields: {
        name:     { type: string, tag: 1, label: required }
        quantity: { type: int32,  tag: 2 }
        tags:     { type: string, tag: 3, label: repeated }
      }
    }
  }
}
```

Validated against the schema. Missing defaults filled in (`label: optional` for `quantity`).

### 3. Template (`csharp.yt`)

Reads the data and generates output:

```
<?yt delim="<% %>" ?>
<% each messages as msg_name, msg %>
using System.Collections.Generic;

namespace <% pascal_dotted package %>;

public class <% pascal msg_name %>
{
    <% each msg.fields as fname, f %>
    <% if f.label == "repeated" %>
    public List<<% csharp_type[f.type] %>> <% pascal fname %> { get; set; } = new();
    <% else %>
    public <% csharp_type[f.type] %> <% pascal fname %> { get; set; }<% if f.label == "required" %> = ""<% /if %>;
    <% /if %>
    <% /each %>

    public void Encode(IWriter writer)
    {
        <% each msg.fields as fname, f %>
        <% if f.label == "repeated" %>
        foreach (var item in <% pascal fname %>) {
            writer.Write<% pascal csharp_type[f.type] %>(<% f.tag %>, item);
        }
        <% else %>
        writer.Write<% pascal csharp_type[f.type] %>(<% f.tag %>, <% pascal fname %>);
        <% /if %>
        <% /each %>
    }

    public static <% pascal msg_name %> Decode(IReader reader)
    {
        var obj = new <% pascal msg_name %>();
        while (reader.ReadTag(out int tag)) {
            switch (tag) {
                <% each msg.fields as fname, f %>
                <% if f.label == "repeated" %>
                case <% f.tag %>: obj.<% pascal fname %>.Add(reader.Read<% pascal csharp_type[f.type] %>()); break;
                <% else %>
                case <% f.tag %>: obj.<% pascal fname %> = reader.Read<% pascal csharp_type[f.type] %>(); break;
                <% /if %>
                <% /each %>
                default: reader.SkipField(); break;
            }
        }
        return obj;
    }
}
<% /each %>
```

The first line declares delimiters (`<% %>`). Pick any pair that doesn't collide with your output language. Zero escaping needed.


## Key Concepts

### Schema Types

| Syntax | Meaning |
|--------|---------|
| `string`, `int`, `float`, `bool` | Primitives |
| `@TypeName` | Reusable type reference |
| `[type]` | Array |
| `{ @: type }` | Map (named collection) |
| `{}` | Freeform object |
| `[default:value]` | Default value (key attribute) |
| `[optional]` | Key attribute — field may be absent |

### Template Syntax

Templates are plain text with delimited expressions. The parser recognizes directives by keyword:

| Inside delimiters | What it does |
|---|---|
| `each collection as item` | Iterate array |
| `each map as key, value` | Iterate map |
| `if condition` | Conditional |
| `elif condition` / `else` | Branches |
| `/if`, `/each` | Close blocks |
| `output expr` / `/output` | Multi-file output |
| `define name(args)` / `call name(args)` | Macros (recursive) |
| `expr` | Evaluate and emit |
| `func expr` | Built-in function call |

Built-in functions: `pascal`, `camel`, `snake`, `upper`, `lower`, `length`, `pascal_dotted`

### Lookup Tables

Type mappings and other transforms live in a separate HJSON file:

```hjson
{
  csharp_type: {
    int32: int
    string: string
    bool: bool
  }
}
```

Used in templates with bracket notation: `<% csharp_type[f.type] %>`. Missing keys evaluate to absent (falsy in `if`, empty in value position).

### Multi-File Output

A single template can produce multiple files using `output` blocks:

```
<?yt delim="<% %>" ?>
<% each messages as name, msg %>
<% output pascal(name) + ".cs" %>
// content for this file
<% /output %>
<% /each %>
```

Run with `--outdir ./generated/` and each `output` block creates a separate file.

## File Extensions

| Extension | Format |
|-----------|--------|
| `.hjson` | Standard HJSON (data files) |
| `.ytson` | HJSON + key attributes (schema files) |
| `.yt` | YeetCode template |
| `.yeet` | PEG grammar |

## Design Principles

1. **Schema is the contract** — grammar maps into it, template reads out of it
2. **Everything is inspectable** — the HJSON intermediate is a real, editable file
3. **Custom delimiters** — zero escaping, pick delimiters that don't collide with output
4. **Multi-file output is first-class** — one template, N output files
5. **Defaults eliminate null checks** — required fields always have values
6. **Maps over arrays** — named things use maps for O(1) lookup and uniqueness
7. **Fail loudly** — type mismatches, missing fields, and optional access errors are caught early