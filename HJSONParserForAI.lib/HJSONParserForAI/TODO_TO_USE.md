# TODO: Make Our HJSON Parser Production-Ready

**Goal:** Replace the third-party `Hjson` library in [`CrucibleLoader.cs`](../../apiserver/Crucible/Loader/CrucibleLoader.cs) with our custom two-phase parser.

**Status:** Phase 1 (structural analysis) is ✅ COMPLETE. Phase 2 (content parsing) is ❌ STUB ONLY.

---

## Confirmed: Standard HJSON Multiline Syntax

✅ **Standard HJSON uses `"""` for multiline strings** (triple double-quote)
- NOT `'''` (triple single-quote)
- Our spec now correctly reflects this

---

## Current State

### ✅ What Works (Phase 1)
- Delimiter tracking (braces, brackets, quotes)
- Error detection (unclosed, mismatched, unmatched)
- Repair hypothesis generation with confidence scores
- Region isolation (healthy vs damaged)
- AI-friendly diagnostic formatting

### ❌ What's Missing (Phase 2)
- Actual HJSON content parsing
- JsonDocument output for compatibility with our typed system
- Unquoted string handling
- Comment parsing
- Value type detection

---

## Tasks Required Before We Can Use Our Parser

### 1. **Implement Phase 2 Content Parser** 
**File:** [`Core/HjsonParser.cs`](Core/HjsonParser.cs)

**Current:** Returns stub `Dictionary<string, object>` with status placeholders
**Need:** Full recursive descent parser that produces `JsonDocument`

Methods to implement:
```csharp
private JsonElement ParseValue(int offset, ref int i)
private JsonElement ParseObject(int offset, ref int i)
private JsonElement ParseArray(int offset, ref int i)
private string ParseUnquotedString(int offset, ref int i)
private string ParseQuotedString(int offset, ref int i, bool multiline)
private JsonElement ParseNumber(int offset, ref int i)
private JsonElement ParseKeyword(int offset, ref int i)  // true, false, null
```

**Complexity:** Medium - recursive descent is well-understood pattern

---

### 2. **Return JsonDocument Instead of Dictionary**
**Why:** Our codebase now uses `Dictionary<string, JsonElement>` for type safety

**Change:**
```csharp
// BEFORE
public ParseResult Parse(...) {
    object? parsedValue = new Dictionary<string, object> { ... };
    return new ParseResult(parsedValue, ...);
}

// AFTER
public ParseResult Parse(...) {
    JsonDocument parsedDocument = BuildJsonDocument(...);
    return new ParseResult(parsedDocument.RootElement, ...);
}
```

---

### 3. **Handle HJSON-Specific Features**

#### a) **Unquoted Keys**
```hjson
{
  database: {      # Key is unquoted
    host: localhost
  }
}
```

#### b) **Unquoted String Values**
```hjson
{
  name: John Doe    # Value is unquoted (no quotes needed)
}
```

#### c) **Trailing Commas**
```hjson
{
  a: 1,
  b: 2,    # Trailing comma is valid
}
```

#### d) **Comments**
```hjson
// Line comment
{
  /* Block
     comment */
  key: value
}
```

#### e) **Multiline Strings with `"""`**
```hjson
{
  description: """
    This is a
    multiline
    string
  """
}
```

---

### 4. **Write Comprehensive Unit Tests**
**File:** `Tests/Phase2Tests.cs` (create)

**Need tests for:**
- [x] Valid HJSON with all features
- [ ] Unquoted keys and values
- [ ] Multiline strings with `"""`
- [ ] Comments (line and block)
- [ ] Trailing commas
- [ ] Nested objects and arrays
- [ ] Mixed quoted/unquoted content
- [ ] Edge cases (empty objects, empty arrays)

**Test Data:** Already exist in `Tests/TestData/` - need test runner

---

### 5. **Integration Changes**

**File:** [`apiserver/Crucible/Loader/CrucibleLoader.cs:58-60`](../../apiserver/Crucible/Loader/CrucibleLoader.cs:58-60)

**BEFORE (using third-party Hjson):**
```csharp
var hjsonContent = File.ReadAllText(hjsonFilePath);
var jsonValue = HjsonValue.Parse(hjsonContent);  // Third-party
jsonString = jsonValue.ToString(Stringify.Plain);
```

**AFTER (using our parser):**
```csharp
var hjsonContent = File.ReadAllText(hjsonFilePath);

// Phase 1: Structural analysis
var structuralAnalyzer = new StructuralAnalyzer();
var structure = structuralAnalyzer.Analyze(hjsonContent);

// If structural errors, format diagnostics for user
if (structure.StructuralErrors.Count > 0) {
    var formatter = new DiagnosticFormatter();
    var diagnostics = formatter.FormatForAI(
        new ParseResult(null, new(), structure.StructuralErrors),
        hjsonContent
    );
    throw new InvalidOperationException(
        $"Crucible '{crucibleName}' has structural errors:\n{diagnostics}"
    );
}

// Phase 2: Content parsing
var hjsonParser = new HjsonParser();
var parseResult = hjsonParser.Parse(hjsonContent, structure);

// Convert JsonDocument to JSON string for JsonSerializer.Deserialize<CrucibleDefinition>
jsonString = JsonSerializer.Serialize(parseResult.ParsedValue);
```

---

### 6. **Performance Verification**
**Goal:** Ensure parsing is fast enough for production

**Benchmarks needed:**
- Small crucible (<100 lines) - should be <1ms
- Large crucible (1000+ lines) - should be <10ms
- Broken file with errors - should be <50ms (includes hypothesis generation)

---

### 7. **Error Message Quality Test**
**Goal:** Verify diagnostics are actually helpful

**Test with real users/LLMs:**
1. Give intentionally broken HJSON to LLM
2. Show our diagnostic output
3. Verify LLM can fix the error without additional help

**Success criteria:**
- >80% of broken files fixed on first attempt
- LLM understands repair hypotheses without clarification

---

## Estimated Work

**Phase 2 Implementation:** 2-3 days
**Testing & Integration:** 1 day  
**Error Message Tuning:** 0.5 days

**Total:** ~4 days of focused development

---

## Why Do This?

### Benefits of Our Custom Parser

1. **Better Errors** - Hypothetical fix suggestions instead of "parse error at line X"
2. **Partial Parsing** - Extract valid data from partially broken files
3. **Type Safety** - Can output JsonDocument instead of Dictionary<string, object>
4. **Extensibility** - Easy to add custom HJSON extensions
5. **Debugging** - Can highlight exact problem locations with context

### When to Do This

**Not urgent** - Third-party Hjson library works fine for now.

**Trigger to implement:**
1. Users frequently write broken crucibles and need help debugging
2. We want to add HJSON extensions (custom syntax)
3. We need comment-preserving round-trip editing
4. Error messages from third-party lib become a pain point

---

## Next Steps

When ready to implement:
1. Switch to Code mode
2. Start with Phase 2 parser implementation
3. Test against existing crucibles
4. Gradually migrate CrucibleLoader
5. Remove third-party Hjson dependency

**No rush** - the architecture is ready, implementation is straightforward when needed.