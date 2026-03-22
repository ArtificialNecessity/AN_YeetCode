using System.Text.Json;
using YeetCode.Grammar;
using YeetCode.Schema;
using YeetCode.Template;
using YeetJson;

namespace YeetCode.Pipeline;

/// <summary>
/// Options for the "generate" command — full pipeline:
/// grammar + input → HJSON → validate → template → output
/// </summary>
public class GenerateOptions
{
    public required string SchemaFilePath { get; init; }
    public required string GrammarFilePath { get; init; }
    public required string InputFilePath { get; init; }
    public required string TemplateFilePath { get; init; }
    public string? FunctionsFilePath { get; init; }
    public string? SingleOutputFilePath { get; init; }
    public string? OutputDirectoryPath { get; init; }
    public Dictionary<string, string>? GrammarDefines { get; init; }
}

/// <summary>
/// Options for the "parse" command — grammar + input → validated HJSON
/// </summary>
public class ParseOptions
{
    public required string SchemaFilePath { get; init; }
    public required string GrammarFilePath { get; init; }
    public required string InputFilePath { get; init; }
    public string? OutputFilePath { get; init; }
    public Dictionary<string, string>? GrammarDefines { get; init; }
}

/// <summary>
/// Options for the "template" command — HJSON data → template → output
/// (the "half yeet" — no grammar parsing)
/// </summary>
public class TemplateOptions
{
    public string? SchemaFilePath { get; init; }
    public required string DataFilePath { get; init; }
    public required string TemplateFilePath { get; init; }
    public string? FunctionsFilePath { get; init; }
    public string? SingleOutputFilePath { get; init; }
    public string? OutputDirectoryPath { get; init; }
}

/// <summary>
/// Options for the "validate" command — check HJSON data against schema
/// </summary>
public class ValidateOptions
{
    public required string SchemaFilePath { get; init; }
    public required string DataFilePath { get; init; }
}

/// <summary>
/// Result of a pipeline operation.
/// </summary>
public class PipelineResult
{
    public bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }
    public Dictionary<string, string>? GeneratedFiles { get; init; }
    public string? SingleFileOutput { get; init; }
    public string? ParsedHjsonOutput { get; init; }
    public List<string>? ValidationErrors { get; init; }
}

/// <summary>
/// Orchestrates the YeetCode pipeline stages. Used by both CLI and MSBuild task.
/// All file I/O is done here — callers just provide paths.
/// </summary>
public static class YeetCodePipeline
{
    /// <summary>
    /// Full pipeline: grammar + input → parse → validate → template → output
    /// </summary>
    public static PipelineResult Generate(GenerateOptions generateOptions)
    {
        // 1. Load schema
        var loadedSchema = SchemaLoader.LoadFromFile(generateOptions.SchemaFilePath);

        // 2. Parse input with grammar
        var parsedDataDocument = ParseInputWithGrammar(
            generateOptions.GrammarFilePath,
            generateOptions.InputFilePath,
            generateOptions.GrammarDefines);

        // 3. Validate and apply defaults
        var validatedDataDocument = SchemaValidator.ValidateAndApplyDefaults(
            parsedDataDocument, loadedSchema);

        // 4. Run template
        return RunTemplate(
            validatedDataDocument,
            generateOptions.TemplateFilePath,
            generateOptions.FunctionsFilePath,
            generateOptions.SingleOutputFilePath,
            generateOptions.OutputDirectoryPath);
    }

    /// <summary>
    /// Parse only: grammar + input → validated HJSON output
    /// </summary>
    public static PipelineResult Parse(ParseOptions parseOptions)
    {
        // 1. Load schema
        var loadedSchema = SchemaLoader.LoadFromFile(parseOptions.SchemaFilePath);

        // 2. Parse input with grammar
        var parsedDataDocument = ParseInputWithGrammar(
            parseOptions.GrammarFilePath,
            parseOptions.InputFilePath,
            parseOptions.GrammarDefines);

        // 3. Validate and apply defaults
        var validatedDataDocument = SchemaValidator.ValidateAndApplyDefaults(
            parsedDataDocument, loadedSchema);

        // 4. Serialize to JSON (HJSON output not yet implemented, use JSON)
        string jsonOutput = JsonSerializer.Serialize(
            validatedDataDocument.RootElement,
            new JsonSerializerOptions { WriteIndented = true });

        // 5. Write output
        if (parseOptions.OutputFilePath != null) {
            File.WriteAllText(parseOptions.OutputFilePath, jsonOutput);
        }

        return new PipelineResult
        {
            Succeeded = true,
            ParsedHjsonOutput = jsonOutput
        };
    }

    /// <summary>
    /// Template only: HJSON data → template → output (the "half yeet")
    /// </summary>
    public static PipelineResult RunTemplateCommand(TemplateOptions templateOptions)
    {
        // 1. Parse HJSON data file
        var dataDocument = ParseHjsonFile(templateOptions.DataFilePath);

        // 2. Optionally validate against schema
        if (templateOptions.SchemaFilePath != null) {
            var loadedSchema = SchemaLoader.LoadFromFile(templateOptions.SchemaFilePath);
            dataDocument = SchemaValidator.ValidateAndApplyDefaults(dataDocument, loadedSchema);
        }

        // 3. Run template
        return RunTemplate(
            dataDocument,
            templateOptions.TemplateFilePath,
            templateOptions.FunctionsFilePath,
            templateOptions.SingleOutputFilePath,
            templateOptions.OutputDirectoryPath);
    }

