namespace YeetCode.Grammar;

// ── Grammar AST Types ────────────────────────────────────
//
// Grammar syntax (.yeet files):
//   rule_name: expression -> @Type
//   TOKEN_NAME: /regex/
//   %skip: /pattern/
//   %define name
//   %if expr / %else / %endif
//
// PEG Expressions:
//   "literal"              — literal string match
//   TOKEN_NAME             — token reference (uppercase)
//   rule_name              — rule reference (lowercase)
//   name:expr              — named capture
//   a b c                  — sequence
//   a | b | c              — ordered choice
//   e*                     — zero or more
//   e+                     — one or more
//   e?                     — optional
//   (a b)                  — grouping

/// <summary>
/// A complete parsed grammar with rules, tokens, skip pattern, and directives.
/// </summary>
public class ParsedGrammar
{
    /// <summary>Parser rules (lowercase names) mapping to their definitions.</summary>
    public required Dictionary<string, GrammarRule> ParserRules { get; init; }
    
    /// <summary>Lexer tokens (UPPERCASE names) mapping to their regex patterns.</summary>
    public required Dictionary<string, TokenDefinition> LexerTokens { get; init; }
    
    /// <summary>Skip pattern for whitespace/comments, consumed implicitly between tokens.</summary>
    public string? SkipPattern { get; init; }
    
    /// <summary>Grammar parameters declared with %define.</summary>
    public required List<string> DefinedParameters { get; init; }
    
    /// <summary>Source file path for error reporting.</summary>
    public string? SourceFilePath { get; init; }
}

/// <summary>
/// A single grammar rule: rule_name: expression -> mappings
/// </summary>
public class GrammarRule
{
    public required string RuleName { get; init; }
    public required PegExpression Expression { get; init; }
    public required List<TypeMapping> TypeMappings { get; init; }
    public int SourceLine { get; init; }
}

/// <summary>
/// A lexer token definition: TOKEN_NAME: /regex/flags?
/// </summary>
public class TokenDefinition
{
    public required string TokenName { get; init; }
    public required string RegexPattern { get; init; }
    public string? RegexFlags { get; init; }  // e.g., "s" for dotall
    public int SourceLine { get; init; }
}

/// <summary>
/// Type mapping: -> @Type, -> @Type { kind: @Variant }, -> path[], -> path[capture]
/// </summary>
public class TypeMapping
{
    /// <summary>Target type name (without @), e.g., "MessageField"</summary>
    public string? TargetTypeName { get; init; }
    
    /// <summary>Discriminator variant (without @), e.g., "Binary" for { kind: @Binary }</summary>
    public string? KindVariantName { get; init; }
    
    /// <summary>Schema path for array/map insertion, e.g., "messages", "messages[].fields"</summary>
    public string? SchemaPath { get; init; }
    
    /// <summary>Capture name for map key insertion, e.g., "name" in messages[name]</summary>
    public string? MapKeyCaptureName { get; init; }
    
    /// <summary>True if this is an array append (path[]), false if map insert (path[key])</summary>
    public bool IsArrayAppend { get; init; }
}

// ── PEG Expression Types ─────────────────────────────────

/// <summary>Base class for all PEG expression AST nodes.</summary>
public abstract class PegExpression
{
    public int SourceLine { get; init; }
    public int SourceColumn { get; init; }
}

/// <summary>Literal string match: "text"</summary>
public class LiteralExpression : PegExpression
{
    public required string LiteralText { get; init; }
}

/// <summary>Token reference: TOKEN_NAME (uppercase identifier)</summary>
public class TokenRefExpression : PegExpression
{
    public required string TokenName { get; init; }
}

/// <summary>Rule reference: rule_name (lowercase identifier)</summary>
public class RuleRefExpression : PegExpression
{
    public required string RuleName { get; init; }
}

/// <summary>Named capture: name:expr</summary>
public class CaptureExpression : PegExpression
{
    public required string CaptureName { get; init; }
    public required PegExpression CapturedExpression { get; init; }
}

/// <summary>Sequence: a b c (match all in order)</summary>
public class SequenceExpression : PegExpression
{
    public required List<PegExpression> SequenceElements { get; init; }
}

/// <summary>Ordered choice: a | b | c (try left to right, take first match)</summary>
public class ChoiceExpression : PegExpression
{
    public required List<PegExpression> ChoiceAlternatives { get; init; }
}

/// <summary>Repetition: e*, e+, e?</summary>
public class RepeatExpression : PegExpression
{
    public required PegExpression RepeatedExpression { get; init; }
    public required RepeatMode Mode { get; init; }
}

public enum RepeatMode
{
    ZeroOrMore,   // e*
    OneOrMore,    // e+
    Optional      // e?
}

/// <summary>Grouping: (expr)</summary>
public class GroupExpression : PegExpression
{
    public required PegExpression GroupedExpression { get; init; }
}

// ── Preprocessor Directive Types ─────────────────────────

/// <summary>Preprocessor conditional block: %if expr / %else / %endif</summary>
public class ConditionalDirective
{
    public required string Condition { get; init; }  // e.g., "syntax == proto3"
    public required List<string> ThenBranchLines { get; init; }
    public List<string>? ElseBranchLines { get; init; }
    public int SourceLine { get; init; }
}