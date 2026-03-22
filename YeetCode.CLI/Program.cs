using YeetCode.Pipeline;

namespace YeetCode.CLI;

/// <summary>
/// YeetCode CLI — schema-driven meta-programming tool for language-to-language transformation.
///
/// Commands:
///   generate  — full pipeline: grammar + input → HJSON → template → output
///   parse     — grammar + input → validated HJSON
///   template  — HJSON data → template → output (the "half yeet")
///   validate  — check HJSON data against schema
/// </summary>
public static class Program
{
    public static int Main(string[] commandLineArgs)
    {
        if (commandLineArgs.Length == 0) {
            PrintUsage();
            return 1;
        }

        string commandName = commandLineArgs[0].ToLowerInvariant();
        string[] commandArgs = commandLineArgs[1..];

        return commandName switch
        {
            "generate" => RunGenerate(commandArgs),
            "parse" => RunParse(commandArgs),
            "template" => RunTemplate(commandArgs),
            "validate" => RunValidate(commandArgs),
            "help" or "--help" or "-h" => PrintUsageAndReturn0(),
            "version" or "--version" => PrintVersion(),
            _ => PrintUnknownCommand(commandName)
        };
    }

    // ── Command implementations ──────────────────────────

    private static int RunGenerate(string[] commandArgs)
    {
        var parsedArgs = ParseArguments(commandArgs);

        string schemaFilePath = RequireArg(parsedArgs, "schema", "generate");
        string grammarFilePath = RequireArg(parsedArgs, "grammar", "generate");
        string inputFilePath = RequireArg(parsedArgs, "input", "generate");
        string templateFilePath = RequireArg(parsedArgs, "template", "generate");
        string? functionsFilePath = GetOptionalArg(parsedArgs, "functions");
        string? singleOutputFilePath = GetOptionalArg(parsedArgs, "output");
        string? outputDirectoryPath = GetOptionalArg(parsedArgs, "outdir");
        var grammarDefines = ParseDefines(parsedArgs);

        var generateOptions = new GenerateOptions
        {
            SchemaFilePath = schemaFilePath,
            GrammarFilePath = grammarFilePath,
            InputFilePath = inputFilePath,
            TemplateFilePath = templateFilePath,
            FunctionsFilePath = functionsFilePath,
            SingleOutputFilePath = singleOutputFilePath,
            OutputDirectoryPath = outputDirectoryPath,
            GrammarDefines = grammarDefines.Count > 0 ? grammarDefines : null
        };

        var pipelineResult = YeetCodePipeline.Generate(generateOptions);

        if (pipelineResult.GeneratedFiles != null) {
            foreach (var (fileName, _) in pipelineResult.GeneratedFiles) {
                Console.WriteLine($"  → {fileName}");
            }
        } else if (pipelineResult.SingleFileOutput != null && singleOutputFilePath == null && outputDirectoryPath == null) {
            Console.Write(pipelineResult.SingleFileOutput);
        }

        return 0;
    }

    private static int RunParse(string[] commandArgs)
    {
        var parsedArgs = ParseArguments(commandArgs);

        string schemaFilePath = RequireArg(parsedArgs, "schema", "parse");
        string grammarFilePath = RequireArg(parsedArgs, "grammar", "parse");
        string inputFilePath = RequireArg(parsedArgs, "input", "parse");
        string? outputFilePath = GetOptionalArg(parsedArgs, "output");
        var grammarDefines = ParseDefines(parsedArgs);

        var parseOptions = new ParseOptions
        {
            SchemaFilePath = schemaFilePath,
            GrammarFilePath = grammarFilePath,
            InputFilePath = inputFilePath,
            OutputFilePath = outputFilePath,
            GrammarDefines = grammarDefines.Count > 0 ? grammarDefines : null
        };

        var pipelineResult = YeetCodePipeline.Parse(parseOptions);

        if (pipelineResult.ParsedHjsonOutput != null && outputFilePath == null) {
            Console.Write(pipelineResult.ParsedHjsonOutput);
        }

        return 0;
    }

    private static int RunTemplate(string[] commandArgs)
    {
        var parsedArgs = ParseArguments(commandArgs);

        string dataFilePath = RequireArg(parsedArgs, "data", "template");
        string templateFilePath = RequireArg(parsedArgs, "template", "template");
        string? schemaFilePath = GetOptionalArg(parsedArgs, "schema");
        string? functionsFilePath = GetOptionalArg(parsedArgs, "functions");
        string? singleOutputFilePath = GetOptionalArg(parsedArgs, "output");
        string? outputDirectoryPath = GetOptionalArg(parsedArgs, "outdir");

        var templateOptions = new TemplateOptions
        {
            SchemaFilePath = schemaFilePath,
            DataFilePath = dataFilePath,
            TemplateFilePath = templateFilePath,
            FunctionsFilePath = functionsFilePath,
            SingleOutputFilePath = singleOutputFilePath,
            OutputDirectoryPath = outputDirectoryPath
        };

        var pipelineResult = YeetCodePipeline.RunTemplateCommand(templateOptions);

        if (pipelineResult.GeneratedFiles != null) {
            foreach (var (fileName, _) in pipelineResult.GeneratedFiles) {
                Console.WriteLine($"  → {fileName}");
            }
        } else if (pipelineResult.SingleFileOutput != null && singleOutputFilePath == null && outputDirectoryPath == null) {
            Console.Write(pipelineResult.SingleFileOutput);
        }

        return 0;
    }

