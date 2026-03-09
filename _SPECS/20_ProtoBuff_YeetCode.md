# Protocol Buffers → C# via Yeetcode (v2 — data-oriented)

Flat, human-readable HJSON intermediate. No AST wrappers, no discriminated
unions for field types. The template resolves enum-vs-message by looking
sideways at the enums map.

---

## 1. Schema (`proto.schema.hjson`)

```hjson
{
  @Field: {
    type: string              # int32, string, Widget, WidgetType — anything
    tag: int
    label: string = "optional"  # required, optional, repeated
    deprecated: bool = false
  }

  @MapField: {
    key_type: string          # int32, string, bool, etc.
    value_type: string        # any type
    tag: int
  }

  @OneofBranch: {
    type: string
    tag: int
  }

  @RpcMethod: {
    input: string
    output: string
    client_streaming: bool = false
    server_streaming: bool = false
  }

  @Option: {
    value: string
  }

  # ── Document root ─────────────────────────────

  syntax: string = "proto3"
  package: string?
  imports: [string]?
  options: {@Option}?

  enums: {
    # WidgetType: { UNKNOWN: 0, SPROCKET: 1, GEAR: 2 }
  }?

  messages: {
    # Widget: { fields: {...}, maps: {...}, oneofs: {...}, ... }
    @: {
      fields: {@Field}?
      maps: {@MapField}?
      oneofs: {
        # variant: { color: { type: string, tag: 8 }, ... }
        @: {@OneofBranch}
      }?
      nested_enums: {}?        # same shape as top-level enums
      nested_messages: {}?     # recursive — same shape as messages
      reserved_tags: [int]?
      reserved_names: [string]?
      options: {@Option}?
    }
  }?

  services: {
    # WidgetService: { GetWidget: { input: ..., output: ... }, ... }
    @: {@RpcMethod}
  }?
}
```

Notes:
- `@:` in the schema means "any key" — the map key is the name, no `name` field inside
- Enums are just `{ NAME: number }` — the flattest possible representation
- Fields are a map keyed by field name — `{ name: { type: string, tag: 1 } }`
- Map fields live in a separate `maps` section because they have different structure (key_type + value_type)
- Oneofs are a map of maps — the outer key is the oneof name, inner keys are branch names
- The template resolves whether `type: WidgetType` is an enum or message by checking `enums.WidgetType`

---

## 2. Functions (`proto-csharp.functions.hjson`)

```hjson
{
  csharp_type: {
    int32: int
    int64: long
    uint32: uint
    uint64: ulong
    sint32: int
    sint64: long
    fixed32: uint
    fixed64: ulong
    sfixed32: int
    sfixed64: long
    float: float
    double: double
    bool: bool
    string: string
    bytes: "Google.Protobuf.ByteString"
  }

  csharp_default: {
    int32: "0"
    int64: "0L"
    uint32: "0"
    uint64: "0UL"
    sint32: "0"
    sint64: "0L"
    fixed32: "0"
    fixed64: "0UL"
    sfixed32: "0"
    sfixed64: "0L"
    float: "0F"
    double: "0D"
    bool: "false"
    string: "\"\""
    bytes: "Google.Protobuf.ByteString.Empty"
  }

  write_method: {
    int32: WriteInt32
    int64: WriteInt64
    uint32: WriteUInt32
    uint64: WriteUInt64
    sint32: WriteSInt32
    sint64: WriteSInt64
    fixed32: WriteFixed32
    fixed64: WriteFixed64
    sfixed32: WriteSFixed32
    sfixed64: WriteSFixed64
    float: WriteFloat
    double: WriteDouble
    bool: WriteBool
    string: WriteString
    bytes: WriteBytes
  }

  read_method: {
    int32: ReadInt32
    int64: ReadInt64
    uint32: ReadUInt32
    uint64: ReadUInt64
    sint32: ReadSInt32
    sint64: ReadSInt64
    fixed32: ReadFixed32
    fixed64: ReadFixed64
    sfixed32: ReadSFixed32
    sfixed64: ReadSFixed64
    float: ReadFloat
    double: ReadDouble
    bool: ReadBool
    string: ReadString
    bytes: ReadBytes
  }
}
```

---

## 3. Grammar (`proto.grammar.yeet`)

