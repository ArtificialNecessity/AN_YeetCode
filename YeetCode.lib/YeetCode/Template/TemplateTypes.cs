namespace YeetCode.Template;

// ── Template AST Nodes ───────────────────────────────────
//
// Template syntax (Option A — keyword-first, no prefix):
//   <?yt delim="<% %>" ?>
//   <% each messages as msg_name, msg %>
//   <% if f.label == "repeated" %>
//   <% elif condition %>
//   <% else %>
//   <% /if %>
//   <% /each %>
//   <% output pascal(msg_name) + ".cs" %>
//   <% /output %>
//   <% define render_expr(e) %>
//   <% /define %>
//   <% call render_expr(e.binary.left) %>
//   <% msg.name %>                    — value expression
//   <% pascal msg.name %>             — function call expression
//   <% csharp_type[f.type] %>         — bracket access expression

/// <summary>
/// Base class for all template AST nodes.
/// </summary>
public abstract class TemplateNode
{
    public int SourceLine { get; init; }
    public int SourceColumn { get; init; }
}

/// <summary>Raw text to emit verbatim.</summary>
public class LiteralTextNode : TemplateNode
{
    public required string Text { get; init; }
}

/// <summary>Value expression to evaluate and emit: <% expr %></summary>
public class ValueExpressionNode : TemplateNode
{
    public required TemplateExpression Expression { get; init; }
}

/// <summary>each block: <% each collection as item %> or <% each map as key, value %></summary>
public class EachBlockNode : TemplateNode
{
    public required TemplateExpression CollectionExpression { get; init; }
    public required string ItemVariableName { get; init; }
    /// <summary>For map iteration: the value variable name. Null for array iteration.</summary>
    public string? ValueVariableName { get; init; }
    /// <summary>Optional separator string emitted between items.</summary>
    public string? SeparatorText { get; init; }
    public required List<TemplateNode> BodyNodes { get; init; }
}

/// <summary>if / elif / else block</summary>
public class IfBlockNode : TemplateNode
{
    public required List<ConditionalBranch> Branches { get; init; }
    public List<TemplateNode>? ElseBodyNodes { get; init; }
}

/// <summary>A single condition + body pair in an if/elif chain.</summary>
public class ConditionalBranch
{
    public required TemplateExpression Condition { get; init; }
    public required List<TemplateNode> BodyNodes { get; init; }
}

/// <summary>define block: <% define name(args) %> ... <% /define %></summary>
public class DefineBlockNode : TemplateNode
{
    public required string MacroName { get; init; }
    public required List<string> ParameterNames { get; init; }
    public required List<TemplateNode> BodyNodes { get; init; }
}

/// <summary>call: <% call name(args) %></summary>
public class CallNode : TemplateNode
{
    public required string MacroName { get; init; }
    public required List<TemplateExpression> ArgumentExpressions { get; init; }
}

/// <summary>output block: <% output expr %> ... <% /output %></summary>
public class OutputBlockNode : TemplateNode
{
    public required TemplateExpression FileNameExpression { get; init; }
    public required List<TemplateNode> BodyNodes { get; init; }
}

// ── Expressions ──────────────────────────────────────────

/// <summary>Base class for template expressions.</summary>
public abstract class TemplateExpression { }

/// <summary>Dot-path access: msg.name, msg.fields.length, package</summary>
public class PathExpression : TemplateExpression
{
    public required List<string> PathSegments { get; init; }
    public bool HasOptionalAccess { get; init; }
    public override string ToString() => string.Join(".", PathSegments);
}

/// <summary>Bracket access: csharp_type[f.type], enums[f.type]</summary>
public class BracketAccessExpression : TemplateExpression
{
    public required TemplateExpression BaseExpression { get; init; }
    public required TemplateExpression IndexExpression { get; init; }
}

/// <summary>Function call: pascal(msg.name), upper(s), length(arr)</summary>
public class FunctionCallExpression : TemplateExpression
{
    public required string FunctionName { get; init; }
    public required List<TemplateExpression> ArgumentExpressions { get; init; }
}

/// <summary>String literal: "literal text"</summary>
public class StringLiteralExpression : TemplateExpression
{
    public required string Value { get; init; }
}

/// <summary>String concatenation: expr + expr</summary>
public class ConcatExpression : TemplateExpression
{
    public required TemplateExpression LeftExpression { get; init; }
    public required TemplateExpression RightExpression { get; init; }
}

/// <summary>Comparison: expr == expr, expr != expr</summary>
public class ComparisonExpression : TemplateExpression
{
    public required TemplateExpression LeftExpression { get; init; }
    public required string Operator { get; init; } // "==" or "!="
    public required TemplateExpression RightExpression { get; init; }
}

/// <summary>@TypeName reference in comparisons: e.kind == @Binary</summary>
public class TypeRefExpression : TemplateExpression
{
    public required string TypeName { get; init; }
}