    private static int RunValidate(string[] commandArgs)
    {
        var parsedArgs = ParseArguments(commandArgs);

        string schemaFilePath = RequireArg(parsedArgs, "schema", "validate");
        string dataFilePath = RequireArg(parsedArgs, "data", "validate");

        var validateOptions = new ValidateOptions
        {
            SchemaFilePath = schemaFilePath,
            DataFilePath = dataFilePath
        };

        var pipelineResult = YeetCodePipeline.Validate(validateOptions);

        if (!pipelineResult.Succeeded) {
            Console.Error.WriteLine(pipelineResult.ErrorMessage);
            return 1;
        }

        Console.WriteLine("Validation passed.");
        return 0;
    }

    // ── Argument parsing ─────────────────────────────────

    private static Dictionary<string, List<string>> ParseArguments(string[] commandArgs)
    {
        var parsedArgMap = new Dictionary<string, List<string>>();
        int argIndex = 0;

        while (argIndex < commandArgs.Length) {
            string currentArg = commandArgs[argIndex];

            if (currentArg.StartsWith("--")) {
                string argName = currentArg[2..];

                // Handle --define name=value (can appear multiple times)
                if (argIndex + 1 < commandArgs.Length) {
                    string argValue = commandArgs[argIndex + 1];
                    if (!parsedArgMap.ContainsKey(argName)) {
                        parsedArgMap[argName] = new List<string>();
                    }
                    parsedArgMap[argName].Add(argValue);
                    argIndex += 2;
                } else {
                    throw new InvalidOperationException(
                        $"Argument '--{argName}' requires a value. Run 'yeetcode help' for usage.");
                }
            } else {
                throw new InvalidOperationException(
                    $"Unexpected argument '{currentArg}'. Arguments must start with '--'. " +
                    "Run 'yeetcode help' for usage.");
            }
        }

        return parsedArgMap;
    }

    private static string RequireArg(Dictionary<string, List<string>> parsedArgMap, string argName, string commandName)
    {
        if (!parsedArgMap.TryGetValue(argName, out var argValues) || argValues.Count == 0) {
            throw new InvalidOperationException(
                $"Missing required argument '--{argName}' for '{commandName}' command. " +
                $"Run 'yeetcode help' for usage.");
        }
        return argValues[0];
    }

    private static string? GetOptionalArg(Dictionary<string, List<string>> parsedArgMap, string argName)
    {
        if (parsedArgMap.TryGetValue(argName, out var argValues) && argValues.Count > 0) {
            return argValues[0];
        }
        return null;
    }

    private static Dictionary<string, string> ParseDefines(Dictionary<string, List<string>> parsedArgMap)
    {
        var grammarDefines = new Dictionary<string, string>();
        if (parsedArgMap.TryGetValue("define", out var defineValues)) {
            foreach (string defineValue in defineValues) {
                int equalsIndex = defineValue.IndexOf('=');
                if (equalsIndex > 0) {
                    string defineName = defineValue[..equalsIndex];
                    string defineVal = defineValue[(equalsIndex + 1)..];
                    grammarDefines[defineName] = defineVal;
                } else {
                    grammarDefines[defineValue] = "true";
                }
            }
        }
        return grammarDefines;
    }

    // ── Help and version ─────────────────────────────────

    private static void PrintUsage()
    {
        Console.Error.WriteLine("""
            YeetCode — schema-driven meta-programming tool

            Usage: yeetcode <command> [options]

            Commands:
              generate   Full pipeline: grammar + input → HJSON → template → output
              parse      Grammar + input → validated HJSON
              template   HJSON data → template → output (the "half yeet")
              validate   Check HJSON data against schema

            Generate options:
              --schema <file>      Schema file (.ytson)
              --grammar <file>     Grammar file (.yeet)
              --input <file>       Input file to parse
              --template <file>    Template file (.yt)
              --functions <file>   Functions/lookup tables file (.hjson)
              --output <file>      Single-file output path
              --outdir <dir>       Multi-file output directory
              --define <name=val>  Grammar parameter (repeatable)

            Parse options:
              --schema <file>      Schema file (.ytson)
              --grammar <file>     Grammar file (.yeet)
              --input <file>       Input file to parse
              --output <file>      Output HJSON file (stdout if omitted)
              --define <name=val>  Grammar parameter (repeatable)

            Template options:
              --data <file>        HJSON data file
              --template <file>    Template file (.yt)
              --schema <file>      Schema file (.ytson) — optional validation
              --functions <file>   Functions/lookup tables file (.hjson)
              --output <file>      Single-file output path
              --outdir <dir>       Multi-file output directory

            Validate options:
              --schema <file>      Schema file (.ytson)
              --data <file>        HJSON data file to validate
            """);
    }

    private static int PrintUsageAndReturn0()
    {
        PrintUsage();
        return 0;
    }

    private static int PrintVersion()
    {
        var assemblyVersion = typeof(Program).Assembly.GetName().Version;
        Console.WriteLine($"yeetcode {assemblyVersion}");
        return 0;
    }

    private static int PrintUnknownCommand(string commandName)
    {
        Console.Error.WriteLine($"Unknown command '{commandName}'. Run 'yeetcode help' for usage.");
        return 1;
    }
}