```
# ═══════════════════════════════════════════════
# Top level
# ═══════════════════════════════════════════════

file: syntax_decl import_decl* package_decl? file_option*
      (message_decl | enum_decl | service_decl)*

syntax_decl: "syntax" "=" syntax:QUOTED_STRING ";"

package_decl: "package" package:PACKAGE_NAME ";"

import_decl: "import" ("weak" | "public")? path:QUOTED_STRING ";"
  -> imports[]

file_option: "option" name:OPTION_NAME "=" value:OPTION_VALUE ";"
  -> options[name]
  -> @Option

# ═══════════════════════════════════════════════
# Enums — flat map of name → number
# ═══════════════════════════════════════════════

enum_decl: "enum" name:IDENT "{" enum_body* "}"
  -> enums[name]

enum_body: enum_value | option_decl | reserved_decl | ";"

enum_value: vname:IDENT "=" number:INT field_options? ";"
  -> enums[name][vname] = number

# ═══════════════════════════════════════════════
# Messages — map of name → message structure
# ═══════════════════════════════════════════════

message_decl: "message" name:IDENT "{" message_body* "}"
  -> messages[name]

message_body: field_decl | map_decl | oneof_decl
            | nested_message | nested_enum
            | reserved_decl | option_decl | ";"

field_decl: label:LABEL? type:IDENT fname:IDENT "=" tag:INT field_options? ";"
  -> messages[name].fields[fname]
  -> @Field

map_decl: "map" "<" key_type:MAP_KEY_TYPE "," value_type:IDENT ">"
          fname:IDENT "=" tag:INT field_options? ";"
  -> messages[name].maps[fname]
  -> @MapField

oneof_decl: "oneof" oname:IDENT "{" oneof_branch+ "}"

oneof_branch: type:IDENT bname:IDENT "=" tag:INT ";"
  -> messages[name].oneofs[oname][bname]
  -> @OneofBranch

nested_message: "message" nname:IDENT "{" message_body* "}"
  -> messages[name].nested_messages[nname]

nested_enum: "enum" ename:IDENT "{" enum_body* "}"
  -> messages[name].nested_enums[ename]

reserved_decl: "reserved" reserved_ranges ";"
reserved_ranges: reserved_range ("," reserved_range)*
reserved_range: INT ("to" (INT | "max"))? | QUOTED_STRING

option_decl: "option" name:OPTION_NAME "=" value:OPTION_VALUE ";"

field_options: "[" field_option ("," field_option)* "]"
field_option: OPTION_NAME "=" OPTION_VALUE

# ═══════════════════════════════════════════════
# Services — map of name → map of method name → method
# ═══════════════════════════════════════════════

service_decl: "service" name:IDENT "{" service_body* "}"
  -> services[name]

service_body: rpc_decl | option_decl | ";"

rpc_decl: "rpc" mname:IDENT
          "(" client_streaming:"stream"? input:QUALIFIED_IDENT ")"
          "returns"
          "(" server_streaming:"stream"? output:QUALIFIED_IDENT ")"
          (";" | "{" option_decl* "}")
  -> services[name][mname]
  -> @RpcMethod

# ═══════════════════════════════════════════════
# Lexer
# ═══════════════════════════════════════════════

LABEL: "required" | "optional" | "repeated"
SCALAR_TYPE: "double" | "float" | "int32" | "int64" | "uint32" | "uint64"
           | "sint32" | "sint64" | "fixed32" | "fixed64" | "sfixed32" | "sfixed64"
           | "bool" | "string" | "bytes"
MAP_KEY_TYPE: "int32" | "int64" | "uint32" | "uint64" | "sint32" | "sint64"
            | "fixed32" | "fixed64" | "sfixed32" | "sfixed64" | "bool" | "string"
IDENT: /[a-zA-Z_][a-zA-Z0-9_]*/
QUALIFIED_IDENT: /\.?[a-zA-Z_][a-zA-Z0-9_]*(\.[a-zA-Z_][a-zA-Z0-9_]*)*/
PACKAGE_NAME: /[a-zA-Z_][a-zA-Z0-9_]*(\.[a-zA-Z_][a-zA-Z0-9_]*)*/
INT: /-?(?:0[xX][0-9a-fA-F]+|0[0-7]*|[1-9][0-9]*)/
QUOTED_STRING: /"(?:[^"\\]|\\.)*"/
OPTION_NAME: /\(?[a-zA-Z_][a-zA-Z0-9_.]*\)?(\.[a-zA-Z_][a-zA-Z0-9_]*)*/
OPTION_VALUE: /(?:"(?:[^"\\]|\\.)*"|[a-zA-Z_][a-zA-Z0-9_]*|-?[0-9]+(?:\.[0-9]+)?)/

%skip: /(?:\s|\/\/[^\n]*|\/\*[\s\S]*?\*\/)*/
```

