# YeetCode VSCode Extension — TextMate Grammars & Language Support

## Phase 1: Extension Scaffold & HJSON Grammar

- [ ] Create `YeetCode.VSCode/` directory with `package.json`, `tsconfig.json`, `.vscodeignore`
- [ ] Register language IDs: `hjson`, `ytson`, `ytgmr`, `ytmpl`
- [ ] Register file associations: `*.hjson` → hjson, `*.ytson` → ytson, `*.ytgmr` → ytgmr, `*.ytmpl` → ytmpl
- [ ] Write TextMate grammar `syntaxes/hjson.tmLanguage.json` for HJSON
  - [ ] Unquoted string values (tolerant — no quotes required)
  - [ ] Quoted string values (double-quoted with escapes)
  - [ ] Multiline strings (`'''...'''`)
  - [ ] Keys (with colon separator)
  - [ ] Numbers (int, float)
  - [ ] Booleans (`true`, `false`, `null`)
  - [ ] Comments (`#` line, `//` line, `/* */` block)
  - [ ] Objects `{}` and arrays `[]`
  - [ ] Commas (optional — HJSON allows omitting them)
- [ ] Add language-configuration for HJSON (bracket pairs, auto-close, comment toggling)
- [ ] Verify with `greeting.ytdata.hjson` and `valid_simple.hjson` test files

## Phase 2: YTSON Grammar (Schema Files)

- [ ] Write TextMate grammar `syntaxes/ytson.tmLanguage.json` extending HJSON base
  - [ ] `@TypeName` definitions (keys starting with `@`)
  - [ ] `@TypeName` references in values
  - [ ] `@:` anonymous map entry marker
  - [ ] Key attributes `[optional]`, `[default:value]`, `[optional, default:value]`
  - [ ] Primitive type keywords as values: `string`, `int`, `float`, `bool`
  - [ ] Optional marker `?` on types
  - [ ] Array type syntax `[type]`, `[@TypeName]`
  - [ ] Map type syntax `{type}`, `{@TypeName}`
  - [ ] Freeform object `{}`
- [ ] Add language-configuration for YTSON
- [ ] Verify with `proto.schema.ytson` and `simple.ytschema.ytson` test files

## Phase 3: YTGMR Grammar (PEG Grammar Files)

- [ ] Write TextMate grammar `syntaxes/ytgmr.tmLanguage.json`
  - [ ] Rule definitions: `rule_name ::= expression`
  - [ ] Token definitions: `TOKEN_NAME ::= /regex/`
  - [ ] Lowercase rule names vs UPPERCASE token names (distinct scopes)
  - [ ] `::=` definition operator
  - [ ] String literals `"text"` in expressions
  - [ ] Regex patterns `/pattern/flags`
  - [ ] Named captures `name:expression`
  - [ ] PEG operators: `*`, `+`, `?`, `|`
  - [ ] Grouping `()`
  - [ ] Schema mapping arrow `->` and `@TypeName` targets
  - [ ] Directives: `%skip`, `%define`, `%if`, `%else`, `%endif`, `%parse_file`
  - [ ] Comments: `#` line comments, `//` line comments, `/* */` block comments
  - [ ] Path notation: `messages[]`, `messages[name]`
- [ ] Add language-configuration for YTGMR
- [ ] Verify with `simple.ytgmr` and `simple_proto.grammar.yeet` test files

## Phase 4: YTMPL Grammar (Template Files)

- [ ] Write TextMate grammar `syntaxes/ytmpl.tmLanguage.json`
  - [ ] Header line: `<?yt delim="OPEN CLOSE" ?>`
  - [ ] Delimited blocks `<% ... %>` (static — most common delimiter)
  - [ ] Directives inside delimiters: `each`, `if`, `elif`, `else`, `define`, `call`, `output`
  - [ ] Close directives: `/each`, `/if`, `/define`, `/output`
  - [ ] `as` keyword in each loops
  - [ ] `separator=` attribute
  - [ ] Value expressions: dot paths `msg.name`, bracket access `map[key]`
  - [ ] Built-in functions: `pascal`, `camel`, `snake`, `upper`, `lower`, `length`
  - [ ] `@TypeName` references in comparisons
  - [ ] String literals inside delimiters
  - [ ] `+` concatenation operator
  - [ ] Comparison operators `==`, `!=`
  - [ ] Optional access `?.`
  - [ ] Everything outside delimiters is literal text (no highlighting)
- [ ] Add language-configuration for YTMPL
- [ ] Verify with `simple.ytmpl`, `greeting.ytmpl` test files

## Phase 5: Polish & Packaging

- [ ] Add file icons for each language (optional)
- [ ] Add snippet definitions for common patterns (optional)
- [ ] Write README.md for the extension
- [ ] Test all grammars together in a workspace with real YeetCode files
- [ ] Package with `vsce package` for local install / marketplace publishing

## Future: Phase 6 — Language Server (LSP)

- [ ] Architect LSP server wrapping YeetCode.lib C# parsers
- [ ] Real-time diagnostics from existing lexers/parsers
- [ ] Schema-aware autocomplete for ytmpl (field paths from ytson schema)
- [ ] Go-to-definition for `@TypeName` references
- [ ] Dynamic delimiter detection for ytmpl files
- [ ] Hover info for grammar rules and template directives