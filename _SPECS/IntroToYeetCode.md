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

### Schema (`proto.schema.ytson`)

```hjson
{
  @Field: {
    type: string
    tag: int
    label: string = optional
  }

  package [optional]: string
  syntax: string = proto3
  messages [optional]: {
    @: { fields: { @: @Field } }
  }
}
```

`@Field` defines a reusable type. `[optional]` marks fields that may be absent. `= proto3` provides a default. `{ @: ... }` defines a map (named collection).

### Data (`widgets.hjson`)

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

This is validated against the schema. Missing defaults are filled in (`label: optional` for `quantity`). Type mismatches are caught. Required fields are enforced.

### Template (`csharp.yt`)

```
<?yt delim="<% %>" ?>
<% each messages as msg_name, msg %>
public class <% pascal msg_name %> {
    <% each msg.fields as fname, f %>
    <% if f.label == "repeated" %>
    public List<<% csharp_type[f.type] %>> <% pascal fname %> { get; set; } = new();
    <% else %>
    public <% csharp_type[f.type] %> <% pascal fname %> { get; set; }
    <% /if %>
    <% /each %>
}
<% /each %>
```

The first line declares delimiters (`<% %>`). Pick any pair that doesn't collide with your output language — `{% %}`, `[[ ]]`, whatever. Zero escaping needed.

### Output (`Widget.cs`)

```csharp
public class Widget {
    public string Name { get; set; }
    public int Quantity { get; set; }
    public List<string> Tags { get; set; } = new();
}
```

## Key Concepts

### Schema Types

| Syntax | Meaning |
|--------|---------|
| `string`, `int`, `float`, `bool` | Primitives |
| `@TypeName` | Reusable type reference |
| `[type]` | Array |
| `{ @: type }` | Map (named collection) |
| `{}` | Freeform object |
| `type = value` | Default value |
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