Key grammar changes:
- `-> enums[name]` puts the enum into a map keyed by its parsed name
- `-> enums[name][vname] = number` puts the value directly as a key-number pair
- `-> messages[name].fields[fname]` nests the field into the message's field map
- No `@Field` discriminated union — all fields are `@Field`, the template resolves type category
- Services are a map of maps — `services[name][mname]`

---

## 4. Template (`proto-csharp.yt`)

```
<?yt delim="<% %>" ?>

<%# ═══════════════════════════════════════════ %>
<%# Helper: is this type a scalar?              %>
<%# ═══════════════════════════════════════════ %>

<%#define is_scalar(type)%>
<%#if csharp_type.<%type%>%>true<%/if%>
<%/define%>

<%# ═══════════════════════════════════════════ %>
<%# Helper: is this type an enum?               %>
<%# ═══════════════════════════════════════════ %>

<%#define is_enum(type)%>
<%#if enums.<%type%>%>true<%/if%>
<%/define%>

<%# ═══════════════════════════════════════════ %>
<%# Helper: C# type for any proto type          %>
<%# Scalars → lookup, enums → pascal,           %>
<%# messages → pascal + nullable                 %>
<%# ═══════════════════════════════════════════ %>

<%#define cs_type(type)%>
<%#if csharp_type.<%type%>%><%csharp_type type%><%#else%><%pascal type%><%/if%>
<%/define%>

<%#define cs_default(type)%>
<%#if csharp_default.<%type%>%><%csharp_default type%><%#elif enums.<%type%>%>0<%#else%>null<%/if%>
<%/define%>

<%# ═══════════════════════════════════════════ %>
<%# One .cs per message                         %>
<%# ═══════════════════════════════════════════ %>

<%#each messages as msg_name, msg%>
<%#output "<%pascal msg_name%>.g.cs"%>
// <auto-generated>
//   Generated by Yeetcode from <%package%>.proto
// </auto-generated>
#nullable enable

using System;
using System.Collections.Generic;
using Google.Protobuf;
using Google.Protobuf.Collections;

<%#if package%>
namespace <%pascal_dotted package%>
{
<%/if%>
<%#call emit_message(msg_name, msg, "    ")%>
<%#if package%>
}
<%/if%>
<%/output%>
<%/each%>

<%# ═══════════════════════════════════════════ %>
<%# One .cs per enum                            %>
<%# ═══════════════════════════════════════════ %>

<%#each enums as enum_name, values%>
<%#output "<%pascal enum_name%>.g.cs"%>
// <auto-generated>
//   Generated by Yeetcode from <%package%>.proto
// </auto-generated>

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

<%# ═══════════════════════════════════════════ %>
<%# One .cs per service                         %>
<%# ═══════════════════════════════════════════ %>

<%#each services as svc_name, methods%>
<%#output "<%pascal svc_name%>Grpc.g.cs"%>
// <auto-generated>
//   Generated by Yeetcode from <%package%>.proto
// </auto-generated>

using System.Threading.Tasks;
using Grpc.Core;

<%#if package%>
namespace <%pascal_dotted package%>
{
<%/if%>
    public static partial class <%pascal svc_name%>
    {
<%#each methods as method_name, m%>
        private const string <%pascal method_name%>MethodName = "/<%package%>.<%svc_name%>/<%method_name%>";
<%/each%>

        public abstract partial class <%pascal svc_name%>ClientBase : ClientBase<<%pascal svc_name%>ClientBase>
        {
<%#each methods as method_name, m%>
<%#if !m.client_streaming && !m.server_streaming%>
            public virtual AsyncUnaryCall<<%pascal m.output%>> <%pascal method_name%>Async(
                <%pascal m.input%> request, CallOptions options = default)
                => CallInvoker.AsyncUnaryCall(<%pascal method_name%>Method, null, options, request);
<%#elif !m.client_streaming && m.server_streaming%>
            public virtual AsyncServerStreamingCall<<%pascal m.output%>> <%pascal method_name%>(
                <%pascal m.input%> request, CallOptions options = default)
                => CallInvoker.AsyncServerStreamingCall(<%pascal method_name%>Method, null, options, request);
<%/if%>
<%/each%>
        }

        public abstract partial class <%pascal svc_name%>Base
        {
<%#each methods as method_name, m%>
<%#if !m.client_streaming && !m.server_streaming%>
            public virtual Task<<%pascal m.output%>> <%pascal method_name%>(
                <%pascal m.input%> request, ServerCallContext context)
                => throw new RpcException(new Status(StatusCode.Unimplemented, ""));
<%#elif !m.client_streaming && m.server_streaming%>
            public virtual Task <%pascal method_name%>(
                <%pascal m.input%> request,
                IServerStreamWriter<<%pascal m.output%>> responseStream,
                ServerCallContext context)
                => throw new RpcException(new Status(StatusCode.Unimplemented, ""));
<%/if%>
<%/each%>
        }
    }
<%#if package%>
}
<%/if%>
<%/output%>
<%/each%>

<%# ═══════════════════════════════════════════════ %>
<%# Message emission (recursive for nested)         %>
<%# ═══════════════════════════════════════════════ %>

<%#define emit_message(msg_name, msg, indent)%>
<%indent%>public sealed partial class <%pascal msg_name%> : IMessage<<%pascal msg_name%>>, IBufferMessage
<%indent%>{
<%# ── Field numbers ── %>
<%#if msg.fields%>
<%#each msg.fields as fname, f%>
<%indent%>    public const int <%pascal fname%>FieldNumber = <%f.tag%>;
<%/each%>
<%/if%>
<%#if msg.maps%>
<%#each msg.maps as fname, f%>
<%indent%>    public const int <%pascal fname%>FieldNumber = <%f.tag%>;
<%/each%>
<%/if%>
<%#if msg.oneofs%>
<%#each msg.oneofs as oname, branches%>
<%#each branches as bname, b%>
<%indent%>    public const int <%pascal bname%>FieldNumber = <%b.tag%>;
<%/each%>
<%/each%>
<%/if%>

<%# ── Field declarations ── %>
<%#if msg.fields%>
<%#each msg.fields as fname, f%>
<%#call emit_field(fname, f, indent + "    ")%>
<%/each%>
<%/if%>

<%# ── Map declarations ── %>
<%#if msg.maps%>
<%#each msg.maps as fname, f%>
<%indent%>    private readonly MapField<<%#call cs_type(f.key_type)%>, <%#call cs_type(f.value_type)%>> _<%camel fname%> = new();
<%indent%>    public MapField<<%#call cs_type(f.key_type)%>, <%#call cs_type(f.value_type)%>> <%pascal fname%> => _<%camel fname%>;

<%/each%>
<%/if%>

<%# ── Oneof declarations ── %>
<%#if msg.oneofs%>
<%#each msg.oneofs as oname, branches%>
<%indent%>    public enum <%pascal oname%>OneofCase
<%indent%>    {
<%indent%>        None = 0,
<%#each branches as bname, b%>
<%indent%>        <%pascal bname%> = <%b.tag%>,
<%/each%>
<%indent%>    }

<%indent%>    private <%pascal oname%>OneofCase _<%camel oname%>Case = <%pascal oname%>OneofCase.None;
<%indent%>    private object? _<%camel oname%>Value;
<%indent%>    public <%pascal oname%>OneofCase <%pascal oname%>Case => _<%camel oname%>Case;

<%#each branches as bname, b%>
<%indent%>    public <%#call cs_type(b.type)%> <%pascal bname%>
<%indent%>    {
<%indent%>        get => _<%camel oname%>Case == <%pascal oname%>OneofCase.<%pascal bname%> ? (<%#call cs_type(b.type)%>)_<%camel oname%>Value! : <%#call cs_default(b.type)%>;
<%indent%>        set { _<%camel oname%>Value = value; _<%camel oname%>Case = <%pascal oname%>OneofCase.<%pascal bname%>; }
<%indent%>    }

<%/each%>
<%indent%>    public void Clear<%pascal oname%>()
<%indent%>    {
<%indent%>        _<%camel oname%>Case = <%pascal oname%>OneofCase.None;
<%indent%>        _<%camel oname%>Value = null;
<%indent%>    }
<%/each%>
<%/if%>

<%# ── Constructor ── %>
<%indent%>    public <%pascal msg_name%>() { }

<%# ── Copy constructor ── %>
<%indent%>    public <%pascal msg_name%>(<%pascal msg_name%> other)
<%indent%>    {
<%#if msg.fields%>
<%#each msg.fields as fname, f%>
<%#call emit_copy(fname, f, indent + "        ")%>
<%/each%>
<%/if%>
<%#if msg.maps%>
<%#each msg.maps as fname, f%>
<%indent%>        _<%camel fname%>.Add(other._<%camel fname%>);
<%/each%>
<%/if%>
<%indent%>    }

<%indent%>    public <%pascal msg_name%> Clone() => new(this);

<%# ── WriteTo ── %>
<%indent%>    public void WriteTo(CodedOutputStream output)
<%indent%>    {
<%#if msg.fields%>
<%#each msg.fields as fname, f%>
<%#call emit_write(fname, f, indent + "        ")%>
<%/each%>
<%/if%>
<%indent%>    }

<%# ── CalculateSize ── %>
<%indent%>    public int CalculateSize()
<%indent%>    {
<%indent%>        int size = 0;
<%#if msg.fields%>
<%#each msg.fields as fname, f%>
<%#call emit_size(fname, f, indent + "        ")%>
<%/each%>
<%/if%>
<%indent%>        return size;
<%indent%>    }

<%# ── MergeFrom (deserialize) ── %>
<%indent%>    public void MergeFrom(CodedInputStream input)
<%indent%>    {
<%indent%>        uint tag;
<%indent%>        while ((tag = input.ReadTag()) != 0)
<%indent%>        {
<%indent%>            switch (tag)
<%indent%>            {
<%#if msg.fields%>
<%#each msg.fields as fname, f%>
<%#call emit_read_case(fname, f, indent + "                ")%>
<%/each%>
<%/if%>
<%indent%>                default:
<%indent%>                    input.SkipLastField();
<%indent%>                    break;
<%indent%>            }
<%indent%>        }
<%indent%>    }

<%# ── MergeFrom (other instance) ── %>
<%indent%>    public void MergeFrom(<%pascal msg_name%> other)
<%indent%>    {
<%#if msg.fields%>
<%#each msg.fields as fname, f%>
<%#call emit_merge(fname, f, indent + "        ")%>
<%/each%>
<%/if%>
<%indent%>    }

<%# ── Nested enums ── %>
<%#if msg.nested_enums%>
<%#each msg.nested_enums as ename, evalues%>

<%indent%>    public enum <%pascal ename%>
<%indent%>    {
<%#each evalues as vname, vnumber%>
<%indent%>        <%vname%> = <%vnumber%>,
<%/each%>
<%indent%>    }
<%/each%>
<%/if%>

<%# ── Nested messages (recursive) ── %>
<%#if msg.nested_messages%>
<%#each msg.nested_messages as nname, nmsg%>

<%#call emit_message(nname, nmsg, indent + "    ")%>
<%/each%>
<%/if%>
<%indent%>}
<%/define%>

<%# ═══════════════════════════════════════════════ %>
<%# Field declaration — type resolved here          %>
<%# ═══════════════════════════════════════════════ %>

<%#define emit_field(fname, f, indent)%>
<%#if f.label == "repeated"%>
<%indent%>private readonly RepeatedField<<%#call cs_type(f.type)%>> _<%camel fname%> = new();
<%indent%>public RepeatedField<<%#call cs_type(f.type)%>> <%pascal fname%> => _<%camel fname%>;

<%#elif enums.<%f.type%>%>
<%# ── Enum field ── %>
<%indent%>private <%pascal f.type%> _<%camel fname%> = 0;
<%indent%>public <%pascal f.type%> <%pascal fname%>
<%indent%>{
<%indent%>    get => _<%camel fname%>;
<%indent%>    set => _<%camel fname%> = value;
<%indent%>}

<%#elif csharp_type.<%f.type%>%>
<%# ── Scalar field ── %>
<%indent%>private <%csharp_type f.type%> _<%camel fname%> = <%csharp_default f.type%>;
<%indent%>public <%csharp_type f.type%> <%pascal fname%>
<%indent%>{
<%indent%>    get => _<%camel fname%>;
<%indent%>    set => _<%camel fname%> = value;
<%indent%>}

<%#else%>
<%# ── Message ref field ── %>
<%indent%>private <%pascal f.type%>? _<%camel fname%>;
<%indent%>public <%pascal f.type%>? <%pascal fname%>
<%indent%>{
<%indent%>    get => _<%camel fname%>;
<%indent%>    set => _<%camel fname%> = value;
<%indent%>}

<%/if%>
<%/define%>

<%# ═══════════════════════════════════════════════ %>
<%# Write field                                     %>
<%# ═══════════════════════════════════════════════ %>

<%#define emit_write(fname, f, indent)%>
<%#if f.label == "repeated"%>
<%indent%>_<%camel fname%>.WriteTo(output, _repeated_<%camel fname%>_codec);
<%#elif enums.<%f.type%>%>
<%indent%>if (_<%camel fname%> != 0)
<%indent%>{
<%indent%>    output.WriteRawTag(<%f.tag%>);
<%indent%>    output.WriteEnum((int)_<%camel fname%>);
<%indent%>}
<%#elif csharp_type.<%f.type%>%>
<%indent%>if (_<%camel fname%> != <%csharp_default f.type%>)
<%indent%>{
<%indent%>    output.WriteRawTag(<%f.tag%>);
<%indent%>    output.<%write_method f.type%>(_<%camel fname%>);
<%indent%>}
<%#else%>
<%indent%>if (_<%camel fname%> != null)
<%indent%>{
<%indent%>    output.WriteRawTag(<%f.tag%>);
<%indent%>    output.WriteMessage(_<%camel fname%>);
<%indent%>}
<%/if%>
<%/define%>

<%# ═══════════════════════════════════════════════ %>
<%# Size calculation                                %>
<%# ═══════════════════════════════════════════════ %>

<%#define emit_size(fname, f, indent)%>
<%#if f.label != "repeated"%>
<%#if enums.<%f.type%>%>
<%indent%>if (_<%camel fname%> != 0)
<%indent%>    size += CodedOutputStream.ComputeTagSize(<%f.tag%>) + CodedOutputStream.ComputeEnumSize((int)_<%camel fname%>);
<%#elif csharp_type.<%f.type%>%>
<%indent%>if (_<%camel fname%> != <%csharp_default f.type%>)
<%indent%>    size += CodedOutputStream.ComputeTagSize(<%f.tag%>) + CodedOutputStream.Compute<%pascal f.type%>Size(_<%camel fname%>);
<%#else%>
<%indent%>if (_<%camel fname%> != null)
<%indent%>    size += CodedOutputStream.ComputeTagSize(<%f.tag%>) + CodedOutputStream.ComputeMessageSize(_<%camel fname%>);
<%/if%>
<%/if%>
<%/define%>

<%# ═══════════════════════════════════════════════ %>
<%# Deserialization switch cases                    %>
<%# ═══════════════════════════════════════════════ %>

<%#define emit_read_case(fname, f, indent)%>
<%#if f.label != "repeated"%>
<%#if enums.<%f.type%>%>
<%indent%>case <%f.tag%>u:
<%indent%>    _<%camel fname%> = (<%pascal f.type%>)input.ReadEnum();
<%indent%>    break;
<%#elif csharp_type.<%f.type%>%>
<%indent%>case <%f.tag%>u:
<%indent%>    _<%camel fname%> = input.<%read_method f.type%>();
<%indent%>    break;
<%#else%>
<%indent%>case <%f.tag%>u:
<%indent%>    _<%camel fname%> ??= new();
<%indent%>    input.ReadMessage(_<%camel fname%>);
<%indent%>    break;
<%/if%>
<%/if%>
<%/define%>

<%# ═══════════════════════════════════════════════ %>
<%# Copy constructor field                          %>
<%# ═══════════════════════════════════════════════ %>

<%#define emit_copy(fname, f, indent)%>
<%#if f.label == "repeated"%>
<%indent%>_<%camel fname%>.Add(other._<%camel fname%>);
<%#elif csharp_type.<%f.type%>%>
<%indent%>_<%camel fname%> = other._<%camel fname%>;
<%#elif enums.<%f.type%>%>
<%indent%>_<%camel fname%> = other._<%camel fname%>;
<%#else%>
<%indent%>if (other._<%camel fname%> != null)
<%indent%>    _<%camel fname%> = other._<%camel fname%>.Clone();
<%/if%>
<%/define%>

<%# ═══════════════════════════════════════════════ %>
<%# MergeFrom field                                 %>
<%# ═══════════════════════════════════════════════ %>

<%#define emit_merge(fname, f, indent)%>
<%#if f.label == "repeated"%>
<%indent%>_<%camel fname%>.Add(other._<%camel fname%>);
<%#elif csharp_type.<%f.type%>%>
<%indent%>if (other._<%camel fname%> != <%csharp_default f.type%>)
<%indent%>    _<%camel fname%> = other._<%camel fname%>;
<%#elif enums.<%f.type%>%>
<%indent%>if (other._<%camel fname%> != 0)
<%indent%>    _<%camel fname%> = other._<%camel fname%>;
<%#else%>
<%indent%>if (other._<%camel fname%> != null)
<%indent%>{
<%indent%>    _<%camel fname%> ??= new();
<%indent%>    _<%camel fname%>.MergeFrom(other._<%camel fname%>);
<%indent%>}
<%/if%>
<%/define%>
```

