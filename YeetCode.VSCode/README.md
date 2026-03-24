# YeetCode Language Support

Syntax highlighting for YeetCode file formats.

## Supported Languages

| Language | Extensions | Description |
|----------|-----------|-------------|
| **HJSON** | `.hjson` | Human JSON — tolerant data format with unquoted strings, optional commas, comments |
| **YTSON** | `.ytson` | Yeet JSON — HJSON with key attributes inspired by Clearsivler HDF |
| **YTGMR** | `.ytgmr`, `.yeet` | YeetCode PEG grammar files for parsing custom syntax |
| **YTMPL** | `.ytmpl`, `.yt` | YeetCode template files with configurable delimiters |

## Screenshots

### HJSON Data Files

![HJSON syntax highlighting](https://raw.githubusercontent.com/ArtificialNecessity/AN_YeetCode/main/YeetCode.VSCode/screenshots/hjson.png)

### YTSON Schema Files

![YTSON syntax highlighting](https://raw.githubusercontent.com/ArtificialNecessity/AN_YeetCode/main/YeetCode.VSCode/screenshots/ytson.png)

### YTGMR Grammar Files

![YTGMR syntax highlighting](https://raw.githubusercontent.com/ArtificialNecessity/AN_YeetCode/main/YeetCode.VSCode/screenshots/ytgmr.png)

### YTMPL Template Files

![YTMPL syntax highlighting](https://raw.githubusercontent.com/ArtificialNecessity/AN_YeetCode/main/YeetCode.VSCode/screenshots/ytmpl.png)

## Color Philosophy

- **HJSON**: Value-emphasized — keys are muted grey, unquoted values are bright blue, quoted strings are orange, numbers/booleans are green. Structure fades, data pops.
- **YTGMR**: Grammar-focused — `::=` is a keyword, regex patterns are teal/type-colored, string literals are orange, captures are light blue.
- **YTMPL**: Code-like — `<%`/`%>` delimiters are grey, directives are blue keywords, source paths are green, local variables are light blue.

All colors are theme-aware — they adapt to your active VSCode color theme.