namespace HJSONParserForAI.Core;

/// <summary>
/// Types of delimiters tracked during structural analysis
/// </summary>
public enum DelimiterType
{
    Brace,           // { }
    Bracket,         // [ ]
    Paren,           // ( ) - rare in HJSON but valid in values
    DoubleQuote,     // ""
    SingleQuote,     // ''
    TripleQuote      // ''' for multiline strings
}

/// <summary>
/// A delimiter occurrence in source text
/// </summary>
public record Delimiter(
    DelimiterType Type,
    int Line,
    int Column,
    int Offset,      // Character offset in source
    bool IsOpen,
    string Context   // Surrounding code for display
);

/// <summary>
/// Categories of structural errors
/// </summary>
public enum StructuralErrorKind
{
    UnclosedDelimiter,      // { never closed
    UnmatchedClose,         // } with no matching {
    MismatchedPair,         // { closed with ]
    UnclosedString,         // Quote opened, hit EOL
    AmbiguousNesting        // Multiple valid interpretations possible
}

/// <summary>
/// Repair actions that can fix structural errors
/// </summary>
public enum RepairAction
{
    Insert,   // Insert missing delimiter
    Delete,   // Remove spurious delimiter
    Replace   // Change delimiter type
}

/// <summary>
/// A suggested fix for a structural error
/// </summary>
public record RepairHypothesis(
    RepairAction Action,
    int Line,
    int Column,
    string Description,
    float Confidence,        // 0.0 - 1.0
    string? InsertText = null,
    DelimiterType? ReplaceWith = null
);

/// <summary>
/// A structural error with repair suggestions
/// </summary>
public record StructuralError(
    StructuralErrorKind Kind,
    Delimiter? Opener,
    Delimiter? Closer,
    int Line,
    int Column,
    string Message,
    List<RepairHypothesis> Hypotheses
);

/// <summary>
/// Health status of a code region
/// </summary>
public enum RegionHealth
{
    Healthy,      // Structurally valid
    Damaged,      // Contains or adjacent to errors
    Quarantined   // Too damaged to parse
}

/// <summary>
/// A region of source text with known health status
/// </summary>
public record Region(
    int StartOffset,
    int EndOffset,
    int StartLine,
    int EndLine,
    RegionHealth Health,
    StructuralError? RelatedError
);

/// <summary>
/// Results from Phase 1 structural analysis
/// </summary>
public record StructureResult(
    List<Delimiter> AllDelimiters,
    List<StructuralError> StructuralErrors,
    List<Region> Regions
);

/// <summary>
/// Semantic errors from Phase 2 content parsing
/// </summary>
public record SemanticError(
    string Kind,
    int Line,
    int Column,
    string Message,
    StructuralError? StructuralContext = null,
    string? Note = null
);

/// <summary>
/// Complete parse results
/// </summary>
public record ParseResult(
    object? ParsedValue,
    List<SemanticError> SemanticErrors,
    List<StructuralError> StructuralErrors
);