---

## 5. Example

### Input: `acme/widgets.proto`

```proto
syntax = "proto3";
package acme.widgets;

enum WidgetType {
  WIDGET_TYPE_UNKNOWN = 0;
  SPROCKET = 1;
  GEAR = 2;
  COG = 3;
}

message Widget {
  string name = 1;
  int32 quantity = 2;
  WidgetType type = 3;
  repeated string tags = 4;
  map<string, string> metadata = 5;

  message Dimension {
    float width = 1;
    float height = 2;
    float depth = 3;
  }

  Dimension dimensions = 6;

  oneof variant {
    string color = 7;
    int32 material_code = 8;
  }
}

service WidgetService {
  rpc GetWidget (WidgetRequest) returns (Widget);
  rpc ListWidgets (ListWidgetsRequest) returns (stream Widget);
}

message WidgetRequest {
  string name = 1;
}

message ListWidgetsRequest {
  WidgetType type_filter = 1;
  int32 page_size = 2;
  string page_token = 3;
}
```

### Intermediate: `widgets.hjson`

```hjson
{
  syntax: proto3
  package: acme.widgets

  enums: {
    WidgetType: {
      WIDGET_TYPE_UNKNOWN: 0
      SPROCKET: 1
      GEAR: 2
      COG: 3
    }
  }

  messages: {
    Widget: {
      fields: {
        name:     { type: string,     tag: 1 }
        quantity: { type: int32,      tag: 2 }
        type:     { type: WidgetType, tag: 3 }
        tags:     { type: string,     tag: 4, label: repeated }
      }
      maps: {
        metadata: { key_type: string, value_type: string, tag: 5 }
      }
      nested_messages: {
        Dimension: {
          fields: {
            width:  { type: float, tag: 1 }
            height: { type: float, tag: 2 }
            depth:  { type: float, tag: 3 }
          }
        }
      }
      fields_continued: {
        dimensions: { type: Dimension, tag: 6 }
      }
      oneofs: {
        variant: {
          color:         { type: string, tag: 7 }
          material_code: { type: int32,  tag: 8 }
        }
      }
    }

    WidgetRequest: {
      fields: {
        name: { type: string, tag: 1 }
      }
    }

    ListWidgetsRequest: {
      fields: {
        type_filter: { type: WidgetType, tag: 1 }
        page_size:   { type: int32,      tag: 2 }
        page_token:  { type: string,     tag: 3 }
      }
    }
  }

  services: {
    WidgetService: {
      GetWidget:   { input: WidgetRequest,      output: Widget }
      ListWidgets: { input: ListWidgetsRequest,  output: Widget, server_streaming: true }
    }
  }
}
```

