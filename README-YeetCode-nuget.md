# ArtificialNecessity.YeetCode

(C)opyright 2026 by David Jeske <davidj@gmail.com>
Licensed under Apache 2.0

**Yeets one language into another.** Schema-driven meta-programming for language-to-language transformation.

YeetCode parses any custom syntax into validated HJSON intermediate data, then generates multi-file output through templates with configurable delimiters. Available as both a **CLI tool** and an **MSBuild task**.

## Installation

### NuGet Package (MSBuild Task)

```xml
<PackageReference Include="ArtificialNecessity.YeetCode.MSBuild" Version="0.1.*" PrivateAssets="all" />
```

### CLI Tool

```bash
dotnet tool install -g ArtificialNecessity.YeetCode
```

### VSCode Extension

Syntax highlighting for all YeetCode file formats (HJSON, YTSON, YTGMR, YTMPL) is available as a VSCode extension. See the [VSCode Extension README](https://github.com/ArtificialNecessity/AN_YeetCode/blob/main/YeetCode.VSCode/README.md) for details and screenshots.

---

## Two Ways to Use YeetCode

### Half Yeet — Data + Template → Output

The simplest mode. You already have your data in HJSON format. YeetCode just runs it through a template to produce output. No grammar, no parsing.

```
data.hjson → template.ytmpl → output files
```

**When to use:** Hand-written data, config files, lookup tables, or data exported from another tool.

### Full Yeet — Grammar + Input → Data → Template → Output

The full pipeline. YeetCode parses your custom syntax using a PEG grammar, validates the parsed data against a schema, then runs it through a template.

```
grammar.ytgmr + input.proto → data.hjson → template.ytmpl → output files
                                    ↑
                            schema.ytschema.ytson
                            (validates + fills defaults)
```

**When to use:** Parsing existing file formats (protobuf, IDL, config languages) into generated code.

---

## File Naming Convention

| Role     | Extension           | Example                   |
| -------- | ------------------- | ------------------------- |
| Grammar  | `.ytgmr`          | `proto.ytgmr`           |
| Schema   | `.ytschema.ytson` | `proto.ytschema.ytson`  |
| Template | `.ytmpl`          | `proto-csharp.ytmpl`    |
| Data     | `.ytdata.hjson`   | `widgets.ytdata.hjson`  |
| Input    | `.ytinput.ext`    | `widgets.ytinput.proto` |

The last extension always indicates the file format. The `yt` prefix identifies YeetCode-specific files. User input files keep their own format extension.

---

## CLI Usage

### Half Yeet (template command)

```bash
yeetcode template \
  --data greeting.ytdata.hjson \
  --template greeting.ytmpl \
  --output greeting.txt
```

### Full Yeet (generate command)

```bash
yeetcode generate \
  --schema proto.ytschema.ytson \
  --grammar proto.ytgmr \
  --input widgets.ytinput.proto \
  --template proto-csharp.ytmpl \
  --outdir ./generated/
```

### Other Commands

```bash
# Parse only — produce validated HJSON from input
yeetcode parse --schema s.ytson --grammar g.ytgmr --input i.proto --output parsed.hjson

# Validate — check HJSON data against schema
yeetcode validate --schema s.ytson --data d.hjson
```

---

## MSBuild Task Usage

Add the package reference, then use the tasks in your `.csproj`:

### Half Yeet (YeetCodeTemplateTask)

```xml
<Target Name="GenerateFromData" BeforeTargets="CoreCompile">
  <YeetCodeTemplateTask
    DataFile="greeting.ytdata.hjson"
    TemplateFile="greeting.ytmpl"
    OutputFile="generated\greeting.txt" />
</Target>
```

### Full Yeet (YeetCodeGenerateTask)

```xml
<Target Name="GenerateFromProto" BeforeTargets="CoreCompile">
  <YeetCodeGenerateTask
    SchemaFile="proto.ytschema.ytson"
    GrammarFile="proto.ytgmr"
    InputFile="widgets.ytinput.proto"
    TemplateFile="proto-csharp.ytmpl"
    OutputDirectory="generated\" />
</Target>
```

### Task Properties

**YeetCodeGenerateTask** (full yeet):

| Property            | Required | Description                            |
| ------------------- | -------- | -------------------------------------- |
| `SchemaFile`      | ✅       | Schema file (.ytschema.ytson)          |
| `GrammarFile`     | ✅       | Grammar file (.ytgmr)                  |
| `InputFile`       | ✅       | Input file to parse                    |
| `TemplateFile`    | ✅       | Template file (.ytmpl)                 |
| `FunctionsFile`   |          | Lookup tables (.hjson)                 |
| `OutputFile`      |          | Single-file output path                |
| `OutputDirectory` |          | Multi-file output directory            |
| `Defines`         |          | Semicolon-separated grammar parameters |

**YeetCodeTemplateTask** (half yeet):

| Property            | Required | Description                    |
| ------------------- | -------- | ------------------------------ |
| `DataFile`        | ✅       | HJSON data file                |
| `TemplateFile`    | ✅       | Template file (.ytmpl)         |
| `SchemaFile`      |          | Optional schema for validation |
| `FunctionsFile`   |          | Lookup tables (.hjson)         |
| `OutputFile`      |          | Single-file output path        |
| `OutputDirectory` |          | Multi-file output directory    |

---

## Example: Half Yeet — Greeting Generator

### Data (`greeting.ytdata.hjson`)

```hjson
{
  greeting: Hello
  name: World
  items: [
    { label: Alpha, value: 1 }
    { label: Beta, value: 2 }
    { label: Gamma, value: 3 }
  ]
}
```

### Template (`greeting.ytmpl`)

```
<?yt delim="<% %>" ?>
// Auto-generated — do not edit
<%greeting%>, <%name%>!

Items:
<%each items as item%>
  - <%item.label%> = <%item.value%>
<%/each%>
```

### Output

```
// Auto-generated — do not edit
Hello, World!

Items:
  - Alpha = 1
  - Beta = 2
  - Gamma = 3
```

### Run it

```bash
yeetcode template --data greeting.ytdata.hjson --template greeting.ytmpl
```

---

## Example: Full Yeet — Proto to C#

### Schema (`simple.ytschema.ytson`)

Defines the shape of the intermediate data:

```hjson
{
  syntax: string
  package: string
  messages: [{
    name: string
    fields: [{
      name: string
      type: string
      tag: int
    }]
  }]
}
```

### Grammar (`simple.ytgmr`)

PEG grammar that parses `.proto` files into the schema shape:

```
file ::= syntax_decl? package_decl? message*

syntax_decl ::= "syntax" "=" syntax:QUOTED_STRING ";"
package_decl ::= "package" package:IDENT ";"
message ::= "message" name:IDENT "{" fields:field* "}"
field ::= type:IDENT name:IDENT "=" tag:INT ";"

IDENT ::= /[a-zA-Z_][a-zA-Z0-9_.]+/
INT ::= /0|[1-9][0-9]*/
QUOTED_STRING ::= /"[^"]*"/

%skip ::= /(?:\s|\/\/[^\n]*)*/
```

### Input (`simple.ytinput.proto`)

```proto
syntax = "proto3";
package acme.widgets;

message Widget {
  string name = 1;
  int32 quantity = 2;
  string description = 3;
}
```

### Template (`simple.ytmpl`)

```
<?yt delim="<% %>" ?>
// Auto-generated from <%syntax%> — do not edit
// Package: <%package%>

<%each messages as msg%>
public class <%msg.name%>
{
    <%each msg.fields as f%>
    public <%f.type%> <%f.name%> { get; set; } // tag <%f.tag%>
    <%/each%>
}
<%/each%>
```

### Output

```csharp
// Auto-generated from "proto3" — do not edit
// Package: acme.widgets

public class Widget
{
    public string name { get; set; } // tag 1
    public int32 quantity { get; set; } // tag 2
    public string description { get; set; } // tag 3
}
```

### Run it

```bash
yeetcode generate \
  --schema simple.ytschema.ytson \
  --grammar simple.ytgmr \
  --input simple.ytinput.proto \
  --template simple.ytmpl
```

---

## Template Syntax

Templates use configurable delimiters declared in the header:

```
<?yt delim="<% %>" ?>
```

### Header Options

| Option                | Default | Description                                                    |
| --------------------- | ------- | -------------------------------------------------------------- |
| `delim="OPEN CLOSE"` | —       | **Required.** Delimiter pair for template blocks               |
| `trimlines=false`     | `true`  | Disable standalone directive line trimming (see below)         |

Example with trimlines disabled:
```
<?yt delim="<% %>" trimlines=false ?>
```

### Comments

Use `#` inside a delimited block to write a comment that produces no output:

```
<% # This is a YeetCode template comment — it won't appear in output %>
```

When a comment is the only thing on a line, the entire line (including its newline) is removed from output, so comments don't leave blank lines.

### Standalone Directive Line Trimming

By default, when a control directive (`each`, `if`, `else`, `/each`, `/if`, `define`, `call`, `output`, `# comment`, etc.) is the **only thing on a line** (besides whitespace), the entire line including its newline is removed from output. This prevents blank lines from control-flow directives.

**With trimming (default):**
```
<?yt delim="<% %>" ?>
Header
<% each items as item %>
- <% item.name %>
<% /each %>
Footer
```
Output: `Header\n- alpha\n- beta\nFooter\n` — no blank lines from `each`/`/each`.

**Without trimming:**
```
<?yt delim="<% %>" trimlines=false ?>
```
Output preserves the blank lines where directives were.

Value expressions like `<% name %>` are never trimmed — only control directives.

### Directives

| Directive                                    | Purpose                     |
| -------------------------------------------- | --------------------------- |
| `# comment text`                           | Comment (no output)         |
| `each collection as item`                  | Iterate array               |
| `each map as key, value`                   | Iterate map key-value pairs |
| `if condition`                             | Conditional                 |
| `elif condition`                           | Else-if                     |
| `else`                                     | Else                        |
| `define name(args)`                        | Define reusable macro       |
| `call name(args)`                          | Invoke macro                |
| `output filename`                          | Multi-file output           |
| `/each`, `/if`, `/define`, `/output` | Close blocks                |

### Built-in Functions

`pascal`, `camel`, `snake`, `upper`, `lower`, `pascal_dotted`, `length`

### Special Variables (inside `each`)

`index` (0-based), `first` (boolean), `last` (boolean)

---

## License

MIT