    /// <summary>
    /// Validate only: check HJSON data against schema
    /// </summary>
    public static PipelineResult Validate(ValidateOptions validateOptions)
    {
        var loadedSchema = SchemaLoader.LoadFromFile(validateOptions.SchemaFilePath);
        var dataDocument = ParseHjsonFile(validateOptions.DataFilePath);

        var validationErrors = SchemaValidator.Validate(dataDocument, loadedSchema);

        return new PipelineResult
        {
            Succeeded = validationErrors.Count == 0,
            ValidationErrors = validationErrors,
            ErrorMessage = validationErrors.Count > 0
                ? $"Validation failed with {validationErrors.Count} error(s):\n" +
                  string.Join("\n", validationErrors.Select(e => $"  • {e}"))
                : null
        };
    }

    // ── Internal helpers ──────────────────────────────────

    private static JsonDocument ParseInputWithGrammar(
        string grammarFilePath,
        string inputFilePath,
        Dictionary<string, string>? grammarDefines)
    {
        string grammarSourceText = File.ReadAllText(grammarFilePath);
        string inputText = File.ReadAllText(inputFilePath);

        // Preprocess grammar (resolve %define, %if/%else/%endif)
        var grammarPreprocessor = new GrammarPreprocessor(grammarDefines);
        string preprocessedGrammarText = grammarPreprocessor.Preprocess(grammarSourceText);

        // Lex and parse grammar
        var grammarLexer = new GrammarLexer(preprocessedGrammarText);
        var grammarTokens = grammarLexer.Tokenize();
        var grammarParser = new GrammarParser();
        var parsedGrammar = grammarParser.Parse(grammarTokens);

        // Interpret grammar against input
        var pegInterpreter = new PegInterpreter(parsedGrammar);
        return pegInterpreter.Parse(inputText);
    }

    private static JsonDocument ParseHjsonFile(string hjsonFilePath)
    {
        string hjsonText = File.ReadAllText(hjsonFilePath);

        var structuralAnalyzer = new StructuralAnalyzer();
        var structureResult = structuralAnalyzer.Analyze(hjsonText);

        if (structureResult.StructuralErrors.Count > 0) {
            var diagnosticFormatter = new DiagnosticFormatter();
            var diagnosticOutput = diagnosticFormatter.FormatForAI(
                new ParseResult(null, new(), structureResult.StructuralErrors),
                hjsonText);
            throw new InvalidOperationException(
                $"HJSON file '{hjsonFilePath}' has structural errors:\n{diagnosticOutput}");
        }

        var hjsonContentParser = new HjsonContentParser(new HjsonParserOptions
        {
            EmitKeyAttributes = true
        });
        var parseResult = hjsonContentParser.Parse(hjsonText, structureResult);

        if (parseResult.ParsedDocument == null) {
            throw new InvalidOperationException(
                $"HJSON file '{hjsonFilePath}' parsed to null — check for structural errors");
        }

        return parseResult.ParsedDocument;
    }

    private static PipelineResult RunTemplate(
        JsonDocument validatedDataDocument,
        string templateFilePath,
        string? functionsFilePath,
        string? singleOutputFilePath,
        string? outputDirectoryPath)
    {
        string templateSourceText = File.ReadAllText(templateFilePath);

        // Parse functions file if provided
        JsonDocument? functionsDocument = null;
        if (functionsFilePath != null) {
            functionsDocument = ParseHjsonFile(functionsFilePath);
        }

        // Parse template
        var templateParser = new TemplateParser();
        var templateNodes = templateParser.Parse(templateSourceText);

        // Evaluate template
        var templateEvaluator = new TemplateEvaluator(validatedDataDocument, functionsDocument);

        // Check if template has output directives (multi-file mode)
        var generatedFiles = templateEvaluator.EvaluateMultiFile(templateNodes);

        if (generatedFiles.Count > 0) {
            // Multi-file mode
            if (singleOutputFilePath != null && outputDirectoryPath == null) {
                throw new InvalidOperationException(
                    "Template uses multi-file output directives but --output was specified instead of --outdir. " +
                    "Use --outdir to specify the output directory for multi-file templates.");
            }

            if (outputDirectoryPath != null) {
                Directory.CreateDirectory(outputDirectoryPath);
                foreach (var (fileName, fileContent) in generatedFiles) {
                    string fullOutputPath = Path.Combine(outputDirectoryPath, fileName);
                    string? outputSubDirectory = Path.GetDirectoryName(fullOutputPath);
                    if (outputSubDirectory != null) {
                        Directory.CreateDirectory(outputSubDirectory);
                    }
                    File.WriteAllText(fullOutputPath, fileContent);
                }
            }

            return new PipelineResult
            {
                Succeeded = true,
                GeneratedFiles = generatedFiles
            };
        } else {
            // Single-file mode — re-evaluate for single output
            string singleOutput = templateEvaluator.Evaluate(templateNodes);

            if (singleOutputFilePath != null) {
                string? outputSubDirectory = Path.GetDirectoryName(singleOutputFilePath);
                if (outputSubDirectory != null && outputSubDirectory.Length > 0) {
                    Directory.CreateDirectory(outputSubDirectory);
                }
                File.WriteAllText(singleOutputFilePath, singleOutput);
            } else if (outputDirectoryPath != null) {
                // Single file to outdir with default name
                Directory.CreateDirectory(outputDirectoryPath);
                string defaultFileName = Path.GetFileNameWithoutExtension(templateFilePath) + ".out";
                File.WriteAllText(Path.Combine(outputDirectoryPath, defaultFileName), singleOutput);
            }

            return new PipelineResult
            {
                Succeeded = true,
                SingleFileOutput = singleOutput
            };
        }
    }
}