That's it. No `kind` discriminators, no `@Type` tags, no arrays of objects with `name` fields. Every name is a map key. Every enum is just `{ NAME: number }`. Every field is just `{ type: x, tag: n }`. Every service method is just `{ input: x, output: y }`.

The template resolves `type: WidgetType` by checking `enums.WidgetType` — which exists — so it emits enum code. It resolves `type: Dimension` by checking `enums.Dimension` — which doesn't exist — then `csharp_type.Dimension` — which doesn't exist — so it falls through to message ref code.

### Output: `Widget.g.cs`

```csharp
// <auto-generated>
//   Generated by Yeetcode from acme.widgets.proto
// </auto-generated>
#nullable enable

using System;
using System.Collections.Generic;
using Google.Protobuf;
using Google.Protobuf.Collections;

namespace Acme.Widgets
{
    public sealed partial class Widget : IMessage<Widget>, IBufferMessage
    {
        public const int NameFieldNumber = 1;
        public const int QuantityFieldNumber = 2;
        public const int TypeFieldNumber = 3;
        public const int TagsFieldNumber = 4;
        public const int MetadataFieldNumber = 5;
        public const int DimensionsFieldNumber = 6;
        public const int ColorFieldNumber = 7;
        public const int MaterialCodeFieldNumber = 8;

        private string _name = "";
        public string Name
        {
            get => _name;
            set => _name = value;
        }

        private int _quantity = 0;
        public int Quantity
        {
            get => _quantity;
            set => _quantity = value;
        }

        private WidgetType _type = 0;
        public WidgetType Type
        {
            get => _type;
            set => _type = value;
        }

        private readonly RepeatedField<string> _tags = new();
        public RepeatedField<string> Tags => _tags;

        private readonly MapField<string, string> _metadata = new();
        public MapField<string, string> Metadata => _metadata;

        private Dimension? _dimensions;
        public Dimension? Dimensions
        {
            get => _dimensions;
            set => _dimensions = value;
        }

        public enum VariantOneofCase
        {
            None = 0,
            Color = 7,
            MaterialCode = 8,
        }

        private VariantOneofCase _variantCase = VariantOneofCase.None;
        private object? _variantValue;
        public VariantOneofCase VariantCase => _variantCase;

        public string Color
        {
            get => _variantCase == VariantOneofCase.Color ? (string)_variantValue! : "";
            set { _variantValue = value; _variantCase = VariantOneofCase.Color; }
        }

        public int MaterialCode
        {
            get => _variantCase == VariantOneofCase.MaterialCode ? (int)_variantValue! : 0;
            set { _variantValue = value; _variantCase = VariantOneofCase.MaterialCode; }
        }

        public void ClearVariant()
        {
            _variantCase = VariantOneofCase.None;
            _variantValue = null;
        }

        public Widget() { }

        public Widget(Widget other)
        {
            _name = other._name;
            _quantity = other._quantity;
            _type = other._type;
            _tags.Add(other._tags);
            _metadata.Add(other._metadata);
            if (other._dimensions != null)
                _dimensions = other._dimensions.Clone();
        }

        public Widget Clone() => new(this);

        public void WriteTo(CodedOutputStream output)
        {
            if (_name != "")
            {
                output.WriteRawTag(1);
                output.WriteString(_name);
            }
            if (_quantity != 0)
            {
                output.WriteRawTag(2);
                output.WriteInt32(_quantity);
            }
            if (_type != 0)
            {
                output.WriteRawTag(3);
                output.WriteEnum((int)_type);
            }
            _tags.WriteTo(output, _repeated_tags_codec);
        }

        public void MergeFrom(CodedInputStream input)
        {
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                switch (tag)
                {
                    case 1u:
                        _name = input.ReadString();
                        break;
                    case 2u:
                        _quantity = input.ReadInt32();
                        break;
                    case 3u:
                        _type = (WidgetType)input.ReadEnum();
                        break;
                    case 6u:
                        _dimensions ??= new();
                        input.ReadMessage(_dimensions);
                        break;
                    default:
                        input.SkipLastField();
                        break;
                }
            }
        }

        public void MergeFrom(Widget other)
        {
            if (other._name != "")
                _name = other._name;
            if (other._quantity != 0)
                _quantity = other._quantity;
            if (other._type != 0)
                _type = other._type;
            _tags.Add(other._tags);
            _metadata.Add(other._metadata);
            if (other._dimensions != null)
            {
                _dimensions ??= new();
                _dimensions.MergeFrom(other._dimensions);
            }
        }

        public int CalculateSize()
        {
            int size = 0;
            if (_name != "")
                size += CodedOutputStream.ComputeTagSize(1) + CodedOutputStream.ComputeStringSize(_name);
            if (_quantity != 0)
                size += CodedOutputStream.ComputeTagSize(2) + CodedOutputStream.ComputeInt32Size(_quantity);
            if (_type != 0)
                size += CodedOutputStream.ComputeTagSize(3) + CodedOutputStream.ComputeEnumSize((int)_type);
            if (_dimensions != null)
                size += CodedOutputStream.ComputeTagSize(6) + CodedOutputStream.ComputeMessageSize(_dimensions);
            return size;
        }

        public sealed partial class Dimension : IMessage<Dimension>, IBufferMessage
        {
            public const int WidthFieldNumber = 1;
            public const int HeightFieldNumber = 2;
            public const int DepthFieldNumber = 3;

            private float _width = 0F;
            public float Width { get => _width; set => _width = value; }

            private float _height = 0F;
            public float Height { get => _height; set => _height = value; }

            private float _depth = 0F;
            public float Depth { get => _depth; set => _depth = value; }

            public Dimension() { }

            public Dimension(Dimension other)
            {
                _width = other._width;
                _height = other._height;
                _depth = other._depth;
            }

            public Dimension Clone() => new(this);

            // WriteTo, MergeFrom, CalculateSize follow same pattern...
        }
    }
}
```

---

## 6. What changed from v1

| v1 (AST-oriented) | v2 (data-oriented) |
|---|---|
| `@Field` discriminated union with 5 variants | One `@Field` type, template resolves category |
| `kind: @ScalarField`, `kind: @EnumRef`, etc. | `<%#if enums.<%f.type%>%>` in template |
| Enums as `[{ name: SPROCKET, number: 1 }]` | Enums as `{ SPROCKET: 1 }` |
| Messages as `[{ name: Widget, fields: [...] }]` | Messages as `{ Widget: { fields: {...} } }` |
| Services as array of objects | Services as `{ WidgetService: { GetWidget: {...} } }` |
| Field names stored in `name` property | Field names are map keys |
| `#each` iterates arrays of named objects | `#each` iterates maps as `key, value` |
| `exists_in()` function needed for type lookup | Path existence check: `enums.<%type%>` |
| ~150 lines of schema with 12 `@Types` | ~50 lines of schema with 5 `@Types` |

The schema shrank 3x because maps eliminated the need for named-object wrappers. The template got simpler because type resolution is just path existence, not discriminated union dispatch. The HJSON intermediate reads like something a human would write.